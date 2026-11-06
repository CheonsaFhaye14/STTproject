using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportErrors.DTOs;

namespace STTproject.Features.Admin.ImportErrors.Services
{
    public interface IImportBatchDetailsService
    {
        /// <summary>Null when the batch doesn't exist.</summary>
        Task<ImportBatchDetailsDto?> GetAsync(int importBatchId, CancellationToken ct = default);
    }

    public sealed class ImportBatchDetailsService : IImportBatchDetailsService
    {
        private readonly IDbContextFactory<EntrielContext> _factory;

        public ImportBatchDetailsService(IDbContextFactory<EntrielContext> factory) => _factory = factory;

        public async Task<ImportBatchDetailsDto?> GetAsync(int importBatchId, CancellationToken ct = default)
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);

            var b = await ctx.ImportBatches.AsNoTracking()
                .Where(x => x.ImportBatchId == importBatchId)
                .Select(x => new
                {
                    x.ImportBatchId,
                    x.FileName,
                    x.ImportType,
                    x.BatchStatus,
                    x.UploadedAt,
                    x.SubDistributorId,
                    SubdName = x.SubDistributor != null ? x.SubDistributor.SubdName : null,
                    UploadedByName = x.UploadedByNavigation.Username
                })
                .FirstOrDefaultAsync(ct);

            if (b is null) return null;

            var links = await ctx.ImportErrorBatches.AsNoTracking()
                .Where(l => l.ImportBatchId == importBatchId)
                .Select(l => new
                {
                    l.ImportError.ImportErrorId,
                    l.ImportError.ErrorType,
                    l.ImportError.FieldName,
                    l.ImportError.ErrorMessage,
                    l.ImportError.DataJson,
                    l.ImportError.Status,
                    l.Occurrences
                })
                .ToListAsync(ct);

            var errors = links
                .Select(l => new ImportBatchErrorDto
                {
                    ImportErrorId = l.ImportErrorId,
                    ErrorType = l.ErrorType,
                    FieldName = l.FieldName,
                    Value = ImportErrorPresentation.ExtractProblemValue(l.DataJson, l.ErrorMessage),
                    Status = l.Status,
                    Occurrences = l.Occurrences
                })
                .OrderBy(e => ImportErrorPresentation.IsPending(e.Status) ? 0 : 1)
                .ThenByDescending(e => e.Occurrences)
                .ThenBy(e => e.ImportErrorId)
                .ToList();

            return new ImportBatchDetailsDto
            {
                ImportBatchId = b.ImportBatchId,
                FileName = b.FileName,
                ImportType = b.ImportType,
                BatchStatus = b.BatchStatus,
                SubDistributorId = b.SubDistributorId,
                SubDistributorName = b.SubdName,
                UploadedByName = string.IsNullOrWhiteSpace(b.UploadedByName) ? null : b.UploadedByName,
                UploadedAtPht = DateTime.SpecifyKind(b.UploadedAt, DateTimeKind.Utc).AddHours(8),
                UniqueErrors = errors.Count,
                Occurrences = errors.Sum(e => e.Occurrences),
                PendingErrors = errors.Count(e => ImportErrorPresentation.IsPending(e.Status)),
                Errors = errors
            };
        }
    }
}