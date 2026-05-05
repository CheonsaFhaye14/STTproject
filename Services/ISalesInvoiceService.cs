using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface ISalesInvoiceService
{
    Task<SalesInvoicePageData> GetPageDataAsync(int subDistributorId, CancellationToken cancellationToken = default);
    Task<bool> InvoiceNumberExistsAsync(string invoiceNumber, int currentInvoiceId = 0, CancellationToken cancellationToken = default);
    Task<SaveInvoiceResult> SaveInvoiceAsync(
        InputInvoiceModel invoice,
        List<InputItemModel> items,
        int currentInvoiceId,
        int currentUserId,
        CancellationToken cancellationToken = default);
    Task<(InputInvoiceModel? Invoice, List<InputItemModel> Items)?> GetInvoiceByIdAsync(int invoiceId, CancellationToken cancellationToken = default);
}

public sealed class SalesInvoiceService : ISalesInvoiceService
{
    // Static semaphore ensures all SalesInvoiceService instances share the same lock
    // This prevents concurrent DbContext access from multiple components
    private static readonly SemaphoreSlim _dbAccessLock = new(1, 1);

    private readonly SttprojectContext _context;

    public SalesInvoiceService(SttprojectContext context)
    {
        _context = context;
    }

    public async Task<SalesInvoicePageData> GetPageDataAsync(int subDistributorId, CancellationToken cancellationToken = default)
    {
        // Acquire lock to ensure sequential DbContext access
        await _dbAccessLock.WaitAsync(cancellationToken);
        try
        {
            return await _GetPageDataAsyncInternal(subDistributorId, cancellationToken);
        }
        finally
        {
            _dbAccessLock.Release();
        }
    }

    /// <summary>
    /// Internal method that retrieves page data without acquiring the lock.
    /// Only call this when you already hold the _dbAccessLock.
    /// </summary>
    private async Task<SalesInvoicePageData> _GetPageDataAsyncInternal(int subDistributorId, CancellationToken cancellationToken = default)
    {
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        var customerBranches = await _context.CustomerBranches
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.BranchName)
            .ToListAsync(cancellationToken);

        var subdItems = await _context.SubdItems
            .AsNoTracking()
            .Where(i => i.SubDistributorId == subDistributorId && i.IsActive)
            .OrderBy(i => i.SubdItemCode)
            .ToListAsync(cancellationToken);

        var subdItemIds = subdItems
            .Select(s => s.SubdItemId)
            .Distinct()
            .ToList();

        var itemUoms = await _context.ItemsUoms
            .AsNoTracking()
            .Where(i => subdItemIds.Contains(i.SubdItemId))
            .OrderBy(i => i.UomName)
            .ToListAsync(cancellationToken);

        return new SalesInvoicePageData
        {
            Customers = customers,
            CustomerBranches = customerBranches,
            SubdItems = subdItems,
            ItemUoms = itemUoms
        };
    }

    public async Task<bool> InvoiceNumberExistsAsync(string invoiceNumber, int currentInvoiceId = 0, CancellationToken cancellationToken = default)
    {
        await _dbAccessLock.WaitAsync(cancellationToken);
        try
        {
            return await _InvoiceNumberExistsAsyncInternal(invoiceNumber, currentInvoiceId, cancellationToken);
        }
        finally
        {
            _dbAccessLock.Release();
        }
    }

    /// <summary>
    /// Internal method that checks if invoice number exists without acquiring the lock.
    /// Only call this when you already hold the _dbAccessLock.
    /// </summary>
    private Task<bool> _InvoiceNumberExistsAsyncInternal(string invoiceNumber, int currentInvoiceId, CancellationToken cancellationToken)
    {
        return _context.SalesInvoices
            .AnyAsync(x => x.SalesInvoiceCode == invoiceNumber && x.SalesInvoiceId != currentInvoiceId, cancellationToken);
    }

    public async Task<SaveInvoiceResult> SaveInvoiceAsync(
        InputInvoiceModel invoice,
        List<InputItemModel> items,
        int currentInvoiceId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        // Acquire lock to ensure sequential DbContext access
        await _dbAccessLock.WaitAsync(cancellationToken);
        try
        {
            return await _SaveInvoiceAsyncInternal(invoice, items, currentInvoiceId, currentUserId, cancellationToken);
        }
        finally
        {
            _dbAccessLock.Release();
        }
    }

    /// <summary>
    /// Internal method that saves invoice without acquiring the lock.
    /// Only call this when you already hold the _dbAccessLock.
    /// </summary>
    private async Task<SaveInvoiceResult> _SaveInvoiceAsyncInternal(
        InputInvoiceModel invoice,
        List<InputItemModel> items,
        int currentInvoiceId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (items is null || !items.Any())
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "At least one item is required before committing the invoice."
            };
        }

        var duplicateExists = await _InvoiceNumberExistsAsyncInternal(invoice.InvoiceNumber, currentInvoiceId, cancellationToken);

        if (duplicateExists)
        {
            return new SaveInvoiceResult
            {
                IsDuplicate = true,
                InvoiceId = currentInvoiceId
            };
        }

        var customerExists = await _context.Customers
            .AnyAsync(c => c.CustomerId == invoice.CustomerId && c.IsActive, cancellationToken);

        if (!customerExists)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Selected customer is invalid."
            };
        }

        var customerHasBranches = await _context.CustomerBranches
            .AnyAsync(cb => cb.CustomerId == invoice.CustomerId && cb.IsActive, cancellationToken);

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
            .AnyAsync(cb => cb.CustomerBranchId == invoice.CustomerBranchId && cb.CustomerId == invoice.CustomerId && cb.IsActive, cancellationToken);

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
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
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
                    CreatedBy = currentUserId,
                    UpdatedBy = currentUserId,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now,
                };

                _context.SalesInvoices.Add(salesInvoice);
                await _context.SaveChangesAsync(cancellationToken);

                _context.SalesInvoiceItems.AddRange(items.Select(i => new SalesInvoiceItem
                {
                    SalesInvoiceId = salesInvoice.SalesInvoiceId,
                    SubdItemId = i.SubdItemId,
                    ItemsUomId = i.ItemsUomId,
                    Quantity = i.Quantity,
                    Amount = i.Amount
                    ,
                    CreatedBy = currentUserId,
                    UpdatedBy = currentUserId,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                }));

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new SaveInvoiceResult
                {
                    IsSaved = true,
                    InvoiceId = salesInvoice.SalesInvoiceId
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
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

        await using var updateTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            existing.SalesInvoiceCode = invoice.InvoiceNumber;
            existing.SalesInvoiceDate = invoice.InvoiceDate;
            existing.OrderDate = invoice.OrderDate;
            existing.OrderType = invoice.OrderType;
            existing.CustomerId = invoice.CustomerId;
            existing.CustomerBranchId = invoice.CustomerBranchId;
            existing.SubDistributorId = invoice.SubdistributorId;
            existing.UpdatedBy = currentUserId;
            existing.UpdatedDate = DateTime.Now;

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
                ItemsUomId = i.ItemsUomId,
                Quantity = i.Quantity,
                Amount = i.Amount
                ,
                CreatedBy = currentUserId,
                UpdatedBy = currentUserId,
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            }));

            await _context.SaveChangesAsync(cancellationToken);
            await updateTransaction.CommitAsync(cancellationToken);

            return new SaveInvoiceResult
            {
                IsSaved = true,
                InvoiceId = currentInvoiceId
            };
        }
        catch
        {
            await updateTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<(InputInvoiceModel? Invoice, List<InputItemModel> Items)?> GetInvoiceByIdAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        // Acquire lock to ensure sequential DbContext access
        await _dbAccessLock.WaitAsync(cancellationToken);
        try
        {
            return await _GetInvoiceByIdAsyncInternal(invoiceId, cancellationToken);
        }
        finally
        {
            _dbAccessLock.Release();
        }
    }

    /// <summary>
    /// Internal method that retrieves invoice by ID without acquiring the lock.
    /// Only call this when you already hold the _dbAccessLock.
    /// </summary>
    private async Task<(InputInvoiceModel? Invoice, List<InputItemModel> Items)?> _GetInvoiceByIdAsyncInternal(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .Where(si => si.SalesInvoiceId == invoiceId)
            .Select(si => new InputInvoiceModel
            {
                InvoiceNumber = si.SalesInvoiceCode,
                InvoiceDate = si.SalesInvoiceDate,
                OrderDate = si.OrderDate,
                OrderType = si.OrderType,
                CustomerId = si.CustomerId,
                CustomerBranchId = si.CustomerBranchId,
                SubdistributorId = si.SubDistributorId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice == null)
        {
            return null;
        }

        var items = await _context.SalesInvoiceItems
            .AsNoTracking()
            .Where(sii => sii.SalesInvoiceId == invoiceId)
            .Select(sii => new InputItemModel
            {
                SubdItemId = sii.SubdItemId,
                ItemCode = sii.SubdItem.SubdItemCode,
                ItemName = sii.SubdItem.ItemName,
                ItemsUomId = sii.ItemsUomId,
                UomName = sii.ItemsUom != null ? sii.ItemsUom.UomName : string.Empty,
                Quantity = sii.Quantity,
                Amount = sii.Amount
            })
            .ToListAsync(cancellationToken);

        return (invoice, items);
    }
}

public sealed class SalesInvoicePageData
{
    public List<Customer> Customers { get; set; } = new();
    public List<CustomerBranch> CustomerBranches { get; set; } = new();
    public List<SubdItem> SubdItems { get; set; } = new();
    public List<ItemsUom> ItemUoms { get; set; } = new();
}

public sealed class SaveInvoiceResult
{
    public bool IsSaved { get; set; }
    public bool IsDuplicate { get; set; }
    public int InvoiceId { get; set; }
    public string? ErrorMessage { get; set; }
}
