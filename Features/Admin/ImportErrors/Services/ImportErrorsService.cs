using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportErrors.DTOs;

namespace STTproject.Features.Admin.ImportErrors.Services
{
    public interface IImportErrorsService
    {
        /// <summary>"All Errors" view: one row per unique ImportError.</summary>
        Task<(IReadOnlyList<ImportErrorListDto> Items, int Total)> GetPagedErrorsAsync(ImportErrorFilter filter);

        /// <summary>"By Import File" view: one row per ImportBatch that has errors (via ImportErrorBatch).</summary>
        Task<(IReadOnlyList<ImportBatchListDto> Items, int Total)> GetPagedBatchesAsync(ImportErrorFilter filter);

        /// <summary>Sidebar counts. They follow the search, problem and subdistributor filters, but not the status filter.</summary>
        Task<ImportErrorStatusCounts> GetStatusCountsAsync(ImportErrorFilter filter);

        Task<IReadOnlyList<string>> GetErrorTypesAsync();
    }

    public class ImportErrorsService : IImportErrorsService
    {
        // If AdminCustomerService gets its context another way (a scoped EntrielContext, for example),
        // change this constructor to match and replace "await using var ctx = ..." accordingly.
        private readonly IDbContextFactory<EntrielContext> _factory;

        public ImportErrorsService(IDbContextFactory<EntrielContext> factory) => _factory = factory;

        public async Task<(IReadOnlyList<ImportErrorListDto> Items, int Total)> GetPagedErrorsAsync(ImportErrorFilter f)
        {
            await using var ctx = await _factory.CreateDbContextAsync();

            var q = FilteredErrors(ctx, f, includeStatus: true);
            var total = await q.CountAsync();

            var asc = f.SortAscending;
            // Pending errors always come first. The chosen column only orders within the pending group and within the rest.
            var pendingFirst = q.OrderBy(e => e.Status.ToLower() == ImportErrorPresentation.Pending ? 0 : 1);
            IOrderedQueryable<ImportError> sorted = f.SortColumn switch
            {
                "Problem" => asc ? pendingFirst.ThenBy(e => e.ErrorType) : pendingFirst.ThenByDescending(e => e.ErrorType),
                "Value" => asc ? pendingFirst.ThenBy(e => e.ErrorMessage) : pendingFirst.ThenByDescending(e => e.ErrorMessage),
                "Subdistributor" => asc
                    ? pendingFirst.ThenBy(e => e.ImportBatch.SubDistributor!.SubdName)
                    : pendingFirst.ThenByDescending(e => e.ImportBatch.SubDistributor!.SubdName),
                "Status" => asc ? pendingFirst.ThenBy(e => e.Status) : pendingFirst.ThenByDescending(e => e.Status),
                _ => asc
                    ? pendingFirst.ThenBy(e => e.ImportErrorBatches.Max(b => (DateTime?)b.LastSeenAt))
                    : pendingFirst.ThenByDescending(e => e.ImportErrorBatches.Max(b => (DateTime?)b.LastSeenAt)),
            };

            var rows = await sorted
                .ThenBy(e => e.ImportErrorId)
                .Skip((Math.Max(f.Page, 1) - 1) * f.PageSize)
                .Take(f.PageSize)
                .Select(e => new
                {
                    e.ImportErrorId,
                    e.ErrorType,
                    e.FieldName,
                    e.ErrorMessage,
                    e.DataJson,
                    SubDistributorId = e.ImportBatch.SubDistributorId,
                    SubDistributorName = e.ImportBatch.SubDistributor != null
                        ? e.ImportBatch.SubDistributor!.SubdName
                        : null,
                    Occurrences = e.ImportErrorBatches.Sum(b => (int?)b.Occurrences) ?? 0,
                    LastSeenAt = e.ImportErrorBatches.Max(b => (DateTime?)b.LastSeenAt),
                    e.Status,
                    e.ResolutionType,
                    e.ResolvedAt,
                })
                .ToListAsync();

            var items = rows.Select(r => new ImportErrorListDto
            {
                ImportErrorId = r.ImportErrorId,
                ErrorType = r.ErrorType,
                FieldName = r.FieldName,
                Value = ImportErrorPresentation.ExtractProblemValue(r.DataJson, r.ErrorMessage),
                SubDistributorId = r.SubDistributorId,
                SubDistributorName = r.SubDistributorName,
                Occurrences = r.Occurrences,
                LastSeenAt = r.LastSeenAt,
                Status = r.Status,
                ResolutionType = r.ResolutionType,
                ResolvedAt = r.ResolvedAt,
            }).ToList();

            return (items, total);
        }

        public async Task<(IReadOnlyList<ImportBatchListDto> Items, int Total)> GetPagedBatchesAsync(ImportErrorFilter f)
        {
            await using var ctx = await _factory.CreateDbContextAsync();

            var q = FilteredBatches(ctx, f);

            var total = await q.CountAsync();

            var asc = f.SortAscending;
            // Files that still have pending errors always come first.
            var pendingFirst = q.OrderBy(b => b.ImportErrorBatches.Any(x => x.ImportError.Status.ToLower() == ImportErrorPresentation.Pending) ? 0 : 1);
            IOrderedQueryable<ImportBatch> sorted = f.SortColumn switch
            {
                "File" => asc ? pendingFirst.ThenBy(b => b.FileName) : pendingFirst.ThenByDescending(b => b.FileName),
                "Import" => asc ? pendingFirst.ThenBy(b => b.BatchStatus) : pendingFirst.ThenByDescending(b => b.BatchStatus),
                "ErrorsFound" => asc
                    ? pendingFirst.ThenBy(b => b.ImportErrorBatches.Count())
                    : pendingFirst.ThenByDescending(b => b.ImportErrorBatches.Count()),
                _ => asc ? pendingFirst.ThenBy(b => b.UploadedAt) : pendingFirst.ThenByDescending(b => b.UploadedAt),
            };

            var items = await sorted
                .ThenBy(b => b.ImportBatchId)
                .Skip((Math.Max(f.Page, 1) - 1) * f.PageSize)
                .Take(f.PageSize)
                .Select(b => new ImportBatchListDto
                {
                    ImportBatchId = b.ImportBatchId,
                    FileName = b.FileName,
                    SubDistributorId = b.SubDistributorId,
                    SubDistributorName = b.SubDistributor != null ? b.SubDistributor.SubdName : null,
                    BatchStatus = b.BatchStatus,
                    UploadedAt = b.UploadedAt,
                    UniqueErrors = b.ImportErrorBatches.Count(),
                    Occurrences = b.ImportErrorBatches.Sum(x => (int?)x.Occurrences) ?? 0,
                    PendingErrors = b.ImportErrorBatches.Count(x => x.ImportError.Status.ToLower() == ImportErrorPresentation.Pending),
                })
                .ToListAsync();

            return (items, total);
        }

        public async Task<ImportErrorStatusCounts> GetStatusCountsAsync(ImportErrorFilter f)
        {
            await using var ctx = await _factory.CreateDbContextAsync();

            var groups = await FilteredErrors(ctx, f, includeStatus: false)
                .GroupBy(e => e.Status.ToLower())
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int Of(string key) => groups.Where(g => g.Status == key).Sum(g => g.Count);

            return new ImportErrorStatusCounts
            {
                Total = groups.Sum(g => g.Count),
                Pending = Of(ImportErrorPresentation.Pending),
                Ignored = Of(ImportErrorPresentation.Ignored),
                Resolved = Of(ImportErrorPresentation.Resolved),
            };
        }

        public async Task<IReadOnlyList<string>> GetErrorTypesAsync()
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.ImportErrors.AsNoTracking()
                .Select(e => e.ErrorType)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();
        }

        /// <summary>Shared by the By Import File list and the export.</summary>
        internal static IQueryable<ImportBatch> FilteredBatches(EntrielContext ctx, ImportErrorFilter f)
        {
            // Only files that actually produced errors.
            var q = ctx.ImportBatches.AsNoTracking().Where(b => b.ImportErrorBatches.Any());

            if (f.SubDistributorId.HasValue)
                q = q.Where(b => b.SubDistributorId == f.SubDistributorId);

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim();
                q = q.Where(b => b.FileName.Contains(s)
                              || (b.SubDistributor != null && b.SubDistributor.SubdName.Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(f.Status))
            {
                var importStatus = f.Status.Trim().ToLower();
                q = q.Where(b => b.BatchStatus.ToLower() == importStatus);
            }

            return q;
        }

        /// <summary>Midnight of a Philippine calendar date (UTC+8), as UTC.</summary>
        private static DateTime PhDateToUtc(DateTime phDate) =>
            DateTime.SpecifyKind(phDate.Date, DateTimeKind.Unspecified).AddHours(-8);

        internal static IQueryable<ImportError> FilteredErrors(EntrielContext ctx, ImportErrorFilter f, bool includeStatus)
        {
            var q = ctx.ImportErrors.AsNoTracking().AsQueryable();

            if (f.SubDistributorId.HasValue)
                q = q.Where(e => e.ImportBatch.SubDistributorId == f.SubDistributorId);

            if (!string.IsNullOrWhiteSpace(f.ErrorType))
                q = q.Where(e => e.ErrorType == f.ErrorType);

            if (f.DateFrom.HasValue || f.DateTo.HasValue)
            {
                // Dates are Philippine calendar days; stored timestamps are UTC.
                var fromUtc = f.DateFrom.HasValue ? PhDateToUtc(f.DateFrom.Value) : DateTime.MinValue;
                var toUtc = f.DateTo.HasValue ? PhDateToUtc(f.DateTo.Value.AddDays(1)) : DateTime.MaxValue;
                q = q.Where(e => e.ImportErrorBatches.Any(b => b.LastSeenAt >= fromUtc && b.LastSeenAt < toUtc));
            }

            if (includeStatus && !string.IsNullOrWhiteSpace(f.Status))
            {
                var status = f.Status.Trim().ToLower();
                q = q.Where(e => e.Status.ToLower() == status);
            }

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim();
                q = q.Where(e => e.ErrorMessage.Contains(s)
                              || e.ErrorType.Contains(s)
                              || (e.FieldName != null && e.FieldName.Contains(s))
                              || (e.DataJson != null && e.DataJson.Contains(s))
                              || e.ImportErrorBatches.Any(b => b.ImportBatch.FileName.Contains(s)));
            }

            return q;
        }
    }
}