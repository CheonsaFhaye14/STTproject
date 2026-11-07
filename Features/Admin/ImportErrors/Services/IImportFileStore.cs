using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.Admin.ImportErrors.Services
{
    public interface IImportFileStore
    {
        Task<byte[]?> GetOriginalFileAsync(int importBatchId, CancellationToken ct = default);
    }
    public sealed class DiskImportFileStore : IImportFileStore
    {
        private readonly STTproject.Services.ImportFileStore _store;
        private readonly IDbContextFactory<EntrielContext> _factory;
        private readonly ILogger<DiskImportFileStore> _logger;

        public DiskImportFileStore(
            STTproject.Services.ImportFileStore store, IDbContextFactory<EntrielContext> factory, ILogger<DiskImportFileStore> logger)
        {
            _store = store;
            _factory = factory;
            _logger = logger;
        }

        /// <summary>Which storage a batch's original file belongs to.</summary>
        private enum FileSource
        {
            /// <summary>There is no such batch, so no file is returned from anywhere (an orphaned folder is never used).</summary>
            BatchMissing,
            /// <summary>The batch exists and has no ImportFileId: its file, if any, is in its own per-batch folder.</summary>
            Legacy,
            /// <summary>The batch has an ImportFileId: only the verified shared file may be returned.</summary>
            Shared
        }

        /// <summary>
        /// Outcome of looking up a batch's storage. Bytes is only ever set for <see cref="FileSource.Shared"/>: the verified content,
        /// or null when that file could not be used. Shared with null Bytes covers every failure, including a database lookup
        /// that could not tell whether the batch exists or is linked at all.
        /// </summary>
        private sealed record SharedFileResult(FileSource Source, byte[]? Bytes);

        /// <summary>
        /// The batch's original upload.
        ///  - The batch does not exist: null, straight away. The legacy folder is not looked at, so an orphaned folder can't leak a file.
        ///  - The batch exists with no ImportFileId: a legacy batch, read from its own per-batch folder as before.
        ///  - ImportFileId set: the linked shared file is read and its SHA-256 must equal ImportFile.FileHash. If that file is
        ///    missing, unreadable or fails the hash check, the problem is logged and null is returned. The legacy folder is NOT
        ///    tried, so a batch can never be answered with some other file.
        ///  - The link lookup itself fails: logged, null is returned. The batch is not assumed to be a legacy batch.
        ///  - Cancellation is never swallowed.
        /// Download names and content types come from ImportBatch.FileName, not from the stored file.
        /// </summary>
        public async Task<byte[]?> GetOriginalFileAsync(int importBatchId, CancellationToken ct = default)
        {
            var shared = await ReadSharedAsync(importBatchId, ct);
            if (shared.Source == FileSource.BatchMissing) return null;
            if (shared.Source == FileSource.Shared) return shared.Bytes;

            // The batch exists and is not linked: a legacy batch.
            var path = _store.FindFile(importBatchId);
            if (path == null) return null;

            try
            {
                return await File.ReadAllBytesAsync(path, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Export: could not read stored file for batch {ImportBatchId} at {Path}.", importBatchId, path);
                return null;
            }
        }

        private async Task<SharedFileResult> ReadSharedAsync(int importBatchId, CancellationToken ct)
        {
            // 1. Does the batch exist, and is it linked? A failure here means the answer is unknown, which is treated as "shared, unavailable".
            int? importFileId;
            string? fileHash = null;
            string? storagePath = null;
            try
            {
                await using var ctx = await _factory.CreateDbContextAsync(ct);
                var batch = await ctx.ImportBatches.AsNoTracking()
                    .Where(b => b.ImportBatchId == importBatchId)
                    .Select(b => new
                    {
                        b.ImportFileId,
                        FileHash = b.ImportFile != null ? b.ImportFile.FileHash : null,
                        StoragePath = b.ImportFile != null ? b.ImportFile.StoragePath : null
                    })
                    .FirstOrDefaultAsync(ct);

                if (batch is null) return new SharedFileResult(FileSource.BatchMissing, null);
                if (batch.ImportFileId is null) return new SharedFileResult(FileSource.Legacy, null);

                importFileId = batch.ImportFileId;
                fileHash = batch.FileHash;
                storagePath = batch.StoragePath;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Import files: could not determine whether batch {ImportBatchId} uses a shared file, so no file was returned.", importBatchId);
                return new SharedFileResult(FileSource.Shared, null);
            }

            // 2. Linked: the shared file, verified, or nothing.
            if (string.IsNullOrWhiteSpace(fileHash) || string.IsNullOrWhiteSpace(storagePath))
            {
                _logger.LogError(
                    "Import files: batch {ImportBatchId} points at file {ImportFileId}, but that file's record is missing or incomplete. No file was returned.",
                    importBatchId, importFileId);
                return new SharedFileResult(FileSource.Shared, null);
            }

            try
            {
                var path = _store.FindByStoragePath(storagePath);
                if (path is null)
                {
                    _logger.LogError(
                        "Import files: batch {ImportBatchId} is linked to file {ImportFileId} but {StoragePath} is not on disk. No file was returned.",
                        importBatchId, importFileId, storagePath);
                    return new SharedFileResult(FileSource.Shared, null);
                }

                var bytes = await File.ReadAllBytesAsync(path, ct);
                var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                if (!string.Equals(actual, fileHash.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(
                        "Import files: INTEGRITY PROBLEM. File {ImportFileId} at {StoragePath} (batch {ImportBatchId}) hashes to {Actual}, not the stored {Expected}. No file was returned.",
                        importFileId, storagePath, importBatchId, actual, fileHash);
                    return new SharedFileResult(FileSource.Shared, null);
                }

                return new SharedFileResult(FileSource.Shared, bytes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Import files: could not read shared file {ImportFileId} at {StoragePath} for batch {ImportBatchId}. No file was returned.",
                    importFileId, storagePath, importBatchId);
                return new SharedFileResult(FileSource.Shared, null);
            }
        }
    }
}