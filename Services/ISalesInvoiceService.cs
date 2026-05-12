using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STTproject.Models;
using STTproject.Data;
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
    private readonly IDbContextFactory<SttprojectContext> _contextFactory;
    private readonly ILogger<SalesInvoiceService> _logger;

    public SalesInvoiceService(IDbContextFactory<SttprojectContext> contextFactory, ILogger<SalesInvoiceService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<SalesInvoicePageData> GetPageDataAsync(int subDistributorId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        
        var customers = await context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CustomerName)
            .ToListAsync(cancellationToken);

        var customerBranches = await context.CustomerBranches
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.BranchName)
            .ToListAsync(cancellationToken);

        var subdItems = await context.SubdItems
            .AsNoTracking()
            .Where(i => i.SubDistributorId == subDistributorId && i.IsActive)
            .OrderBy(i => i.SubdItemCode)
            .ToListAsync(cancellationToken);

        var subdItemIds = subdItems
            .Select(s => s.SubdItemId)
            .Distinct()
            .ToList();

        var itemUoms = await context.ItemsUoms
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
        await using var context = _contextFactory.CreateDbContext();
        return await context.SalesInvoices
            .AnyAsync(x => x.SalesInvoiceCode == invoiceNumber && x.SalesInvoiceId != currentInvoiceId, cancellationToken);
    }

    public async Task<SaveInvoiceResult> SaveInvoiceAsync(
        InputInvoiceModel invoice,
        List<InputItemModel> items,
        int currentInvoiceId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await SaveInvoiceAsyncInternal(context, invoice, items, currentInvoiceId, currentUserId, cancellationToken);
    }

    private async Task<SaveInvoiceResult> SaveInvoiceAsyncInternal(
        SttprojectContext context,
        InputInvoiceModel invoice,
        List<InputItemModel> items,
        int currentInvoiceId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (invoice is null)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Invoice data is missing."
            };
        }

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Invoice number is required."
            };
        }

        if (invoice.InvoiceDate == default)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Invoice date is required."
            };
        }

        if (invoice.OrderDate == default)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Order date is required."
            };
        }

        if (string.IsNullOrWhiteSpace(invoice.OrderType))
        {
            invoice.OrderType = "Invoice";
        }

        if (invoice.SubdistributorId <= 0)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Subdistributor is required."
            };
        }

        if (currentUserId <= 0)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Unable to identify the current user. Please sign in again."
            };
        }

        if (items is null || !items.Any())
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "At least one item is required before committing the invoice."
            };
        }

        var invalidItem = items.FirstOrDefault(i => i.SubdItemId <= 0 || i.ItemsUomId <= 0 || i.Quantity <= 0 || i.Amount < 0);
        if (invalidItem is not null)
        {
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "One or more invoice items are invalid. Please review and try again."
            };
        }

        try
        {
            var duplicateExists = await context.SalesInvoices
                .AnyAsync(x => x.SalesInvoiceCode == invoice.InvoiceNumber && x.SalesInvoiceId != currentInvoiceId, cancellationToken);

            if (duplicateExists)
            {
                return new SaveInvoiceResult
                {
                    IsDuplicate = true,
                    InvoiceId = currentInvoiceId
                };
            }

            var customerExists = await context.Customers
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

            var customerHasBranches = await context.CustomerBranches
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

            var customerBranchExists = await context.CustomerBranches
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
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    var salesInvoice = new SalesInvoice
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

                    context.SalesInvoices.Add(salesInvoice);
                    await context.SaveChangesAsync(cancellationToken);

                    context.SalesInvoiceItems.AddRange(items.Select(i => new SalesInvoiceItem
                    {
                        SalesInvoiceId = salesInvoice.SalesInvoiceId,
                        SubdItemId = i.SubdItemId,
                        ItemsUomId = i.ItemsUomId,
                        Quantity = i.Quantity,
                        Amount = i.Amount,
                        CreatedBy = currentUserId,
                        UpdatedBy = currentUserId,
                        CreatedDate = DateTime.Now,
                        UpdatedDate = DateTime.Now
                    }));

                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new SaveInvoiceResult
                    {
                        IsSaved = true,
                        InvoiceId = salesInvoice.SalesInvoiceId
                    };
                }
                catch (DbUpdateException dbEx)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    var baseMsg = dbEx.GetBaseException()?.Message ?? dbEx.Message;
                    _logger.LogError(dbEx, "Database error saving new sales invoice {InvoiceNumber} for user {UserId}: {Message}", invoice.InvoiceNumber, currentUserId, baseMsg);
                    return new SaveInvoiceResult
                    {
                        IsSaved = false,
                        InvoiceId = currentInvoiceId,
                        ErrorMessage = "Unable to save invoice due to a database error. " + baseMsg
                    };
                }
            }

            var existing = await context.SalesInvoices
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

            await using var updateTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

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

                var existingItems = await context.SalesInvoiceItems
                    .Where(x => x.SalesInvoiceId == currentInvoiceId)
                    .ToListAsync(cancellationToken);

                if (existingItems.Any())
                {
                    context.SalesInvoiceItems.RemoveRange(existingItems);
                }

                context.SalesInvoiceItems.AddRange(items.Select(i => new SalesInvoiceItem
                {
                    SalesInvoiceId = currentInvoiceId,
                    SubdItemId = i.SubdItemId,
                    ItemsUomId = i.ItemsUomId,
                    Quantity = i.Quantity,
                    Amount = i.Amount,
                    CreatedBy = currentUserId,
                    UpdatedBy = currentUserId,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                }));

                await context.SaveChangesAsync(cancellationToken);
                await updateTransaction.CommitAsync(cancellationToken);

                return new SaveInvoiceResult
                {
                    IsSaved = true,
                    InvoiceId = currentInvoiceId
                };
            }
            catch (DbUpdateException dbEx)
            {
                await updateTransaction.RollbackAsync(cancellationToken);
                var baseMsg = dbEx.GetBaseException()?.Message ?? dbEx.Message;
                _logger.LogError(dbEx, "Database error updating sales invoice {InvoiceId} for user {UserId}: {Message}", currentInvoiceId, currentUserId, baseMsg);
                return new SaveInvoiceResult
                {
                    IsSaved = false,
                    InvoiceId = currentInvoiceId,
                    ErrorMessage = "Unable to update invoice due to a database error. " + baseMsg
                };
            }
        }
        catch (Exception ex)
        {
            var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
            _logger.LogError(ex, "Unexpected error saving sales invoice {InvoiceNumber} for user {UserId}: {Message}", invoice.InvoiceNumber, currentUserId, baseMsg);
            return new SaveInvoiceResult
            {
                IsSaved = false,
                InvoiceId = currentInvoiceId,
                ErrorMessage = "Unable to save invoice: " + baseMsg
            };
        }
    }

    public async Task<(InputInvoiceModel? Invoice, List<InputItemModel> Items)?> GetInvoiceByIdAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        
        var invoice = await context.SalesInvoices
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

        var items = await context.SalesInvoiceItems
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
