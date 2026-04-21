using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface ISalesInvoiceService
{
    Task<SalesInvoicePageData> GetPageDataAsync(int subDistributorId, CancellationToken cancellationToken = default);
    Task<SaveInvoiceResult> SaveInvoiceAsync(InputInvoiceModel invoice, List<InputItemModel> items, int currentInvoiceId, CancellationToken cancellationToken = default);
}

public sealed class SalesInvoiceService : ISalesInvoiceService
{
    private readonly SttprojectContext _context;

    public SalesInvoiceService(SttprojectContext context)
    {
        _context = context;
    }

    public async Task<SalesInvoicePageData> GetPageDataAsync(int subDistributorId, CancellationToken cancellationToken = default)
    {
        var subdList = await _context.SubDistributors
            .AsNoTracking()
            .OrderBy(s => s.SubdCode)
            .ToListAsync(cancellationToken);

        var customers = await _context.Customers
            .AsNoTracking()
            .OrderBy(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        var customerBranches = await _context.CustomerBranches
            .AsNoTracking()
            .OrderBy(b => b.BranchName)
            .ToListAsync(cancellationToken);

        var subdItems = await _context.SubdItems
            .AsNoTracking()
            .Include(i => i.SubdItemUom)
            .Where(i => i.SubDistributorId == subDistributorId)
            .OrderBy(i => i.SubdItemCode)
            .ToListAsync(cancellationToken);

        var selectedSubd = subdList.FirstOrDefault(s => s.SubDistributorId == subDistributorId);

        return new SalesInvoicePageData
        {
            Subdistributors = subdList,
            Customers = customers,
            CustomerBranches = customerBranches,
            SubdItems = subdItems,
            SelectedSubd = selectedSubd
        };
    }

    public async Task<SaveInvoiceResult> SaveInvoiceAsync(
        InputInvoiceModel invoice,
        List<InputItemModel> items,
        int currentInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var duplicateExists = await _context.SalesInvoices
            .AnyAsync(x => x.SalesInvoiceCode == invoice.InvoiceNumber && x.SalesInvoiceId != currentInvoiceId, cancellationToken);

        if (duplicateExists)
        {
            return new SaveInvoiceResult
            {
                IsDuplicate = true,
                InvoiceId = currentInvoiceId
            };
        }

        var customerHasBranches = await _context.CustomerBranches
            .AnyAsync(cb => cb.CustomerId == invoice.CustomerId, cancellationToken);

        if (!customerHasBranches)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Selected customer has no branch configured."
            };
        }

        if (invoice.CustomerBranchId <= 0)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Customer branch is required."
            };
        }

        var customerBranchExists = await _context.CustomerBranches
            .AnyAsync(cb => cb.CustomerBranchId == invoice.CustomerBranchId && cb.CustomerId == invoice.CustomerId, cancellationToken);

        if (!customerBranchExists)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Selected customer branch is invalid."
            };
        }

        if (currentInvoiceId == 0)
        {
            var salesInvoice = new Models.SalesInvoice
            {
                SalesInvoiceCode = invoice.InvoiceNumber,
                SalesInvoiceDate = invoice.InvoiceDate,
                OrderType = invoice.OrderType,
                OrderDate = invoice.OrderDate,
                CustomerId = invoice.CustomerId,
                CustomerBranchId = invoice.CustomerBranchId,
                SubDistributorId = invoice.SubdistributorId,
                Items = items.Select(i => new SalesInvoiceItem
                {
                    SubdItemId = i.SubdItemId,
                    Quantity = i.Quantity,
                    Price = i.Price
                }).ToList()
            };

            _context.SalesInvoices.Add(salesInvoice);
            await _context.SaveChangesAsync(cancellationToken);

            return new SaveInvoiceResult
            {
                IsSaved = true,
                InvoiceId = salesInvoice.SalesInvoiceId
            };
        }

        var existing = await _context.SalesInvoices
            .FirstOrDefaultAsync(x => x.SalesInvoiceId == currentInvoiceId, cancellationToken);

        if (existing is null)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Invoice record was not found."
            };
        }

        existing.SalesInvoiceCode = invoice.InvoiceNumber;
        existing.SalesInvoiceDate = invoice.InvoiceDate;
        existing.OrderDate = invoice.OrderDate;
        existing.OrderType = invoice.OrderType;
        existing.CustomerId = invoice.CustomerId;
        existing.CustomerBranchId = invoice.CustomerBranchId;
        existing.SubDistributorId = invoice.SubdistributorId;

        var existingItems = await _context.SalesInvoiceItems
            .Where(x => x.SalesInvoiceId == currentInvoiceId)
            .ToListAsync(cancellationToken);

        if (existingItems.Any())
        {
            _context.SalesInvoiceItems.RemoveRange(existingItems);
        }

        _context.SalesInvoiceItems.AddRange(items.Select(i => new SalesInvoiceItem
        {
            SalesInvoiceId = currentInvoiceId,
            SubdItemId = i.SubdItemId,
            Quantity = i.Quantity,
            Price = i.Price
        }));

        await _context.SaveChangesAsync(cancellationToken);

        return new SaveInvoiceResult
        {
            IsSaved = true,
            InvoiceId = currentInvoiceId
        };
    }
}

public sealed class SalesInvoicePageData
{
    public List<SubDistributor> Subdistributors { get; set; } = new();
    public List<Customer> Customers { get; set; } = new();
    public List<CustomerBranch> CustomerBranches { get; set; } = new();
    public List<SubdItem> SubdItems { get; set; } = new();
    public SubDistributor? SelectedSubd { get; set; }
}

public sealed class SaveInvoiceResult
{
    public bool IsSaved { get; set; }
    public bool IsDuplicate { get; set; }
    public int InvoiceId { get; set; }
    public string? ErrorMessage { get; set; }
}
