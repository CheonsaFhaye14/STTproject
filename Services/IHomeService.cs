using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface IHomeService
{
    Task<List<SubDistributor>> GetSubDistributorsAsync(int userId, CancellationToken cancellationToken = default);
    Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<HomeSalesInvoiceBatchRow>> GetSalesInvoiceBatchRowsAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<HomeSalesInvoiceFlatRow>> GetSalesInvoiceFlatRowsAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<HomeSalesInvoiceBatchInvoiceRow>> GetBatchInvoiceSummariesAsync(
        int userId,
        int subDistributorId,
        DateOnly batchCreatedDate,
        int firstSalesInvoiceId,
        int lastSalesInvoiceId,
        CancellationToken cancellationToken = default);
    Task<List<HomeSalesInvoiceDetailRow>> GetBatchInvoiceDetailsAsync(
        int userId,
        int subDistributorId,
        DateOnly batchCreatedDate, 
        int firstSalesInvoiceId,
        int lastSalesInvoiceId,
        CancellationToken cancellationToken = default);
    Task<HomeSalesInvoiceDetailRow?> GetInvoiceDetailByIdAsync(
        int userId,
        int salesInvoiceId,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteInvoiceByIdAsync(
        int userId,
        int salesInvoiceId,
        CancellationToken cancellationToken = default);
}

public class HomeService : IHomeService
{
    private readonly IDbContextFactory<SttprojectContext> _contextFactory;

    public HomeService(IDbContextFactory<SttprojectContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<HomeSalesInvoiceBatchRow>> GetSalesInvoiceBatchRowsAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var subdIds = await tempContext.SubDistributors
            .AsNoTracking()
            .Where(s => s.EncoderId == userId && s.IsActive)
            .Select(s => s.SubDistributorId)
            .ToListAsync(cancellationToken);

        if (!subdIds.Any())
        {
            return new List<HomeSalesInvoiceBatchRow>();
        }

        var invoices = await tempContext.SalesInvoices
            .AsNoTracking()
            .Where(si => subdIds.Contains(si.SubDistributorId))
            .Where(si => si.SalesInvoiceItems.Any())
            .Select(si => new
            {
                si.SalesInvoiceId,
                si.SubDistributorId,
                si.CreatedDate,
                si.SalesInvoiceDate,
                si.SubDistributor.SubdName,
                si.SubDistributor.SubdCode

            })
            .ToListAsync(cancellationToken);

        if (!invoices.Any())
        {
            return new List<HomeSalesInvoiceBatchRow>();
        }

        var ordered = invoices
            .OrderBy(x => x.SubDistributorId)
            .ThenBy(x => x.CreatedDate.Date)
            .ThenBy(x => x.SalesInvoiceId)
            .ToList();

        var rows = new List<HomeSalesInvoiceBatchRow>();
        var batchNumber = 0;
        int? previousSubdId = null;
        DateTime? previousCreatedDate = null;
        int? previousInvoiceId = null;
        HomeSalesInvoiceBatchRow? currentBatch = null;

        foreach (var invoice in ordered)
        {
            var isSameSubd = previousSubdId.HasValue && previousSubdId.Value == invoice.SubDistributorId;
            var isSameCreatedDate = previousCreatedDate.HasValue && previousCreatedDate.Value.Date == invoice.CreatedDate.Date;
            var isContinuousId = previousInvoiceId.HasValue && invoice.SalesInvoiceId == previousInvoiceId.Value + 1;

            if (!(isSameSubd && isSameCreatedDate && isContinuousId))
            {
                batchNumber++;

                currentBatch = new HomeSalesInvoiceBatchRow
                {
                    BatchId = $"{DateOnly.FromDateTime(invoice.CreatedDate):yyyyMMdd}-{invoice.SubdCode}",
                    SubDistributorId = invoice.SubDistributorId,
                    SubdName = invoice.SubdName,
                    SubdCode = invoice.SubdCode,
                    LatestInvoiceDate = invoice.SalesInvoiceDate,
                    OldestInvoiceDate = invoice.SalesInvoiceDate,
                    CreatedDate = invoice.CreatedDate,
                    BatchCreatedDate = DateOnly.FromDateTime(invoice.CreatedDate),
                    FirstSalesInvoiceId = invoice.SalesInvoiceId,
                    LastSalesInvoiceId = invoice.SalesInvoiceId
                };

                rows.Add(currentBatch);
            }

            if (currentBatch != null)
            {
                if (invoice.SalesInvoiceDate > currentBatch.LatestInvoiceDate)
                {
                    currentBatch.LatestInvoiceDate = invoice.SalesInvoiceDate;
                }

                if (invoice.SalesInvoiceDate < currentBatch.OldestInvoiceDate)
                {
                    currentBatch.OldestInvoiceDate = invoice.SalesInvoiceDate;
                }

                currentBatch.LastSalesInvoiceId = invoice.SalesInvoiceId;
            }

            previousSubdId = invoice.SubDistributorId;
            previousCreatedDate = invoice.CreatedDate;
            previousInvoiceId = invoice.SalesInvoiceId;
        }

        return rows
            .OrderByDescending(x => x.CreatedDate)
            .ThenByDescending(x => x.LastSalesInvoiceId)
            .ToList();
    }

    public Task<List<HomeSalesInvoiceBatchInvoiceRow>> GetBatchInvoiceSummariesAsync(
        int userId,
        int subDistributorId,
        DateOnly batchCreatedDate,
        int firstSalesInvoiceId,
        int lastSalesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var dayStart = batchCreatedDate.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        return GetBatchInvoiceSummariesInternalAsync(
            userId,
            subDistributorId,
            dayStart,
            dayEnd,
            firstSalesInvoiceId,
            lastSalesInvoiceId,
            cancellationToken);
    }

    private async Task<List<HomeSalesInvoiceBatchInvoiceRow>> GetBatchInvoiceSummariesInternalAsync(
        int userId,
        int subDistributorId,
        DateTime dayStart,
        DateTime dayEnd,
        int firstSalesInvoiceId,
        int lastSalesInvoiceId,
        CancellationToken cancellationToken)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var invoices = await tempContext.SalesInvoices
            .AsNoTracking()
            .Where(si => si.SubDistributor.EncoderId == userId && si.SubDistributorId == subDistributorId)
            .Where(si => si.CreatedDate >= dayStart && si.CreatedDate < dayEnd)
            .Where(si => si.SalesInvoiceId >= firstSalesInvoiceId && si.SalesInvoiceId <= lastSalesInvoiceId)
            .Where(si => si.SalesInvoiceItems.Any())
            .OrderBy(si => si.SalesInvoiceId)
            .Select(si => new HomeSalesInvoiceBatchInvoiceRow
            {
                SalesInvoiceId = si.SalesInvoiceId,
                InvoiceCode = si.SalesInvoiceCode,
                SalesInvoiceDate = si.SalesInvoiceDate,
                SubdName = si.SubDistributor.SubdName,
                CustomerName = si.Customer.CustomerName
            })
            .ToListAsync(cancellationToken);

        return invoices;
    }

    public Task<List<HomeSalesInvoiceDetailRow>> GetBatchInvoiceDetailsAsync(
        int userId,
        int subDistributorId,
        DateOnly batchCreatedDate,
        int firstSalesInvoiceId,
        int lastSalesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var dayStart = batchCreatedDate.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        return GetBatchInvoiceDetailsInternalAsync(
            userId,
            subDistributorId,
            dayStart,
            dayEnd,
            firstSalesInvoiceId,
            lastSalesInvoiceId,
            cancellationToken);
    }

    private async Task<List<HomeSalesInvoiceDetailRow>> GetBatchInvoiceDetailsInternalAsync(
        int userId,
        int subDistributorId,
        DateTime dayStart,
        DateTime dayEnd,
        int firstSalesInvoiceId,
        int lastSalesInvoiceId,
        CancellationToken cancellationToken)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var invoices = await tempContext.SalesInvoices
            .AsNoTracking()
            .Where(si => si.SubDistributor.EncoderId == userId && si.SubDistributorId == subDistributorId)
            .Where(si => si.CreatedDate >= dayStart && si.CreatedDate < dayEnd)
            .Where(si => si.SalesInvoiceId >= firstSalesInvoiceId && si.SalesInvoiceId <= lastSalesInvoiceId)
            .Where(si => si.SalesInvoiceItems.Any())
            .OrderBy(si => si.SalesInvoiceId)
            .Select(si => new HomeSalesInvoiceDetailRow
            {
                SalesInvoiceId = si.SalesInvoiceId,
                InvoiceNumber = si.SalesInvoiceCode,
                InvoiceDate = si.SalesInvoiceDate,
                CreatedDate = si.CreatedDate,
                TotalItems = si.SalesInvoiceItems.Count
            })
            .ToListAsync(cancellationToken);

        if (!invoices.Any())
        {
            return invoices;
        }

        var invoiceIds = invoices.Select(i => i.SalesInvoiceId).ToList();

        var invoiceItems = await tempContext.SalesInvoiceItems
            .AsNoTracking()
            .Where(item => invoiceIds.Contains(item.SalesInvoiceId))
            .OrderBy(item => item.SalesInvoiceId)
            .ThenBy(item => item.SalesInvoiceItemId)
            .Select(item => new
            {
                item.SalesInvoiceId,
                item.SubdItem.SubdItemCode,
                item.SubdItem.ItemName,
                UomName = item.ItemsUom != null ? item.ItemsUom.UomName : string.Empty,
                item.Quantity,
                item.Amount
            })
            .ToListAsync(cancellationToken);

        var itemMap = invoiceItems
            .GroupBy(item => item.SalesInvoiceId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => new HomeSalesInvoiceItemDetailRow
                {
                    ItemCode = item.SubdItemCode,
                    ItemName = item.ItemName,
                    UomName = item.UomName,
                    Quantity = item.Quantity,
                    Price = item.Amount
                }).ToList());

        foreach (var invoice in invoices)
        {
            if (itemMap.TryGetValue(invoice.SalesInvoiceId, out var details))
            {
                invoice.ItemDetails = details;
            }
        }

        return invoices;
    }

    public async Task<List<HomeSalesInvoiceFlatRow>> GetSalesInvoiceFlatRowsAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var invoices = await tempContext.SalesInvoices
            .AsNoTracking()
            .Where(si => si.SubDistributor.EncoderId == userId)
            .Where(si => si.SalesInvoiceItems.Any())
            .OrderByDescending(si => si.CreatedDate)
            .ThenByDescending(si => si.SalesInvoiceId)
            .Select(si => new HomeSalesInvoiceFlatRow
            {
                SalesInvoiceId = si.SalesInvoiceId,
                InvoiceCode = si.SalesInvoiceCode,
                SalesInvoiceDate = si.SalesInvoiceDate,
                SubdName = si.SubDistributor.SubdName,
                CustomerName = si.Customer != null ? si.Customer.CustomerName : string.Empty
            })
            .ToListAsync(cancellationToken);

        return invoices;
    }

    public async Task<HomeSalesInvoiceDetailRow?> GetInvoiceDetailByIdAsync(
        int userId,
        int salesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await tempContext.SalesInvoices
            .AsNoTracking()
            .Where(si => si.SubDistributor.EncoderId == userId && si.SalesInvoiceId == salesInvoiceId)
            .Where(si => si.SalesInvoiceItems.Any())
            .Select(si => new HomeSalesInvoiceDetailRow
            {
                SalesInvoiceId = si.SalesInvoiceId,
                InvoiceNumber = si.SalesInvoiceCode,
                InvoiceDate = si.SalesInvoiceDate,
                CreatedDate = si.CreatedDate,
                OrderType = si.OrderType,
                OrderDate = si.OrderDate,
                CustomerName = si.Customer.CustomerName,
                CustomerBranch = si.CustomerBranch.BranchName,
                Address = si.CustomerBranch.AddressLine + ", " + si.CustomerBranch.City + ", " + si.CustomerBranch.Province,
                TotalItems = si.SalesInvoiceItems.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice == null)
        {
            return null;
        }

        var invoiceItems = await tempContext.SalesInvoiceItems
            .AsNoTracking()
            .Where(item => item.SalesInvoiceId == salesInvoiceId)
            .OrderBy(item => item.SalesInvoiceItemId)
            .Select(item => new HomeSalesInvoiceItemDetailRow
            {
                ItemCode = item.SubdItem.SubdItemCode,
                ItemName = item.SubdItem.ItemName,
                UomName = item.ItemsUom != null ? item.ItemsUom.UomName : string.Empty,
                Quantity = item.Quantity,
                Price = item.Amount
            })
            .ToListAsync(cancellationToken);

        invoice.ItemDetails = invoiceItems;
        return invoice;
    }

    public async Task<bool> DeleteInvoiceByIdAsync(
        int userId,
        int salesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await tempContext.SalesInvoices
            .FirstOrDefaultAsync(
                si => si.SalesInvoiceId == salesInvoiceId && si.SubDistributor.EncoderId == userId,
                cancellationToken);

        if (invoice == null)
        {
            return false;
        }

        await using var transaction = await tempContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var items = await tempContext.SalesInvoiceItems
                .Where(item => item.SalesInvoiceId == salesInvoiceId)
                .ToListAsync(cancellationToken);

            if (items.Any())
            {
                tempContext.SalesInvoiceItems.RemoveRange(items);
            }

            tempContext.SalesInvoices.Remove(invoice);
            await tempContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<List<SubDistributor>> GetSubDistributorsAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await tempContext.SubDistributors
            .AsNoTracking()
            .Where(s => s.EncoderId == userId && s.IsActive)
            .OrderBy(s => s.SubdCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await tempContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive, cancellationToken);
    }
}

public sealed class HomeSalesInvoiceBatchRow
{
    public string BatchId { get; set; } = string.Empty;
    public int SubDistributorId { get; set; }
    public string SubdName { get; set; } = string.Empty;
    public string SubdCode { get; set; } = string.Empty;
    public DateOnly LatestInvoiceDate { get; set; }
    public DateOnly OldestInvoiceDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateOnly BatchCreatedDate { get; set; }
    public int FirstSalesInvoiceId { get; set; }
    public int LastSalesInvoiceId { get; set; }
}

public sealed class HomeSalesInvoiceBatchInvoiceRow
{
    public int SalesInvoiceId { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public DateOnly SalesInvoiceDate { get; set; }
    public string SubdName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
}

public sealed class HomeSalesInvoiceDetailRow
{
    public int SalesInvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerBranch { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public List<HomeSalesInvoiceItemDetailRow> ItemDetails { get; set; } = new();
}

public sealed class HomeSalesInvoiceItemDetailRow
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UomName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public sealed class HomeSalesInvoiceFlatRow
{
    public int SalesInvoiceId { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public DateOnly SalesInvoiceDate { get; set; }
    public string SubdName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
}
