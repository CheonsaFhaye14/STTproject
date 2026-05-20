// services to get data for sales invoice header and details to populate the fields in the invoice header and details components
using Microsoft.EntityFrameworkCore;
using STTproject.Features.User.TestSalesInvoice.DTOs;
using STTproject.Data;

namespace STTproject.Features.User.TestSalesInvoice.Services
{
    public interface ISalesInvoiceService
    {
        Task<PageData> GetPageDataAsync(int subdistributorId, CancellationToken cancellationToken);
    }

    public class SalesInvoiceService : ISalesInvoiceService
    {
        private readonly IDbContextFactory<SttprojectContext> _contextFactory;

        public SalesInvoiceService(IDbContextFactory<SttprojectContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<PageData> GetPageDataAsync(int subdistributorId, CancellationToken cancellationToken)
        {
            await using var context = _contextFactory.CreateDbContext();

            var customers = await context.Customers
                .AsNoTracking()
                .Where(c => c.SubDistributorId == subdistributorId && c.IsActive)
                .Select(c => c.CustomerName)
                .ToListAsync(cancellationToken);

            var customerBranches = await context.CustomerBranches
                .AsNoTracking()
                .Where(cb => cb.IsActive)
                .Select(cb => cb.BranchName)
                .ToListAsync(cancellationToken);

            var subdItems = await context.SubdItems
                .AsNoTracking()
                .Where(si => si.SubDistributorId == subdistributorId && si.IsActive)
                .Select(si => si.ItemName)
                .ToListAsync(cancellationToken);

            var itemUomIds = await context.SubdItems
                .AsNoTracking()
                .Where(si => si.SubDistributorId == subdistributorId && si.IsActive)
                .Select(si => si.SubdItemId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var itemUoms = await context.ItemsUoms
                .AsNoTracking()
                .Where(iu => itemUomIds.Contains(iu.SubdItemId))
                .Select(iu => iu.UomName)
                .ToListAsync(cancellationToken);

            return new PageData
            {
                Customers = customers,
                CustomerBranches = customerBranches,
                SubdItems = subdItems,
                ItemUoms = itemUoms
            };
        }
    }
}