using System.Globalization;
using System.Security.Cryptography;

namespace STTproject.Services;

public sealed class ImportFileStore
{
    private const int MaxFileNameLength = 150;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly string _root;
    private readonly ILogger<ImportFileStore> _logger;

    public ImportFileStore(IConfiguration configuration, IHostEnvironment environment, ILogger<ImportFileStore> logger)
    {
        var configured = configuration["ImportFiles:RootPath"];

        _root = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(environment.ContentRootPath, "ApplicationData", "ImportFiles")
                : Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));

        _logger = logger;
    }

    /// <summary>
    /// Copies the whole stream to disk under the batch's folder. The stream must be seekable (the import pages pass a MemoryStream);
    /// its position is put back afterwards so the import reads it exactly as before. Returns false when nothing was saved.
    /// </summary>
    public async Task<bool> SaveAsync(int importBatchId, string fileName, Stream source, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!source.CanRead || !source.CanSeek)
            {
                _logger.LogWarning("Import file store: batch {ImportBatchId} was not saved because the upload stream can't be rewound.", importBatchId);
                return false;
            }

            var folder = FolderFor(importBatchId);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, SafeFileName(fileName));

            var originalPosition = source.Position;
            try
            {
                source.Position = 0;
                await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await source.CopyToAsync(target, cancellationToken);
            }
            finally
            {
                source.Position = originalPosition;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Import file store: could not save the file for batch {ImportBatchId} under {Root}.", importBatchId, _root);
            return false;
        }
    }

    /// <summary>The stored file's full path for a batch, or null when there isn't one (never saved, or already cleaned up).</summary>
    public string? FindFile(int importBatchId)
    {
        var folder = FolderFor(importBatchId);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder).FirstOrDefault() : null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Content-addressed storage (additive). A file lives at {root}/by-hash/{first 2 hash chars}/{HASH}, with no extension, so
    // identical bytes map to exactly one path whatever the upload was called. The original name and extension are kept in the
    // database (ImportFile.FileName / ImportBatch.FileName) and are what downloads use.
    // The numeric batch folders above are untouched, so every file stored before this change is still found by FindFile.
    // Nothing here ever deletes or overwrites a stored file.
    // ---------------------------------------------------------------------------------------------------------------

    private const string HashFolder = "by-hash";

    /// <summary>Where a stored file ended up. AlreadyExisted is true when an identical file was already on disk and was reused.</summary>
    public sealed record StoredFileSaveResult(string RelativePath, bool AlreadyExisted);

    /// <summary>The relative path for a SHA-256 (64 hex characters): by-hash/{first 2 characters}/{HASH}, upper-case, no extension.</summary>
    public static string RelativePathFor(string fileHash)
    {
        if (!IsValidHash(fileHash)) throw new ArgumentException("A SHA-256 hash of 64 hex characters is required.", nameof(fileHash));

        var hash = fileHash.ToUpperInvariant();
        return HashFolder + "/" + hash[..2] + "/" + hash;
    }

    /// <summary>
    /// Stores the stream at <paramref name="relativePath"/> (normally <see cref="RelativePathFor"/>) as the file whose SHA-256 is
    /// <paramref name="fileHash"/>. Nothing is trusted on length alone:
    ///  - a file already at the path is reused only if its own SHA-256 equals fileHash; if it differs, the integrity problem is
    ///    logged, the file is left untouched and null is returned (the caller falls back to the per-batch folder);
    ///  - a new file is written to a temp file, its SHA-256 is checked against fileHash, and only then moved into place without
    ///    overwriting. If another upload of the same content moved its copy in first, that copy is verified the same way.
    /// Returns null when nothing verified was stored. The stream's position is restored.
    /// </summary>
    public async Task<StoredFileSaveResult?> SaveToPathAsync(
        string fileHash, string relativePath, Stream source, CancellationToken cancellationToken = default)
    {
        string? temp = null;
        try
        {
            if (!IsValidHash(fileHash))
            {
                _logger.LogError("Import file store: {RelativePath} was not saved because the expected hash is not a SHA-256.", relativePath);
                return null;
            }

            if (!source.CanRead || !source.CanSeek)
            {
                _logger.LogWarning("Import file store: {RelativePath} was not saved because the upload stream can't be rewound.", relativePath);
                return null;
            }

            var full = ResolveWithinRoot(relativePath);
            if (full is null)
            {
                _logger.LogError("Import file store: refused path {RelativePath} because it is outside the storage root.", relativePath);
                return null;
            }

            var expected = fileHash.ToUpperInvariant();

            if (File.Exists(full))
                return await VerifyExistingAsync(full, relativePath, expected, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";

            var originalPosition = source.Position;
            try
            {
                source.Position = 0;
                await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await source.CopyToAsync(target, cancellationToken);
                    await target.FlushAsync(cancellationToken);
                }
            }
            finally
            {
                source.Position = originalPosition;
            }

            // What was written must be what was hashed. If not, nothing goes into the content-addressed folder.
            var writtenHash = await HashFileAsync(temp, cancellationToken);
            if (!string.Equals(writtenHash, expected, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Import file store: the temp copy for {RelativePath} hashes to {Actual}, not the expected {Expected}. Nothing was stored.",
                    relativePath, writtenHash, expected);
                return null;
            }

            try
            {
                File.Move(temp, full, overwrite: false);
                temp = null;
                return new StoredFileSaveResult(relativePath, false);
            }
            catch (IOException) when (File.Exists(full))
            {
                // Another upload of identical content moved its copy into place first. Reuse it only if it verifies.
                // (The temp file is removed in the finally block.)
                return await VerifyExistingAsync(full, relativePath, expected, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Import file store: could not save {RelativePath} under {Root}.", relativePath, _root);
            return null;
        }
        finally
        {
            if (temp is not null)
            {
                try { File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Import file store: could not remove temp file {Temp}.", temp);
                }
            }
        }
    }

    /// <summary>
    /// The full path of a file stored under ImportFile.StoragePath, or null when the path is not inside the storage root
    /// or the file is not there (never saved, moved, or cleaned up).
    /// </summary>
    public string? FindByStoragePath(string? relativePath)
    {
        var full = ResolveWithinRoot(relativePath);
        return full is not null && File.Exists(full) ? full : null;
    }

    // Reuse an existing file only when its content really is the expected content. Never overwrites it.
    private async Task<StoredFileSaveResult?> VerifyExistingAsync(
        string full, string relativePath, string expectedHash, CancellationToken cancellationToken)
    {
        var actual = await HashFileAsync(full, cancellationToken);
        if (string.Equals(actual, expectedHash, StringComparison.Ordinal))
            return new StoredFileSaveResult(relativePath, true);

        _logger.LogError(
            "Import file store: INTEGRITY PROBLEM. {RelativePath} exists but its SHA-256 is {Actual}, not the expected {Expected}. " +
            "The file was not overwritten and not used.",
            relativePath, actual, expectedHash);
        return null;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    // A stored relative path can come from the database, so it is never trusted to stay inside the root.
    private string? ResolveWithinRoot(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;

        var full = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool IsValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    // The folder name is only ever the numeric batch id, so no user-supplied text can steer it elsewhere.
    private string FolderFor(int importBatchId) =>
        Path.Combine(_root, importBatchId.ToString(CultureInfo.InvariantCulture));

    // Keeps the original name as far as the file system allows. Drops any path the browser might send,
    // replaces illegal characters, avoids Windows reserved names and caps the length (extension kept).
    internal static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');

        if (cleaned.Length == 0) return "upload";

        if (cleaned.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(cleaned);
            if (extension.Length > 20) extension = string.Empty;
            cleaned = cleaned[..(MaxFileNameLength - extension.Length)] + extension;
        }

        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(cleaned)))
            cleaned = "_" + cleaned;

        return cleaned;
    }
}