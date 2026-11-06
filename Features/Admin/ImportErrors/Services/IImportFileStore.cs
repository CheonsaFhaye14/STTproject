namespace STTproject.Features.Admin.ImportErrors.Services
{
    public interface IImportFileStore
    {
        Task<byte[]?> GetOriginalFileAsync(int importBatchId, CancellationToken ct = default);
    }
    public sealed class DiskImportFileStore : IImportFileStore
    {
        private readonly STTproject.Services.ImportFileStore _store;
        private readonly ILogger<DiskImportFileStore> _logger;

        public DiskImportFileStore(STTproject.Services.ImportFileStore store, ILogger<DiskImportFileStore> logger)
        {
            _store = store;
            _logger = logger;
        }

        public async Task<byte[]?> GetOriginalFileAsync(int importBatchId, CancellationToken ct = default)
        {
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
    }
}