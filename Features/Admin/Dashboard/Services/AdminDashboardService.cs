using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Dashboard.DTOs;

namespace STTproject.Features.Admin.Dashboard.Services
{
    public class AdminDashboardService : IAdminDashboardService
    {
        private const string OrderTypeCredit = "Credit";
        private const string ErrorStatusPending = "Pending";

        private readonly IDbContextFactory<EntrielContext> _dbContextFactory;

        public AdminDashboardService(IDbContextFactory<EntrielContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        // =====================================================================
        // SALES
        // One row per (sub distributor, year, month). Small enough to load once
        // (≈ subds × months) and filter in memory on the page.
        // Credit memo amounts are stored negative, so:
        //   InvoiceAmount = sum of Invoice lines
        //   CreditAmount  = -(sum of Credit lines)   → shown as a positive number
        //   Net           = InvoiceAmount - CreditAmount
        // =====================================================================
        public async Task<SalesDashboardDto> GetSalesAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();

            var subds = await db.SubDistributors
                .AsNoTracking()
                .OrderBy(s => s.SubdName)
                .Select(s => new SubdLookupDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdCode = s.SubdCode,
                    SubdName = s.SubdName,
                    IsActive = s.IsActive
                })
                .ToListAsync();

            // Flatten first, then group — keeps the navigation out of the aggregates so EF can translate it.
            var amounts = await db.SalesInvoiceItems
                .AsNoTracking()
                .Select(x => new
                {
                    x.SalesInvoice.SubDistributorId,
                    x.SalesInvoice.SalesInvoiceDate.Year,
                    x.SalesInvoice.SalesInvoiceDate.Month,
                    InvoiceAmount = x.SalesInvoice.OrderType == OrderTypeCredit ? 0m : x.Amount,
                    CreditAmount = x.SalesInvoice.OrderType == OrderTypeCredit ? x.Amount : 0m
                })
                .GroupBy(x => new { x.SubDistributorId, x.Year, x.Month })
                .Select(g => new
                {
                    g.Key.SubDistributorId,
                    g.Key.Year,
                    g.Key.Month,
                    Invoice = g.Sum(x => x.InvoiceAmount),
                    Credit = g.Sum(x => x.CreditAmount)
                })
                .ToListAsync();

            var counts = await db.SalesInvoices
                .AsNoTracking()
                .Where(s => s.OrderType != OrderTypeCredit)
                .Select(s => new { s.SubDistributorId, s.SalesInvoiceDate.Year, s.SalesInvoiceDate.Month })
                .GroupBy(s => new { s.SubDistributorId, s.Year, s.Month })
                .Select(g => new { g.Key.SubDistributorId, g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();

            var countLookup = counts.ToDictionary(c => (c.SubDistributorId, c.Year, c.Month), c => c.Count);

            var months = amounts
                .Select(a => new SalesMonthDto
                {
                    SubDistributorId = a.SubDistributorId,
                    Year = a.Year,
                    Month = a.Month,
                    InvoiceAmount = a.Invoice,
                    CreditAmount = Math.Abs(a.Credit),
                    InvoiceCount = countLookup.GetValueOrDefault((a.SubDistributorId, a.Year, a.Month))
                })
                .ToList();

            return new SalesDashboardDto { SubDistributors = subds, Months = months };
        }

        // =====================================================================
        // PENDING IMPORT ERRORS
        // A master ImportError can be seen in several batches (ImportErrorBatch).
        // Each pending error is counted once, under the latest batch it appeared
        // in (newest ImportErrorBatch.LastSeenAt). Errors with no ImportErrorBatch
        // row fall back to ImportError.ImportBatchId.
        // =====================================================================
        public async Task<PendingErrorsSummaryDto> GetPendingErrorsAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();

            var pending = await db.ImportErrors
                .AsNoTracking()
                .Where(e => e.Status == ErrorStatusPending)
                .Select(e => new
                {
                    e.ImportErrorId,
                    e.ErrorType,
                    e.CreatedAt,
                    BatchId = e.ImportErrorBatches
                        .OrderByDescending(b => b.LastSeenAt)
                        .ThenByDescending(b => b.ImportBatchId)
                        .Select(b => (int?)b.ImportBatchId)
                        .FirstOrDefault() ?? e.ImportBatchId
                })
                .ToListAsync();

            if (pending.Count == 0)
                return new PendingErrorsSummaryDto();

            var batchIds = pending.Select(p => p.BatchId).Distinct().ToList();

            var batches = await db.ImportBatches
                .AsNoTracking()
                .Where(b => batchIds.Contains(b.ImportBatchId))
                .Select(b => new
                {
                    b.ImportBatchId,
                    b.FileName,
                    b.SubDistributorId,
                    SubdCode = b.SubDistributor != null ? b.SubDistributor.SubdCode : "",
                    SubdName = b.SubDistributor != null ? b.SubDistributor.SubdName : "",
                    b.UploadedAt,
                    b.BatchStatus,
                    TimesUploaded = b.ImportFileId == null
                        ? 1
                        : db.ImportBatches.Count(x => x.ImportFileId == b.ImportFileId)
                })
                .ToDictionaryAsync(b => b.ImportBatchId);

            var weekAgo = DateTime.Now.AddDays(-7);

            var batchDtos = pending
                .GroupBy(p => p.BatchId)
                .Where(g => batches.ContainsKey(g.Key))
                .Select(g =>
                {
                    var b = batches[g.Key];
                    return new PendingErrorBatchDto
                    {
                        ImportBatchId = b.ImportBatchId,
                        FileName = b.FileName,
                        SubDistributorId = b.SubDistributorId,
                        SubdCode = b.SubdCode,
                        SubdName = b.SubdName,
                        UploadedAt = b.UploadedAt,
                        BatchStatus = b.BatchStatus,
                        TimesUploaded = b.TimesUploaded,
                        PendingCount = g.Count(),
                        OldestPendingAt = g.Min(x => x.CreatedAt),
                        Types = g.GroupBy(x => x.ErrorType)
                                 .Select(t => new ErrorTypeCountDto { ErrorType = t.Key, Count = t.Count() })
                                 .OrderByDescending(t => t.Count)
                                 .ToList()
                    };
                })
                .OrderByDescending(b => b.PendingCount)
                .ToList();

            return new PendingErrorsSummaryDto
            {
                PendingCount = pending.Count,
                NewThisWeek = pending.Count(p => p.CreatedAt >= weekAgo),
                Batches = batchDtos
            };
        }

        // =====================================================================
        // PRICE INCREASES (scheduled, not yet applied)
        // Overdue = effectivity date already passed but AppliedDate still null
        // (sp_ApplyDuePriceIncrease hasn't picked it up yet).
        // Sub items affected mirrors sp_SchedulePriceIncrease: active SubdItems
        // of the company item, limited to sub distributors on the same price
        // list when the schedule has one.
        // =====================================================================
        public async Task<List<PendingPriceIncreaseDto>> GetPendingPriceIncreasesAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();
            var today = DateTime.Today;

            return await db.CompanyItemPriceHistories
                .AsNoTracking()
                .Where(h => h.AppliedDate == null)
                .OrderBy(h => h.EffectivityDate)
                .Select(h => new PendingPriceIncreaseDto
                {
                    CompanyItemPriceHistoryId = h.CompanyItemPriceHistoryId,
                    CompanyItemId = h.CompanyItemId,
                    ItemCode = h.CompanyItem.ItemCode,
                    ItemName = h.CompanyItem.ItemName,
                    Principal = h.CompanyItem.Principal,
                    PriceListType = h.PriceListType,
                    EffectivityDate = h.EffectivityDate,
                    OldPrice = h.OldPrice,
                    IncreaseAmount = h.PriceIncreaseAmount,
                    NewPrice = h.NewPrice,
                    IsOverdue = h.EffectivityDate < today,
                    SubdItemsAffected = db.SubdItems.Count(si =>
                        si.CompanyItemId == h.CompanyItemId
                        && si.IsActive
                        && (h.PriceListType == null || si.SubDistributor.PriceListType == h.PriceListType))
                })
                .ToListAsync();
        }

        // =====================================================================
        // RECORDS
        // =====================================================================
        public async Task<RecordsDto> GetRecordsAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();

            var perSubd = await db.SubDistributors
                .AsNoTracking()
                .Select(s => new SubdRecordDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdCode = s.SubdCode,
                    SubdName = s.SubdName,
                    IsActive = s.IsActive,
                    CustomersActive = s.Customers.Count(c => c.IsActive),
                    CustomersInactive = s.Customers.Count(c => !c.IsActive),
                    SubdItemsActive = s.SubdItems.Count(i => i.IsActive),
                    SubdItemsInactive = s.SubdItems.Count(i => !i.IsActive),
                    PrincipalCount = s.SubdItems.Select(i => i.CompanyItem.Principal).Distinct().Count()
                })
                .ToListAsync();

            var companyItems = await db.CompanyItems
                .AsNoTracking()
                .GroupBy(c => c.IsActive)
                .Select(g => new { IsActive = g.Key, Count = g.Count() })
                .ToListAsync();

            return new RecordsDto
            {
                CompanyItemsActive = companyItems.Where(c => c.IsActive).Sum(c => c.Count),
                CompanyItemsInactive = companyItems.Where(c => !c.IsActive).Sum(c => c.Count),
                PerSubd = perSubd
            };
        }

        // =====================================================================
        // OLD SECTIONS — unchanged. Remove with CustPerSubd,
        // TotalMonthlyAnnualPrice and SubdItemPerSubd.
        // =====================================================================
        public async Task<List<CustomerPerSubdDto>> GetCustomersPerSubdAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();
            return await db.SubDistributors
                .AsNoTracking()
                .Select(s => new CustomerPerSubdDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdName = s.SubdName,
                    SubdCode = s.SubdCode,
                    ActiveCount = s.Customers.Count(c => c.IsActive),
                    InactiveCount = s.Customers.Count(c => !c.IsActive),
                })
                .OrderBy(s => s.SubdName)
                .ToListAsync();
        }

        public async Task<int> GetTotalCustomersAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();
            return await db.Customers.CountAsync();
        }

        public async Task<List<TotalPricesPerSubdMonthlyAnnualDto>> GetTotalPricesPerSubdMonthlyAsync(int year, int month)
        {
            await using var db = _dbContextFactory.CreateDbContext();

            var startDate = new DateOnly(year, month, 1);
            var endDate = startDate.AddMonths(1);

            return await db.SalesInvoiceItems
                .AsNoTracking()
                .Where(sii => sii.SalesInvoice.SalesInvoiceDate >= startDate
                        && sii.SalesInvoice.SalesInvoiceDate < endDate)
                .GroupBy(sii => new
                {
                    sii.SalesInvoice.SubDistributorId,
                    sii.SalesInvoice.SubDistributor.SubdCode,
                    sii.SalesInvoice.SubDistributor.SubdName
                })
                .Select(g => new TotalPricesPerSubdMonthlyAnnualDto
                {
                    SubDistributorId = g.Key.SubDistributorId,
                    SubdCode = g.Key.SubdCode,
                    SubdName = g.Key.SubdName,
                    TotalPrice = g.Sum(x => x.Amount)
                })
                .OrderByDescending(x => x.TotalPrice)
                .ToListAsync();
        }

        public async Task<List<TotalPricesPerSubdMonthlyAnnualDto>> GetTotalPricesPerSubdAnnualAsync(int year)
        {
            await using var db = _dbContextFactory.CreateDbContext();

            var startDate = new DateOnly(year, 1, 1);
            var endDate = startDate.AddYears(1);

            return await db.SalesInvoiceItems
                .AsNoTracking()
                .Where(sii => sii.SalesInvoice.SalesInvoiceDate >= startDate
                        && sii.SalesInvoice.SalesInvoiceDate < endDate)
                .GroupBy(sii => new
                {
                    sii.SalesInvoice.SubDistributorId,
                    sii.SalesInvoice.SubDistributor.SubdCode,
                    sii.SalesInvoice.SubDistributor.SubdName
                })
                .Select(g => new TotalPricesPerSubdMonthlyAnnualDto
                {
                    SubDistributorId = g.Key.SubDistributorId,
                    SubdCode = g.Key.SubdCode,
                    SubdName = g.Key.SubdName,
                    TotalPrice = g.Sum(x => x.Amount)
                })
                .OrderByDescending(x => x.TotalPrice)
                .ToListAsync();
        }

        public async Task<List<SubdItemPerSubdDto>> GetTotalSubdItemsPerSubdAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();

            return await db.SubDistributors
                .AsNoTracking()
                .Select(s => new SubdItemPerSubdDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdCode = s.SubdCode,
                    SubdName = s.SubdName,
                    SubdItemCount = s.SubdItems.Count(),
                    Principals = s.SubdItems.Select(si => si.CompanyItem.Principal).Distinct().ToList(),
                    ActiveCount = s.SubdItems.Count(si => si.IsActive),
                    InactiveCount = s.SubdItems.Count(si => !si.IsActive),
                })
                .OrderBy(s => s.SubdName)
                .ToListAsync();
        }
    }
}
