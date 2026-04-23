using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface IHomeService
{
    Task<List<SubDistributor>> GetSubDistributorsAsync(int userId, CancellationToken cancellationToken = default);
    Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<HomeSalesInvoiceBatchRow>> GetSalesInvoiceBatchRowsAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<HomeSalesInvoiceDetailRow>> GetBatchInvoiceDetailsAsync(
        int userId,
        int subDistributorId,
        DateOnly batchCreatedDate,
        int firstSalesInvoiceId,
        int lastSalesInvoiceId,
        CancellationToken cancellationToken = default);
}

public class HomeService : IHomeService
{
    private readonly SttprojectContext _context;

    public HomeService(SttprojectContext context)
    {
        _context = context;
    }

    public Task<List<SubDistributor>> GetSubDistributorsAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _context.SubDistributors
            .AsNoTracking()
            .Where(s => s.EncoderId == userId && s.IsActive)
            .OrderBy(s => s.SubdCode)
            .ToListAsync(cancellationToken);
    }

    public Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
    }

    public async Task<List<HomeSalesInvoiceBatchRow>> GetSalesInvoiceBatchRowsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var subdIds = await _context.SubDistributors
            .AsNoTracking()
            .Where(s => s.EncoderId == userId && s.IsActive)
            .Select(s => s.SubDistributorId)
            .ToListAsync(cancellationToken);

        if (!subdIds.Any())
        {
            return new List<HomeSalesInvoiceBatchRow>();
        }

        var invoices = await _context.SalesInvoices
            .AsNoTracking()
            .Where(si => subdIds.Contains(si.SubDistributorId))
            .Where(si => si.SalesInvoiceItems.Any())
            .Select(si => new
            {
                si.SalesInvoiceId,
                si.SubDistributorId,
                si.CreatedDate,
                si.SalesInvoiceDate,
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
                    BatchId = $"BATCH-{batchNumber:D4}",
                    SubDistributorId = invoice.SubDistributorId,
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
        var invoices = await _context.SalesInvoices
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

        var invoiceItems = await _context.SalesInvoiceItems
            .AsNoTracking()
            .Where(item => invoiceIds.Contains(item.SalesInvoiceId))
            .OrderBy(item => item.SalesInvoiceId)
            .ThenBy(item => item.SalesInvoiceItemId)
            .Select(item => new
            {
                item.SalesInvoiceId,
                item.SubdItem.SubdItemCode,
                item.SubdItem.ItemName,
                item.SubdItemUom.UomName,
                item.Quantity,
                item.Price
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
                    Price = item.Price
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
}

public sealed class HomeSalesInvoiceBatchRow
{
    public string BatchId { get; set; } = string.Empty;
    public int SubDistributorId { get; set; }
    public string SubdCode { get; set; } = string.Empty;
    public DateOnly LatestInvoiceDate { get; set; }
    public DateOnly OldestInvoiceDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateOnly BatchCreatedDate { get; set; }
    public int FirstSalesInvoiceId { get; set; }
    public int LastSalesInvoiceId { get; set; }
}

public sealed class HomeSalesInvoiceDetailRow
{
    public int SalesInvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateTime CreatedDate { get; set; }
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
