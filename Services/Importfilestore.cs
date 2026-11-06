using System.Globalization;

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