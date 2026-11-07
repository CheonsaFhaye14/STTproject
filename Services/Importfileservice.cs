using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Services;

/// <summary>
/// Stores an uploaded file once per distinct content and links the batch to it (ImportBatch.ImportFileId).
/// Every upload still gets its own ImportBatch; only the physical file and its ImportFile row are shared.
///
/// Safety rules:
///  - nothing is ever deleted or overwritten, and an existing file is reused only if its SHA-256 matches (see ImportFileStore.SaveToPathAsync);
///  - the file is written BEFORE the ImportFile row exists, so a row never points at a file that was never written;
///    if the database step then fails, the file is just an unreferenced, hash-named file that the next identical upload reuses;
///  - two uploads of the same content at the same moment are handled by the unique index on FileHash: the loser reads the winner's row;
///  - any failure returns null and the caller keeps the old per-batch save, so an upload is never lost because of this feature.
/// </summary>
public sealed class ImportFileService
{
    private const int FileNameMax = 255;      // ImportFile.FileName
    private const int StoragePathMax = 500;   // ImportFile.StoragePath

    private readonly IDbContextFactory<EntrielContext> _contextFactory;
    private readonly ImportFileStore _store;
    private readonly ILogger<ImportFileService> _logger;

    public ImportFileService(IDbContextFactory<EntrielContext> contextFactory, ImportFileStore store, ILogger<ImportFileService> logger)
    {
        _contextFactory = contextFactory;
        _store = store;
        _logger = logger;
    }

    /// <summary>The ImportFile a batch now points at. ReusedExistingFile is true when an identical file was already stored.</summary>
    public sealed record AttachResult(int ImportFileId, string FileHash, bool ReusedExistingFile);

    /// <summary>SHA-256 of the whole stream as uppercase hex (64 characters). The stream position is restored.</summary>
    public static async Task<string> ComputeHashAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var originalPosition = source.Position;
        try
        {
            source.Position = 0;
            return Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
        }
        finally
        {
            source.Position = originalPosition;
        }
    }

    /// <summary>
    /// Hashes the upload, stores the physical file if it is new, finds or creates its ImportFile row and links the batch to it.
    /// Returns null if any step failed; nothing has been lost in that case and the caller should save the file the old way.
    /// </summary>
    public async Task<AttachResult?> StoreAndAttachAsync(
        int importBatchId, string fileName, Stream source, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!source.CanRead || !source.CanSeek)
            {
                _logger.LogWarning("Import files: batch {ImportBatchId} was not deduplicated because the upload stream can't be rewound.", importBatchId);
                return null;
            }

            var hash = await ComputeHashAsync(source, cancellationToken);
            var size = source.Length;

            // The path comes from the hash alone, so identical bytes always map to one path. An existing row's path wins, so a
            // row is never left pointing at a file this upload did not check.
            var existing = await FindByHashAsync(hash, cancellationToken);
            var relativePath = existing?.StoragePath ?? ImportFileStore.RelativePathFor(hash);

            // Always make sure verified bytes are on disk. A file already there is reused only if its SHA-256 matches; a file
            // that went missing is put back; a mismatching file is never overwritten (null: the caller uses the legacy save).
            var saved = await _store.SaveToPathAsync(hash, relativePath, source, cancellationToken);
            if (saved is null) return null;

            var row = existing ?? await GetOrCreateAsync(hash, fileName, relativePath, size, cancellationToken);

            // Only possible if a concurrent upload registered this hash under a different path: verify (or restore) that path too.
            if (!string.Equals(row.StoragePath, relativePath, StringComparison.Ordinal)
                && await _store.SaveToPathAsync(hash, row.StoragePath, source, cancellationToken) is null)
                return null;

            var reused = existing is not null || saved.AlreadyExisted;

            // Link only a batch that is not linked yet, so an existing link is never changed.
            await using var context = _contextFactory.CreateDbContext();
            var linked = await context.ImportBatches
                .Where(b => b.ImportBatchId == importBatchId && b.ImportFileId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.ImportFileId, row.ImportFileId), cancellationToken);

            if (linked != 1)
            {
                _logger.LogWarning("Import files: batch {ImportBatchId} was not linked to file {ImportFileId} (batch missing or already linked).",
                    importBatchId, row.ImportFileId);
                return null;
            }

            return new AttachResult(row.ImportFileId, hash, reused);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Import files: could not store and link the file for batch {ImportBatchId}.", importBatchId);
            return null;
        }
    }

    private async Task<ImportFile?> FindByHashAsync(string hash, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.ImportFiles.AsNoTracking().FirstOrDefaultAsync(f => f.FileHash == hash, cancellationToken);
    }

    // Select-or-insert. UX_ImportFile_FileHash makes the insert fail for the loser of a race; that attempt re-reads and uses the winner's row.
    private async Task<ImportFile> GetOrCreateAsync(
        string hash, string fileName, string relativePath, long size, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var context = _contextFactory.CreateDbContext();

            var found = await context.ImportFiles.AsNoTracking().FirstOrDefaultAsync(f => f.FileHash == hash, cancellationToken);
            if (found is not null) return found;

            try
            {
                var entity = new ImportFile
                {
                    ImportFileId = await NextIdAsync(context, cancellationToken),
                    FileHash = hash,
                    FileName = Truncate(string.IsNullOrWhiteSpace(fileName) ? "(unknown)" : Path.GetFileName(fileName), FileNameMax),
                    StoragePath = Truncate(relativePath, StoragePathMax),
                    FileSize = size,
                    CreatedAt = DateTime.UtcNow
                };

                context.ImportFiles.Add(entity);
                await context.SaveChangesAsync(cancellationToken);
                return entity;
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                _logger.LogInformation("Import files: another upload registered hash {Hash} first; using its row.", hash);
            }
        }

        throw new InvalidOperationException("The ImportFile row for hash " + hash + " could not be created or found.");
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } };

    // Plain ADO.NET for the same reason as ImportLogService.NextIdAsync: EF would wrap raw SQL in a subquery, which NEXT VALUE FOR rejects.
    private static async Task<int> NextIdAsync(EntrielContext context, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR dbo.ImportFileIdSequence";

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}