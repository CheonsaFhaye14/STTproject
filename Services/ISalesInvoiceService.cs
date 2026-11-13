using Microsoft.EntityFrameworkCore;
using STTproject.Models;
using STTproject.Data;
using STTproject.Features.Pricing;

namespace STTproject.Services;

public interface ISalesInvoiceService
{
    Task<SalesInvoicePageData> GetPageDataAsync(int subDistributorId, CancellationToken cancellationToken = default);

    Task<bool> InvoiceNumberExistsAsync(string invoiceNumber, string orderType, int subDistributorId,
        int customerId, string? salesMan, int currentInvoiceId = 0, CancellationToken cancellationToken = default);

    Task<decimal> ResolveUomPriceAsync(int itemsUomId, DateOnly invoiceDate, CancellationToken cancellationToken = default);

    Task<SaveInvoiceResult> SaveInvoiceAsync(
        InputInvoiceModel invoice,
        List<InputItemModel> items,
        int currentInvoiceId,
        int currentUserId,
        CancellationToken cancellationToken = default);

    Task<(InputInvoiceModel? Invoice, List<InputItemModel> Items)?> GetInvoiceByIdAsync(int invoiceId, CancellationToken cancellationToken = default);

    Task<string> GetCustomerAddressAsync(int customerId, CancellationToken cancellationToken = default);

    Task<Dictionary<int, UomPriceInfo>> GetUomPricesAsync(
        int subDistributorId, DateOnly invoiceDate, IEnumerable<int> subdItemIds, CancellationToken ct = default);
}

public sealed class SalesInvoiceService : ISalesInvoiceService
{
    private readonly IDbContextFactory<EntrielContext> _contextFactory;
    private readonly ILogger<SalesInvoiceService> _logger;

    public SalesInvoiceService(IDbContextFactory<EntrielContext> contextFactory, ILogger<SalesInvoiceService> logger)
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

        var subdItems = await context.SubdItems
            .AsNoTracking()
            .Include(i => i.CompanyItem)
            .Where(i => i.SubDistributorId == subDistributorId && i.IsActive)
            .OrderBy(i => i.SubdItemCode)
            .ToListAsync(cancellationToken);

        var itemUoms = await context.ItemsUoms
            .AsNoTracking()
            .Where(i =>
                i.SubdItem.SubDistributorId == subDistributorId &&
                i.SubdItem.IsActive)
            .OrderBy(i => i.UomName)
            .ToListAsync(cancellationToken);

        return new SalesInvoicePageData
        {
            Customers = customers,
            SubdItems = subdItems,
            ItemUoms = itemUoms
        };
    }

    public static string? NormalizeSalesMan(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

    public async Task<bool> InvoiceNumberExistsAsync(string invoiceNumber, string orderType, int subDistributorId,
        int customerId, string? salesMan, int currentInvoiceId = 0, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        var sm = NormalizeSalesMan(salesMan);
        return await context.SalesInvoices.AnyAsync(x =>
            x.SalesInvoiceCode == invoiceNumber && x.OrderType == orderType &&
            x.SubDistributorId == subDistributorId && x.CustomerId == customerId &&
            x.SalesMan == sm && x.SalesInvoiceId != currentInvoiceId, cancellationToken);
    }

    // ─── Pricing ─────────────────────────────────────────────────────────────
    // Company price on the invoice date × (UOM conversion ÷ stock conversion),
    // the same rule as the admin pages and sp_RecalculateInvoiceAmounts.

    public async Task<Dictionary<int, UomPriceInfo>> GetUomPricesAsync(
        int subDistributorId, DateOnly invoiceDate, IEnumerable<int> subdItemIds, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await InvoicePricing.LoadUomPricesAsync(context, subDistributorId, invoiceDate, subdItemIds, ct);
    }

    public async Task<decimal> ResolveUomPriceAsync(int itemsUomId, DateOnly invoiceDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var uom = await context.ItemsUoms.AsNoTracking()
            .Where(u => u.ItemsUomId == itemsUomId)
            .Select(u => new { u.SubdItemId, u.SubdItem.SubDistributorId })
            .FirstOrDefaultAsync(cancellationToken);
        if (uom is null) return 0m;

        var prices = await InvoicePricing.LoadUomPricesAsync(
            context, uom.SubDistributorId, invoiceDate, new[] { uom.SubdItemId }, cancellationToken);

        return prices.TryGetValue(itemsUomId, out var p) && p.IsPriced ? p.UnitPrice : 0m;
    }

    /// <summary>
    /// Prices every line server-side (the browser's amounts are never trusted).
    /// Returns the price list used, or an error for the first line that cannot be priced.
    /// </summary>
    private static async Task<(Dictionary<int, UomPriceInfo>? Prices, string? Error)> PriceItemsAsync(
        EntrielContext context, InputInvoiceModel invoice, List<InputItemModel> items, CancellationToken ct)
    {
        var isCredit = string.Equals(invoice.OrderType?.Trim(), "Credit", StringComparison.OrdinalIgnoreCase);

        var prices = await InvoicePricing.LoadUomPricesAsync(
            context, invoice.SubdistributorId, invoice.InvoiceDate, items.Select(i => i.SubdItemId).Distinct(), ct);

        foreach (var item in items)
        {
            if (!prices.TryGetValue(item.ItemsUomId, out var p) || p.SubdItemId != item.SubdItemId)
                return (null, $"{item.ItemCode}: the unit of measure does not belong to this item.");

            if (item.IsFreeItem)
            {
                item.Amount = 0m;          // free goods carry no amount, priced or not
                item.PriceIssue = null;
                continue;
            }

            if (!p.IsPriced)
                return (null, $"{item.ItemCode} ({p.UomName}): {p.Issue}.");

            item.Amount = p.AmountFor(item.Quantity, isCredit);
            item.PriceIssue = null;
        }

        return (prices, null);
    }

    private static SalesInvoiceItem ToEntity(
        InputItemModel i, int salesInvoiceId, UomPriceInfo p, int userId, DateTime now) => new()
        {
            SalesInvoiceId = salesInvoiceId,
            SubdItemId = i.SubdItemId,
            ItemsUomId = i.ItemsUomId,
            Quantity = i.Quantity,
            Amount = i.Amount,
            BaseQuantity = p.BaseQuantityFor(i.Quantity),
            StockPriceUsed = p.StockPrice,
            CompanyItemPriceHistoryId = p.CompanyItemPriceHistoryId,
            PriceSource = i.IsFreeItem ? "Free" : (i.PriceSource ?? "Manual"),
            BilledAmount = i.BilledAmount,
            PricedAt = now,
            CreatedBy = userId,
            UpdatedBy = userId,
            CreatedDate = now,
            UpdatedDate = now
        };

    // ─── Save ────────────────────────────────────────────────────────────────

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
        EntrielContext context,
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

        invoice.SalesManName = NormalizeSalesMan(invoice.SalesManName) ?? string.Empty;

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

        // Amounts are recalculated below, so only the structure is checked here.
        var invalidItem = items.FirstOrDefault(i => i.SubdItemId <= 0 || i.ItemsUomId <= 0 || i.Quantity <= 0);
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
            var (prices, priceError) = await PriceItemsAsync(context, invoice, items, cancellationToken);
            if (priceError is not null)
            {
                return new SaveInvoiceResult
                {
                    IsSaved = false,
                    InvoiceId = currentInvoiceId,
                    ErrorMessage = priceError
                };
            }

            var duplicateExists = await context.SalesInvoices.AnyAsync(x =>
                x.SalesInvoiceCode == invoice.InvoiceNumber && x.OrderType == invoice.OrderType &&
                x.SubDistributorId == invoice.SubdistributorId && x.CustomerId == invoice.CustomerId &&
                x.SalesMan == invoice.SalesManName && x.SalesInvoiceId != currentInvoiceId, cancellationToken);

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

            var now = DateTime.Now;

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
                        CustomerId = invoice.CustomerId,
                        SubDistributorId = invoice.SubdistributorId,
                        SalesMan = NormalizeSalesMan(invoice.SalesManName),
                        CreatedBy = currentUserId,
                        UpdatedBy = currentUserId,
                        CreatedDate = now,
                        UpdatedDate = now,
                    };

                    context.SalesInvoices.Add(salesInvoice);
                    await context.SaveChangesAsync(cancellationToken);

                    context.SalesInvoiceItems.AddRange(items.Select(i =>
                        ToEntity(i, salesInvoice.SalesInvoiceId, prices![i.ItemsUomId], currentUserId, now)));

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
                existing.OrderType = invoice.OrderType;
                existing.CustomerId = invoice.CustomerId;
                existing.SubDistributorId = invoice.SubdistributorId;
                existing.SalesMan = NormalizeSalesMan(invoice.SalesManName);
                existing.UpdatedBy = currentUserId;
                existing.UpdatedDate = now;

                var existingItems = await context.SalesInvoiceItems
                    .Where(x => x.SalesInvoiceId == currentInvoiceId)
                    .ToListAsync(cancellationToken);

                if (existingItems.Any())
                {
                    context.SalesInvoiceItems.RemoveRange(existingItems);
                }

                context.SalesInvoiceItems.AddRange(items.Select(i =>
                    ToEntity(i, currentInvoiceId, prices![i.ItemsUomId], currentUserId, now)));

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

    // ─── Read ────────────────────────────────────────────────────────────────

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
                OrderType = si.OrderType,
                CustomerId = si.CustomerId,
                SalesManName = si.SalesMan ?? string.Empty,
                SubdistributorId = si.SubDistributorId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice == null)
        {
            return null;
        }

        var customer = await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == invoice.CustomerId, cancellationToken);

        if (customer is not null)
        {
            invoice.CustomerCode = customer.CustomerCode;
            invoice.CustomerName = customer.CustomerName;
            invoice.CustomerType = customer.CustomerType;
            invoice.CustomerAddress = FormatCustomerAddress(customer);
        }

        var items = await context.SalesInvoiceItems
            .AsNoTracking()
            .Where(sii => sii.SalesInvoiceId == invoiceId)
            .Select(sii => new InputItemModel
            {
                SubdItemId = sii.SubdItemId,
                ItemCode = sii.SubdItem.SubdItemCode,
                ItemName = sii.PriceSource == "Free" ? sii.SubdItem.ItemName + " (Free)" : sii.SubdItem.ItemName,
                ItemsUomId = sii.ItemsUomId,
                UomName = sii.ItemsUom != null ? sii.ItemsUom.UomName : string.Empty,
                Quantity = sii.Quantity,
                Amount = sii.Amount,
                IsFreeItem = sii.PriceSource == "Free",
                BilledAmount = sii.BilledAmount
            })
            .ToListAsync(cancellationToken);

        return (invoice, items);
    }

    public async Task<string> GetCustomerAddressAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        var customer = await context.Customers
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => new { c.AddressLine, c.City, c.Province, c.ZipCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(customer.AddressLine)) parts.Add(customer.AddressLine);
        if (!string.IsNullOrWhiteSpace(customer.City)) parts.Add(customer.City);
        if (!string.IsNullOrWhiteSpace(customer.Province)) parts.Add(customer.Province);
        if (customer.ZipCode.HasValue) parts.Add(customer.ZipCode.Value.ToString());

        return string.Join(", ", parts);
    }

    private static string FormatCustomerAddress(Customer customer)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(customer.AddressLine)) parts.Add(customer.AddressLine);
        if (!string.IsNullOrWhiteSpace(customer.City)) parts.Add(customer.City);
        if (!string.IsNullOrWhiteSpace(customer.Province)) parts.Add(customer.Province);
        if (customer.ZipCode.HasValue) parts.Add(customer.ZipCode.Value.ToString());

        return string.Join(", ", parts);
    }
}

public sealed class SalesInvoicePageData
{
    public List<Customer> Customers { get; set; } = new();
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