using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Dashboard.DTOs;

namespace STTproject.Features.Admin.Dashboard.Services
{
    public class AdminDashboardService : IAdminDashboardService
    {
        private readonly IDbContextFactory<SttprojectContext> _dbContextFactory;

        public AdminDashboardService(IDbContextFactory<SttprojectContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

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