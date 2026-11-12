using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.SalesInvoice.DTOs;
using STTproject.Features.Pricing;
using System.Data;
using Microsoft.Data.SqlClient;

namespace STTproject.Features.Admin.SalesInvoice.Services;

public class AdminSalesInvoiceService : IAdminSalesInvoiceService
{
    private readonly IDbContextFactory<EntrielContext> _contextFactory;
    private readonly ILogger<AdminSalesInvoiceService> _logger;

    public AdminSalesInvoiceService(
        IDbContextFactory<EntrielContext> contextFactory,
        ILogger<AdminSalesInvoiceService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }
    // ─── User ────────────────────────────────────────────────────────────────

    public async Task<string?> GetUserNameByIdAsync(int? userId)
    {
        if (userId is null) return null;
        await using var context = _contextFactory.CreateDbContext();
        var user = await context.Users.FindAsync(userId.Value);
        return user?.FullName ?? user?.Username;
    }

    private static string? ResolveUserName(STTproject.Data.User? user)
        => user?.FullName ?? user?.Username;

    // ─── View ────────────────────────────────────────────────────────────────

    public async Task<List<SalesInvoiceListRow>> GetSalesInvoicesAsync(
        int subDistributorId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var query = context.SalesInvoices
            .AsNoTracking()
            .AsQueryable();

        if (subDistributorId > 0)
            query = query.Where(si => si.SubDistributorId == subDistributorId);

        return await query
            .OrderByDescending(si => si.SalesInvoiceDate)
            .ThenByDescending(si => si.CreatedDate)
            .Select(si => new SalesInvoiceListRow
            {
                SalesInvoiceId = si.SalesInvoiceId,
                SalesInvoiceCode = si.SalesInvoiceCode,
                SalesInvoiceDate = si.SalesInvoiceDate,
                CustomerName = si.Customer.CustomerName,
                CustomerCode = si.Customer.CustomerCode,
                SubdName = si.SubDistributor.SubdName,
                OrderType = si.OrderType,
                SalesMan = si.SalesMan,
                TotalAmount = si.SalesInvoiceItems.Sum(item => item.Amount),
                TotalItems = si.SalesInvoiceItems.Count,
                CreatedDate = si.CreatedDate,
                UpdatedDate = si.UpdatedDate,
                CreatedByName = si.CreatedByNavigation != null
                                     ? (si.CreatedByNavigation.FullName ?? si.CreatedByNavigation.Username)
                                     : null,
                UpdatedByName = si.UpdatedByNavigation != null
                                     ? (si.UpdatedByNavigation.FullName ?? si.UpdatedByNavigation.Username)
                                     : null,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<(int Year, int Month)>> GetAvailableInvoiceMonthsAsync(
    CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var raw = await context.SalesInvoices
            .AsNoTracking()
            .Select(si => new { si.SalesInvoiceDate.Year, si.SalesInvoiceDate.Month })
            .Distinct()
            .ToListAsync(cancellationToken);

        return raw
            .Select(x => (x.Year, x.Month))
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToList();
    }

    public async Task<(List<SalesInvoiceListRow> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        string? search,
        string? orderType,
        int? subDistributorId,
        string? principal,
        int? month,
        int? year,
        string sortColumn,
        bool sortAscending,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var query = context.SalesInvoices.AsNoTracking().AsQueryable();

        // ── Filters ───────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(si =>
                si.SalesInvoiceCode.Contains(search) ||
                si.Customer.CustomerName.Contains(search) ||
                si.Customer.CustomerCode.Contains(search));

        if (!string.IsNullOrWhiteSpace(orderType))
            query = query.Where(si => si.OrderType.ToLower() == orderType.ToLower());

        if (subDistributorId > 0)
            query = query.Where(si => si.SubDistributorId == subDistributorId);

        if (!string.IsNullOrWhiteSpace(principal))
            query = query.Where(si =>
                si.SalesInvoiceItems.Any(item => item.SubdItem.CompanyItem.Principal == principal));

        if (month.HasValue && month.Value > 0)
            query = query.Where(si => si.SalesInvoiceDate.Month == month.Value);

        if (year.HasValue && year.Value > 0)
            query = query.Where(si => si.SalesInvoiceDate.Year == year.Value);

        // ── Total count ───────────────────────────────────────────────────────
        var total = await query.CountAsync(cancellationToken);

        // ── Sorting ───────────────────────────────────────────────────────────
        query = sortColumn switch
        {
            "SalesInvoiceCode" => sortAscending ? query.OrderBy(si => si.SalesInvoiceCode) : query.OrderByDescending(si => si.SalesInvoiceCode),
            "SalesInvoiceDate" => sortAscending ? query.OrderByDescending(si => si.SalesInvoiceDate) : query.OrderBy(si => si.SalesInvoiceDate),
            "CustomerName" => sortAscending ? query.OrderBy(si => si.Customer.CustomerName) : query.OrderByDescending(si => si.Customer.CustomerName),
            "OrderType" => sortAscending ? query.OrderBy(si => si.OrderType) : query.OrderByDescending(si => si.OrderType),
            "SubDistributor" => sortAscending ? query.OrderBy(si => si.SubDistributor.SubdName) : query.OrderByDescending(si => si.SubDistributor.SubdName),
            "CreatedDate" => sortAscending ? query.OrderByDescending(si => si.CreatedDate) : query.OrderBy(si => si.CreatedDate),
            _ => query.OrderByDescending(si => si.SalesInvoiceDate)
        };

        // ── Paging ────────────────────────────────────────────────────────────
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(si => new SalesInvoiceListRow
            {
                SalesInvoiceId = si.SalesInvoiceId,
                SalesInvoiceCode = si.SalesInvoiceCode,
                SalesInvoiceDate = si.SalesInvoiceDate,
                CustomerName = si.Customer.CustomerName,
                CustomerCode = si.Customer.CustomerCode,
                SubdName = si.SubDistributor.SubdName,
                OrderType = si.OrderType,
                SalesMan = si.SalesMan,
                TotalAmount = si.SalesInvoiceItems.Sum(item => item.Amount),
                TotalItems = si.SalesInvoiceItems.Count,
                CreatedDate = si.CreatedDate,
                UpdatedDate = si.UpdatedDate,
                CreatedByName = si.CreatedByNavigation != null
                                    ? (si.CreatedByNavigation.FullName ?? si.CreatedByNavigation.Username)
                                    : null,
                UpdatedByName = si.UpdatedByNavigation != null
                                    ? (si.UpdatedByNavigation.FullName ?? si.UpdatedByNavigation.Username)
                                    : null,
            })
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<List<string>> GetPrincipalsForDropdownAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.CompanyItems
            .AsNoTracking()
            .Where(ci => !string.IsNullOrWhiteSpace(ci.Principal))
            .Select(ci => ci.Principal)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(cancellationToken);
    }
    public async Task<SalesInvoiceDetailDto?> GetSalesInvoiceDetailAsync(
        int salesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.SalesInvoices
            .AsNoTracking()
            .Where(si => si.SalesInvoiceId == salesInvoiceId)
            .Select(si => new SalesInvoiceDetailDto
            {
                SalesInvoiceId = si.SalesInvoiceId,
                SalesInvoiceCode = si.SalesInvoiceCode,
                SalesInvoiceDate = si.SalesInvoiceDate,
                CustomerId = si.CustomerId,
                CustomerName = si.Customer.CustomerName,
                CustomerCode = si.Customer.CustomerCode,
                SubDistributorId = si.SubDistributorId,
                SubdName = si.SubDistributor.SubdName,
                OrderType = si.OrderType,
                SalesMan = si.SalesMan,
                CreatedDate = si.CreatedDate,
                UpdatedDate = si.UpdatedDate,
                CreatedByName = si.CreatedByNavigation != null
                                     ? (si.CreatedByNavigation.FullName ?? si.CreatedByNavigation.Username)
                                     : null,
                UpdatedByName = si.UpdatedByNavigation != null
                                     ? (si.UpdatedByNavigation.FullName ?? si.UpdatedByNavigation.Username)
                                     : null,
                Items = si.SalesInvoiceItems
                    .Select(item => new SalesInvoiceItemDto
                    {
                        SalesInvoiceItemId = item.SalesInvoiceItemId,
                        SubdItemId = item.SubdItemId,
                        SubdItemCode = item.SubdItem.SubdItemCode,
                        ItemName = item.SubdItem.ItemName,
                        ItemsUomId = item.ItemsUomId,
                        UomName = item.ItemsUom.UomName,
                        UomPrice = item.Quantity != 0 ? item.Amount / item.Quantity : 0m,
                        UnitPrice = item.Quantity != 0 ? item.Amount / item.Quantity : 0m,
                        Quantity = item.Quantity,
                        Amount = item.Amount
                    })
                    .OrderBy(item => item.SubdItemCode)
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> InvoiceCodeExistsAsync(
        string code,
        int subDistributorId,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        var normalized = code.Trim();

        return await context.SalesInvoices
            .AsNoTracking()
            .Where(si => si.SubDistributorId == subDistributorId)
            .Where(si => si.SalesInvoiceCode == normalized)
            .Where(si => !excludeId.HasValue || si.SalesInvoiceId != excludeId.Value)
            .AnyAsync(cancellationToken);
    }

    // ─── Dropdowns ───────────────────────────────────────────────────────────

    public async Task<List<SalesInvoiceCustomerDropdownItem>> GetCustomersForDropdownAsync(
        int subDistributorId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive && c.SubDistributorId == subDistributorId)
            .OrderBy(c => c.CustomerName)
            .Select(c => new SalesInvoiceCustomerDropdownItem
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SalesInvoiceItemDropdownItem>> GetItemsForDropdownAsync(
    CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.SalesInvoiceItems
            .AsNoTracking()
            .Select(sii => new { sii.SubdItemId, sii.SubdItem.SubdItemCode, sii.SubdItem.ItemName })
            .Distinct()
            .OrderBy(x => x.SubdItemCode)
            .Select(x => new SalesInvoiceItemDropdownItem
            {
                SubdItemId = x.SubdItemId,
                SubdItemCode = x.SubdItemCode,
                ItemName = x.ItemName
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SalesInvoiceSubdItemDropdownItem>> GetSubdItemsForDropdownAsync(
        int subDistributorId,
        DateOnly salesInvoiceDate,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var items = await context.SubdItems
            .AsNoTracking()
            .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
            .OrderBy(si => si.SubdItemCode)
            .Select(si => new { si.SubdItemId, si.SubdItemCode, si.ItemName })
            .ToListAsync(cancellationToken);

        var prices = await InvoicePricing.LoadUomPricesAsync(
            context, subDistributorId, salesInvoiceDate, items.Select(i => i.SubdItemId), cancellationToken);

        var activeUomIds = await context.ItemsUoms.AsNoTracking()
            .Where(u => u.IsActive && u.SubdItem.SubDistributorId == subDistributorId)
            .Select(u => u.ItemsUomId)
            .ToListAsync(cancellationToken);
        var active = activeUomIds.ToHashSet();

        var bySubdItem = prices.Values
            .Where(p => active.Contains(p.ItemsUomId))
            .ToLookup(p => p.SubdItemId);

        return items.Select(si => new SalesInvoiceSubdItemDropdownItem
        {
            SubdItemId = si.SubdItemId,
            SubdItemCode = si.SubdItemCode,
            ItemName = si.ItemName,
            Uoms = bySubdItem[si.SubdItemId]
                .OrderBy(p => p.ConversionToBase)
                .Select(p => new SalesInvoiceUomOption
                {
                    ItemsUomId = p.ItemsUomId,
                    UomName = p.UomName,
                    Price = p.UnitPrice,
                    ConversionToBase = p.ConversionToBase,
                    StockConvToBase = p.StockConvToBase,
                    StockPrice = p.StockPrice,
                    PriceIssue = p.Issue
                })
                .ToList()
        }).ToList();
    }

    // In AdminSalesInvoiceService:
    public async Task<List<SalesInvoiceSubDistributorDropdownItem>> GetSubDistributorsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.SubDistributors
            .AsNoTracking()
            .OrderBy(sd => sd.SubdName)
            .Select(sd => new SalesInvoiceSubDistributorDropdownItem
            {
                SubDistributorId = sd.SubDistributorId,
                SubdName = sd.SubdName
            })
            .ToListAsync(cancellationToken);
    }

    // ─── Create ──────────────────────────────────────────────────────────────

    public async Task<SalesInvoiceResult> CreateSalesInvoiceAsync(
        CreateSalesInvoiceDto dto,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var codeExists = await context.SalesInvoices
                .AsNoTracking()
                .AnyAsync(si => si.SubDistributorId == dto.SubDistributorId
                             && si.SalesInvoiceCode == dto.SalesInvoiceCode.Trim(),
                          cancellationToken);

            if (codeExists)
                return SalesInvoiceResult.Duplicate($"Invoice code '{dto.SalesInvoiceCode}' already exists for this sub distributor.");

            var lineKeys = dto.Items.Select(i => (i.SubdItemId, i.ItemsUomId, i.Quantity)).ToList();
            var prices = await InvoicePricing.LoadUomPricesAsync(
                context, dto.SubDistributorId, dto.SalesInvoiceDate, lineKeys.Select(k => k.SubdItemId), cancellationToken);

            var (priced, priceError) = PriceItems(lineKeys, prices, IsCredit(dto.OrderType));
            if (priceError is not null)
                return SalesInvoiceResult.Failed(priceError);

            var now = DateTime.UtcNow;

            var invoice = new STTproject.Data.SalesInvoice
            {
                SalesInvoiceCode = dto.SalesInvoiceCode.Trim(),
                SalesInvoiceDate = dto.SalesInvoiceDate,
                CustomerId = dto.CustomerId,
                SubDistributorId = dto.SubDistributorId,
                OrderType = dto.OrderType.Trim(),
                SalesMan = dto.SalesMan?.Trim(),
                CreatedDate = DateTime.UtcNow,
                CreatedBy = createdByUserId,
                SalesInvoiceItems = dto.Items.Select((i, idx) => new SalesInvoiceItem
                {
                    SubdItemId = i.SubdItemId,
                    ItemsUomId = i.ItemsUomId,
                    Quantity = i.Quantity,
                    Amount = priced![idx].Amount,
                    BaseQuantity = priced[idx].BaseQuantity,
                    StockPriceUsed = priced[idx].StockPrice,
                    CompanyItemPriceHistoryId = priced[idx].HistoryId,
                    PriceSource = "Manual",
                    PricedAt = now,
                    CreatedDate = now,
                    CreatedBy = createdByUserId
                }).ToList()
            };

            context.SalesInvoices.Add(invoice);
            await context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return SalesInvoiceResult.Success(invoice.SalesInvoiceId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            var msg = ex.GetBaseException()?.Message ?? ex.Message;
            _logger.LogError(ex, "Error creating SalesInvoice: {Message}", msg);
            return SalesInvoiceResult.Failed("Unable to create the sales invoice.");
        }
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    public async Task<SalesInvoiceResult> UpdateSalesInvoiceAsync(
        UpdateSalesInvoiceDto dto,
        int updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var existing = await context.SalesInvoices
                .Include(si => si.SalesInvoiceItems)
                .FirstOrDefaultAsync(si => si.SalesInvoiceId == dto.SalesInvoiceId, cancellationToken);

            if (existing is null)
                return SalesInvoiceResult.NotFound();

            var codeExists = await context.SalesInvoices
                .AsNoTracking()
                .AnyAsync(si => si.SubDistributorId == dto.SubDistributorId
                             && si.SalesInvoiceCode == dto.SalesInvoiceCode.Trim()
                             && si.SalesInvoiceId != dto.SalesInvoiceId,
                          cancellationToken);

            if (codeExists)
                return SalesInvoiceResult.Duplicate($"Invoice code '{dto.SalesInvoiceCode}' already exists for this sub distributor.");

            var lineKeys = dto.Items.Select(i => (i.SubdItemId, i.ItemsUomId, i.Quantity)).ToList();
            var prices = await InvoicePricing.LoadUomPricesAsync(
                context, dto.SubDistributorId, dto.SalesInvoiceDate, lineKeys.Select(k => k.SubdItemId), cancellationToken);

            var (priced, priceError) = PriceItems(lineKeys, prices, IsCredit(dto.OrderType));
            if (priceError is not null)
                return SalesInvoiceResult.Failed(priceError);

            var now = DateTime.UtcNow;

            existing.SalesInvoiceCode = dto.SalesInvoiceCode.Trim();
            existing.SalesInvoiceDate = dto.SalesInvoiceDate;
            existing.CustomerId = dto.CustomerId;
            existing.SubDistributorId = dto.SubDistributorId;
            existing.OrderType = dto.OrderType.Trim();
            existing.SalesMan = dto.SalesMan?.Trim();
            existing.UpdatedDate = DateTime.UtcNow;
            existing.UpdatedBy = updatedByUserId;

            var incomingIds = dto.Items
                .Where(i => i.SalesInvoiceItemId > 0)
                .Select(i => i.SalesInvoiceItemId)
                .ToHashSet();

            var toDelete = existing.SalesInvoiceItems
                .Where(i => !incomingIds.Contains(i.SalesInvoiceItemId))
                .ToList();

            context.SalesInvoiceItems.RemoveRange(toDelete);

            for (var idx = 0; idx < dto.Items.Count; idx++)
            {
                var itemDto = dto.Items[idx];
                var p = priced![idx];

                if (itemDto.SalesInvoiceItemId > 0)
                {
                    var existingItem = existing.SalesInvoiceItems
                        .FirstOrDefault(i => i.SalesInvoiceItemId == itemDto.SalesInvoiceItemId);

                    if (existingItem is not null)
                    {
                        existingItem.SubdItemId = itemDto.SubdItemId;
                        existingItem.ItemsUomId = itemDto.ItemsUomId;
                        existingItem.Quantity = itemDto.Quantity;
                        existingItem.Amount = p.Amount;
                        existingItem.BaseQuantity = p.BaseQuantity;
                        existingItem.StockPriceUsed = p.StockPrice;
                        existingItem.CompanyItemPriceHistoryId = p.HistoryId;
                        existingItem.PriceSource = "Manual";
                        existingItem.PricedAt = now;
                        existingItem.UpdatedDate = now;
                        existingItem.UpdatedBy = updatedByUserId;
                    }
                }
                else
                {
                    existing.SalesInvoiceItems.Add(new SalesInvoiceItem
                    {
                        SubdItemId = itemDto.SubdItemId,
                        ItemsUomId = itemDto.ItemsUomId,
                        Quantity = itemDto.Quantity,
                        Amount = p.Amount,
                        BaseQuantity = p.BaseQuantity,
                        StockPriceUsed = p.StockPrice,
                        CompanyItemPriceHistoryId = p.HistoryId,
                        PriceSource = "Manual",
                        PricedAt = now,
                        CreatedDate = now,
                        CreatedBy = updatedByUserId
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return SalesInvoiceResult.Success(existing.SalesInvoiceId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            var msg = ex.GetBaseException()?.Message ?? ex.Message;
            _logger.LogError(ex, "Error updating SalesInvoice {Id}: {Message}", dto.SalesInvoiceId, msg);
            return SalesInvoiceResult.Failed("Unable to update the sales invoice.");
        }
    }

    // ─── Delete (archive, then remove) ───────────────────────────────────────

    public async Task<DeleteSalesInvoiceResult> DeleteSalesInvoiceAsync(
        int salesInvoiceId, int deletedByUserId, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var subdId = await context.SalesInvoices.AsNoTracking()
                .Where(si => si.SalesInvoiceId == salesInvoiceId)
                .Select(si => (int?)si.SubDistributorId)
                .FirstOrDefaultAsync(ct);

            if (subdId is null)
                return DeleteSalesInvoiceResult.NotFound();

            await ArchiveAndDeleteAsync(context, new ArchiveRequest(
                DeletionType: "Individual",
                SubDistributorId: subdId,
                InvoiceId: salesInvoiceId,
                DeletedByUserId: deletedByUserId), ct);

            await tx.CommitAsync(ct);
            return DeleteSalesInvoiceResult.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error deleting SalesInvoice {Id}", salesInvoiceId);
            return DeleteSalesInvoiceResult.Failed(
                $"Unable to delete the sales invoice: {ex.GetBaseException().Message}");
        }
    }

    // ─── Batch delete ────────────────────────────────────────────────────────

    public async Task<int> CountForBatchDeleteAsync(
        int subDistributorId, int year, int month,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);

        return await context.SalesInvoices
            .AsNoTracking()
            .CountAsync(si => si.SubDistributorId == subDistributorId
                           && si.SalesInvoiceDate >= start
                           && si.SalesInvoiceDate < end, cancellationToken);
    }

    public async Task<BatchDeleteResult> DeleteBatchAsync(
        int subDistributorId, int year, int month, int deletedByUserId,
        CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var start = new DateOnly(year, month, 1);

            var deleted = await ArchiveAndDeleteAsync(context, new ArchiveRequest(
                DeletionType: "Batch",
                SubDistributorId: subDistributorId,
                Start: start,
                End: start.AddMonths(1),
                Year: year,
                Month: month,
                DeletedByUserId: deletedByUserId), ct);

            await tx.CommitAsync(ct);
            return new BatchDeleteResult(true, deleted);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error batch deleting {Subd}/{Year}-{Month}",
                subDistributorId, year, month);
            return new BatchDeleteResult(false, 0,
                $"Unable to delete the batch: {ex.GetBaseException().Message}");
        }
    }

    // ─── Archive helper ──────────────────────────────────────────────────────

    private sealed record ArchiveRequest(
        string DeletionType,
        int? SubDistributorId,
        int DeletedByUserId,
        int? InvoiceId = null,          // Individual
        string? IdsCsv = null,          // Selected
        DateOnly? Start = null,         // Batch
        DateOnly? End = null,
        int? Year = null,
        int? Month = null);

    /// <summary>
    /// Must be called inside a transaction. Copies invoices and items into the archive
    /// tables, then deletes the originals. Returns the number of invoices archived.
    /// </summary>
    private static async Task<int> ArchiveAndDeleteAsync(
        EntrielContext context, ArchiveRequest r, CancellationToken ct)
    {
        // 1. Batch header. ToListAsync, because SingleAsync wraps the query in
        //    SELECT TOP(2), which SQL Server rejects for INSERT ... OUTPUT.
        var batchId = (await context.Database
            .SqlQuery<int>($@"
                INSERT INTO SalesInvoiceDeletionBatch
                    (DeletionType, SubDistributorId, PeriodYear, PeriodMonth, InvoiceCount, DeletedBy)
                OUTPUT INSERTED.DeletionBatchId AS Value
                VALUES ({r.DeletionType}, {r.SubDistributorId}, {r.Year}, {r.Month}, 0, {r.DeletedByUserId})")
            .ToListAsync(ct)).Single();

        // 2. Copy invoices (one statement per mode, so no nullable-parameter tricks)
        int invoiceCount;

        if (r.InvoiceId.HasValue)
        {
            invoiceCount = await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO DeletedSalesInvoice
                    (DeletionBatchId, OriginalSalesInvoiceId, SalesInvoiceCode, SalesInvoiceDate, CustomerId,
                     SubDistributorId, OrderType, SalesMan, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
                SELECT {batchId}, si.SalesInvoiceId, si.SalesInvoiceCode, si.SalesInvoiceDate, si.CustomerId,
                       si.SubDistributorId, si.OrderType, si.SalesMan, si.CreatedDate, si.CreatedBy,
                       si.UpdatedDate, si.UpdatedBy
                FROM SalesInvoice si
                WHERE si.SalesInvoiceId = {r.InvoiceId.Value}", ct);
        }
        else if (r.IdsCsv is not null)
        {
            invoiceCount = await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO DeletedSalesInvoice
                    (DeletionBatchId, OriginalSalesInvoiceId, SalesInvoiceCode, SalesInvoiceDate, CustomerId,
                     SubDistributorId, OrderType, SalesMan, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
                SELECT {batchId}, si.SalesInvoiceId, si.SalesInvoiceCode, si.SalesInvoiceDate, si.CustomerId,
                       si.SubDistributorId, si.OrderType, si.SalesMan, si.CreatedDate, si.CreatedBy,
                       si.UpdatedDate, si.UpdatedBy
                FROM SalesInvoice si
                WHERE si.SalesInvoiceId IN (SELECT CAST(value AS INT) FROM STRING_SPLIT({r.IdsCsv}, ','))", ct);
        }
        else
        {
            invoiceCount = await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO DeletedSalesInvoice
                    (DeletionBatchId, OriginalSalesInvoiceId, SalesInvoiceCode, SalesInvoiceDate, CustomerId,
                     SubDistributorId, OrderType, SalesMan, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
                SELECT {batchId}, si.SalesInvoiceId, si.SalesInvoiceCode, si.SalesInvoiceDate, si.CustomerId,
                       si.SubDistributorId, si.OrderType, si.SalesMan, si.CreatedDate, si.CreatedBy,
                       si.UpdatedDate, si.UpdatedBy
                FROM SalesInvoice si
                WHERE si.SubDistributorId = {r.SubDistributorId}
                  AND si.SalesInvoiceDate >= {r.Start}
                  AND si.SalesInvoiceDate <  {r.End}", ct);
        }

        // Nothing matched: remove the empty header and stop.
        if (invoiceCount == 0)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM SalesInvoiceDeletionBatch WHERE DeletionBatchId = {batchId}", ct);
            return 0;
        }

        // 3. Copy items
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO DeletedSalesInvoiceItem
                (DeletedSalesInvoiceId, OriginalSalesInvoiceItemId, SubdItemId, ItemsUomId, Quantity, Amount,
                BaseQuantity, StockPriceUsed, CompanyItemPriceHistoryId, PriceSource, PricedAt, BilledAmount,
                CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
            SELECT d.DeletedSalesInvoiceId, sii.SalesInvoiceItemId, sii.SubdItemId, sii.ItemsUomId,
                sii.Quantity, sii.Amount,
                sii.BaseQuantity, sii.StockPriceUsed, sii.CompanyItemPriceHistoryId, sii.PriceSource, sii.PricedAt, sii.BilledAmount,
                sii.CreatedDate, sii.CreatedBy, sii.UpdatedDate, sii.UpdatedBy
            FROM DeletedSalesInvoice d
            JOIN SalesInvoiceItem sii ON sii.SalesInvoiceId = d.OriginalSalesInvoiceId
            WHERE d.DeletionBatchId = {batchId}", ct);

        // 4. Final count on the header
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE SalesInvoiceDeletionBatch SET InvoiceCount = {invoiceCount}
            WHERE DeletionBatchId = {batchId}", ct);

        // 5. Delete originals, children first
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE sii FROM SalesInvoiceItem sii
            JOIN DeletedSalesInvoice d ON d.OriginalSalesInvoiceId = sii.SalesInvoiceId
            WHERE d.DeletionBatchId = {batchId}", ct);

        await context.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE si FROM SalesInvoice si
            JOIN DeletedSalesInvoice d ON d.OriginalSalesInvoiceId = si.SalesInvoiceId
            WHERE d.DeletionBatchId = {batchId}", ct);

        return invoiceCount;
    }

    // ─── Deleted invoices (archive) ──────────────────────────────────────────
    public async Task<List<(int Year, int Month)>> GetDeletedInvoiceMonthsAsync(
        CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var raw = await context.Database.SqlQuery<int>($@"
            SELECT DISTINCT YEAR(SalesInvoiceDate) * 100 + MONTH(SalesInvoiceDate) AS Value
            FROM DeletedSalesInvoice").ToListAsync(ct);

        return raw
            .Select(v => (Year: v / 100, Month: v % 100))
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToList();
    }
    public async Task<(List<DeletedBatchRow> Items, int Total)> GetDeletedBatchesPagedAsync(
        int page, int pageSize, string? type, string? status, int? subDistributorId,
        int? month, int? year,
        string? search, string? sortColumn, bool sortAscending,
        CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var query = context.Database.SqlQuery<DeletedBatchRaw>($@"
            SELECT DeletionBatchId, DeletionType, SubDistributorId, PeriodYear, PeriodMonth,
                InvoiceCount, Status, DeletedBy, DeletedDate, RestoredBy, RestoredDate
            FROM SalesInvoiceDeletionBatch");

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(b => b.DeletionType == type);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(b => b.Status == status);
        if (subDistributorId > 0)
            query = query.Where(b => b.SubDistributorId == subDistributorId);
        if (month > 0 || year > 0)
        {
            var m = month ?? 0;
            var y = year ?? 0;

            var batchIds = await context.Database.SqlQuery<int>($@"
                SELECT DISTINCT DeletionBatchId AS Value
                FROM DeletedSalesInvoice
                WHERE ({y} = 0 OR YEAR(SalesInvoiceDate) = {y})
                AND ({m} = 0 OR MONTH(SalesInvoiceDate) = {m})").ToListAsync(ct);

            query = query.Where(b => batchIds.Contains(b.DeletionBatchId));
        }

        var raw = await query.ToListAsync(ct);

        // Resolve names
        var subdNames = await context.SubDistributors.AsNoTracking()
            .ToDictionaryAsync(s => s.SubDistributorId, s => s.SubdName, ct);

        var userIds = raw.Select(b => (int?)b.DeletedBy)
            .Concat(raw.Select(b => b.RestoredBy))
            .Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();

        var userNames = new Dictionary<int, string?>();
        foreach (var id in userIds)
            userNames[id] = await GetUserNameByIdAsync(id);

        string? Name(int? id) => id.HasValue && userNames.TryGetValue(id.Value, out var n) ? n : null;

        var rows = raw.Select(b => (
            Raw: b,
            Row: new DeletedBatchRow
            {
                DeletionBatchId = b.DeletionBatchId,
                DeletionType = b.DeletionType,
                SubdName = b.SubDistributorId.HasValue && subdNames.TryGetValue(b.SubDistributorId.Value, out var sn)
                    ? sn ?? "—" : "Multiple / —",
                Period = b.PeriodYear.HasValue && b.PeriodMonth.HasValue
                    ? new DateTime(b.PeriodYear.Value, b.PeriodMonth.Value, 1).ToString("MMMM yyyy")
                    : "—",
                InvoiceCount = b.InvoiceCount,
                Status = b.Status,
                DeletedByName = Name(b.DeletedBy),
                DeletedDate = b.DeletedDate,
                RestoredByName = Name(b.RestoredBy),
                RestoredDate = b.RestoredDate
            })).ToList();

        // Search (batch id, type, subdistributor, period, deleted by, status)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().TrimStart('#');
            bool Has(string? s) => s?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

            rows = rows.Where(x =>
                x.Row.DeletionBatchId.ToString() == term ||
                Has(x.Row.DeletionType) || Has(x.Row.SubdName) || Has(x.Row.Period) ||
                Has(x.Row.DeletedByName) || Has(x.Row.Status)).ToList();
        }

        var total = rows.Count;

        // Sort
        Func<(DeletedBatchRaw Raw, DeletedBatchRow Row), object?> key = sortColumn switch
        {
            "Type" => x => x.Row.DeletionType,
            "Subdistributor" => x => x.Row.SubdName,
            "Period" => x => (x.Raw.PeriodYear ?? 0) * 100 + (x.Raw.PeriodMonth ?? 0),
            "Invoices" => x => x.Row.InvoiceCount,
            "DeletedBy" => x => x.Row.DeletedByName ?? string.Empty,
            "Status" => x => x.Row.Status,
            _ => x => x.Row.DeletedDate
        };

        var ascending = sortColumn is null or "DeletedDate" or "Period" ? !sortAscending : sortAscending;

        var sorted = ascending ? rows.OrderBy(key) : rows.OrderByDescending(key);
        var items = sorted
            .ThenByDescending(x => x.Row.DeletionBatchId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Row)
            .ToList();

        return (items, total);
    }
    public async Task<(List<DeletedInvoiceDetailRow> Items, int Total)> GetDeletedBatchInvoicesPagedAsync(
        int deletionBatchId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var headers = context.Database.SqlQuery<DeletedInvoiceHeaderRaw>($@"
            SELECT DeletedSalesInvoiceId, SalesInvoiceCode, SalesInvoiceDate, CustomerId, OrderType, SalesMan
            FROM DeletedSalesInvoice
            WHERE DeletionBatchId = {deletionBatchId}");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            headers = headers.Where(h => h.SalesInvoiceCode.Contains(term));
        }

        var total = await headers.CountAsync(ct);

        var pageHeaders = await headers
            .OrderBy(h => h.SalesInvoiceDate).ThenBy(h => h.SalesInvoiceCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        if (pageHeaders.Count == 0)
            return (new List<DeletedInvoiceDetailRow>(), total);

        var ids = pageHeaders.Select(h => h.DeletedSalesInvoiceId).ToList();

        var rawItems = await context.Database.SqlQuery<DeletedInvoiceItemRaw>($@"
            SELECT i.DeletedSalesInvoiceItemId, i.DeletedSalesInvoiceId, i.SubdItemId, i.ItemsUomId,
                i.Quantity, i.Amount
            FROM DeletedSalesInvoiceItem i
            JOIN DeletedSalesInvoice d ON d.DeletedSalesInvoiceId = i.DeletedSalesInvoiceId
            WHERE d.DeletionBatchId = {deletionBatchId}")
            .Where(i => ids.Contains(i.DeletedSalesInvoiceId))
            .OrderBy(i => i.DeletedSalesInvoiceItemId)
            .ToListAsync(ct);

        // Resolve display names through EF sets (no guessing table names)
        var customerIds = pageHeaders.Select(h => h.CustomerId).Distinct().ToList();
        var customers = await context.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => $"{c.CustomerCode} – {c.CustomerName}", ct);

        var subdItemIds = rawItems.Select(i => i.SubdItemId).Distinct().ToList();
        var subdItems = await context.SubdItems.AsNoTracking()
            .Where(s => subdItemIds.Contains(s.SubdItemId))
            .ToDictionaryAsync(s => s.SubdItemId, s => (s.SubdItemCode, s.ItemName), ct);

        var uomIds = rawItems.Select(i => i.ItemsUomId).Distinct().ToList();
        var uoms = await context.ItemsUoms.AsNoTracking()
            .Where(u => uomIds.Contains(u.ItemsUomId))
            .ToDictionaryAsync(u => u.ItemsUomId, u => u.UomName, ct);

        var itemsByInvoice = rawItems
            .GroupBy(i => i.DeletedSalesInvoiceId)
            .ToDictionary(g => g.Key, g => g.Select(i =>
            {
                subdItems.TryGetValue(i.SubdItemId, out var si);
                return new DeletedInvoiceItemRow
                {
                    ItemCode = si.SubdItemCode ?? "—",
                    ItemName = si.ItemName ?? "(item removed)",
                    UomName = uoms.TryGetValue(i.ItemsUomId, out var un) ? un : "—",
                    Quantity = i.Quantity,
                    UnitPrice = i.Quantity != 0 ? i.Amount / i.Quantity : 0m,
                    Amount = i.Amount
                };
            }).ToList());

        var rows = pageHeaders.Select(h => new DeletedInvoiceDetailRow
        {
            DeletedSalesInvoiceId = h.DeletedSalesInvoiceId,
            SalesInvoiceCode = h.SalesInvoiceCode,
            SalesInvoiceDate = DateOnly.FromDateTime(h.SalesInvoiceDate),
            CustomerName = customers.TryGetValue(h.CustomerId, out var cn) ? cn : "(customer removed)",
            OrderType = h.OrderType,
            SalesMan = h.SalesMan,
            Items = itemsByInvoice.TryGetValue(h.DeletedSalesInvoiceId, out var list)
                ? list : new List<DeletedInvoiceItemRow>()
        }).ToList();

        return (rows, total);
    }

    public async Task<RestoreBatchResult> RestoreBatchAsync(
        int deletionBatchId, int restoredByUserId, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var status = (await context.Database.SqlQuery<string>($@"
                SELECT Status AS Value FROM SalesInvoiceDeletionBatch
                WHERE DeletionBatchId = {deletionBatchId}").ToListAsync(ct)).FirstOrDefault();

            if (status is null) return new RestoreBatchResult(false, 0, "Batch not found.");
            if (status != "Deleted") return new RestoreBatchResult(false, 0, "This batch has already been restored.");

            // ── Validate against live data ─────────────────────────────────────
            var customerIds = await context.Database.SqlQuery<int>($@"
                SELECT DISTINCT CustomerId AS Value FROM DeletedSalesInvoice
                WHERE DeletionBatchId = {deletionBatchId}").ToListAsync(ct);

            var existingCustomers = await context.Customers.AsNoTracking()
                .Where(c => customerIds.Contains(c.CustomerId))
                .Select(c => c.CustomerId).ToListAsync(ct);

            var missingCustomers = customerIds.Except(existingCustomers).Count();

            var subdItemIds = await context.Database.SqlQuery<int>($@"
                SELECT DISTINCT i.SubdItemId AS Value
                FROM DeletedSalesInvoiceItem i
                JOIN DeletedSalesInvoice d ON d.DeletedSalesInvoiceId = i.DeletedSalesInvoiceId
                WHERE d.DeletionBatchId = {deletionBatchId}").ToListAsync(ct);

            var existingSubdItems = await context.SubdItems.AsNoTracking()
                .Where(s => subdItemIds.Contains(s.SubdItemId))
                .Select(s => s.SubdItemId).ToListAsync(ct);

            var uomIds = await context.Database.SqlQuery<int>($@"
                SELECT DISTINCT i.ItemsUomId AS Value
                FROM DeletedSalesInvoiceItem i
                JOIN DeletedSalesInvoice d ON d.DeletedSalesInvoiceId = i.DeletedSalesInvoiceId
                WHERE d.DeletionBatchId = {deletionBatchId}").ToListAsync(ct);

            var existingUoms = await context.ItemsUoms.AsNoTracking()
                .Where(u => uomIds.Contains(u.ItemsUomId))
                .Select(u => u.ItemsUomId).ToListAsync(ct);

            var missingRefs = missingCustomers
                + subdItemIds.Except(existingSubdItems).Count()
                + uomIds.Except(existingUoms).Count();

            if (missingRefs > 0)
                return new RestoreBatchResult(false, 0,
                    "Cannot restore: some customers, items or UOMs used by these invoices no longer exist.");

            // Code conflicts (a replacement invoice may have been created since)
            var archived = await context.Database.SqlQuery<DeletedInvoiceCodeKey>($@"
                SELECT SubDistributorId, SalesInvoiceCode FROM DeletedSalesInvoice
                WHERE DeletionBatchId = {deletionBatchId}").ToListAsync(ct);

            var codes = archived.Select(a => a.SalesInvoiceCode).Distinct().ToList();
            var live = await context.SalesInvoices.AsNoTracking()
                .Where(si => codes.Contains(si.SalesInvoiceCode))
                .Select(si => new { si.SubDistributorId, si.SalesInvoiceCode })
                .ToListAsync(ct);

            var liveSet = live.Select(l => (l.SubDistributorId, l.SalesInvoiceCode)).ToHashSet();
            var conflicts = archived.Count(a => liveSet.Contains((a.SubDistributorId, a.SalesInvoiceCode)));

            if (conflicts > 0)
                return new RestoreBatchResult(false, 0,
                    $"Cannot restore: {conflicts} invoice code(s) already exist again for the same sub-distributor.");

            // ── Restore (original IDs, parents first) ──────────────────────────
            var restored = await context.Database.ExecuteSqlInterpolatedAsync($@"
                SET IDENTITY_INSERT SalesInvoice ON;
                INSERT INTO SalesInvoice
                    (SalesInvoiceId, SalesInvoiceCode, SalesInvoiceDate, CustomerId, SubDistributorId,
                    OrderType, SalesMan, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
                SELECT OriginalSalesInvoiceId, SalesInvoiceCode, SalesInvoiceDate, CustomerId, SubDistributorId,
                    OrderType, SalesMan, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy
                FROM DeletedSalesInvoice WHERE DeletionBatchId = {deletionBatchId};
                SET IDENTITY_INSERT SalesInvoice OFF;", ct);

            await context.Database.ExecuteSqlInterpolatedAsync($@"
                SET IDENTITY_INSERT SalesInvoiceItem ON;
                INSERT INTO SalesInvoiceItem
                    (SalesInvoiceItemId, SalesInvoiceId, SubdItemId, ItemsUomId, Quantity, Amount,
                    BaseQuantity, StockPriceUsed, CompanyItemPriceHistoryId, PriceSource, PricedAt, BilledAmount,
                    CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
                SELECT i.OriginalSalesInvoiceItemId, d.OriginalSalesInvoiceId, i.SubdItemId, i.ItemsUomId,
                    i.Quantity, i.Amount,
                    i.BaseQuantity, i.StockPriceUsed, i.CompanyItemPriceHistoryId, i.PriceSource, i.PricedAt, i.BilledAmount,
                    i.CreatedDate, i.CreatedBy, i.UpdatedDate, i.UpdatedBy
                FROM DeletedSalesInvoiceItem i
                JOIN DeletedSalesInvoice d ON d.DeletedSalesInvoiceId = i.DeletedSalesInvoiceId
                WHERE d.DeletionBatchId = {deletionBatchId};
                SET IDENTITY_INSERT SalesInvoiceItem OFF;", ct);

            await context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE SalesInvoiceDeletionBatch
                SET Status = 'Restored', RestoredBy = {restoredByUserId}, RestoredDate = SYSUTCDATETIME()
                WHERE DeletionBatchId = {deletionBatchId}", ct);

            await tx.CommitAsync(ct);
            return new RestoreBatchResult(true, restored);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error restoring deletion batch {Id}", deletionBatchId);
            return new RestoreBatchResult(false, 0, $"Unable to restore the batch: {ex.GetBaseException().Message}");
        }
    }

    private sealed class DeletedInvoiceCodeKey
    {
        public int SubDistributorId { get; set; }
        public string SalesInvoiceCode { get; set; } = string.Empty;
    }
    // ─── Recalculate amounts ─────────────────────────────────────────────────

    public async Task<RecalcResult> RunRecalcAsync(
        int subDistributorId, int year, int month, bool dryRun, int userId, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);

        var runIdParam = new SqlParameter("@RunId", SqlDbType.Int) { Direction = ParameterDirection.Output };

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                @"EXEC dbo.sp_RecalculateInvoiceAmounts
                      @SubDistributorId = @SubDistributorId,
                      @FromDate         = @FromDate,
                      @ToDate           = @ToDate,
                      @DryRun           = @DryRun,
                      @RunBy            = @RunBy,
                      @RunId            = @RunId OUTPUT",
                new object[]
                {
                    new SqlParameter("@SubDistributorId", SqlDbType.Int) { Value = subDistributorId },
                    new SqlParameter("@FromDate", SqlDbType.Date) { Value = from.ToDateTime(TimeOnly.MinValue) },
                    new SqlParameter("@ToDate",   SqlDbType.Date) { Value = to.ToDateTime(TimeOnly.MinValue) },
                    new SqlParameter("@DryRun",   SqlDbType.Bit)  { Value = dryRun },
                    new SqlParameter("@RunBy",    SqlDbType.Int)  { Value = userId },
                    runIdParam
                },
                ct);

            if (runIdParam.Value is not int runId)
                return RecalcResult.Failed("The recalculation did not return a run.");

            return await LoadRecalcRunAsync(context, runId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating amounts for {Subd} {Year}-{Month} (dry run: {DryRun})",
                subDistributorId, year, month, dryRun);
            return RecalcResult.Failed($"Unable to recalculate: {ex.GetBaseException().Message}");
        }
    }

    private static async Task<RecalcResult> LoadRecalcRunAsync(EntrielContext context, int runId, CancellationToken ct)
    {
        var run = await context.PriceRecalcRuns.AsNoTracking()
            .Where(r => r.RunId == runId)
            .Select(r => new RecalcRunSummary
            {
                RunId = r.RunId,
                SubDistributorId = r.SubDistributorId,
                SubdName = r.SubDistributor.SubdName,
                FromDate = r.FromDate,
                IsDryRun = r.IsDryRun,
                CreatedByName = context.Users
                    .Where(u => u.UserId == r.CreatedBy)
                    .Select(u => u.FullName ?? u.Username)
                    .FirstOrDefault(),
                CreatedAt = r.CreatedAt,
                AppliedAt = r.AppliedAt,
                UndoneAt = r.UndoneAt,
                Lines = r.Lines ?? 0,
                LinesChanged = r.LinesChanged ?? 0,
                LinesUnpriced = r.LinesUnpriced ?? 0,
                OldTotal = r.OldTotal ?? 0m,
                NewTotal = r.NewTotal ?? 0m
            })
            .FirstOrDefaultAsync(ct);

        if (run is null)
            return RecalcResult.Failed("Recalculation run not found.");

        var issues = await context.PriceRecalcLines.AsNoTracking()
            .Where(l => l.RunId == runId && l.Issue != null)
            .GroupBy(l => l.Issue!)
            .Select(g => new RecalcIssueRow { Issue = g.Key, Lines = g.Count(), Amount = g.Sum(x => x.OldAmount) })
            .OrderByDescending(x => x.Lines)
            .ToListAsync(ct);

        var changes = await (
            from l in context.PriceRecalcLines.AsNoTracking()
            join ci in context.CompanyItems.AsNoTracking() on l.CompanyItemId equals ci.CompanyItemId
            where l.RunId == runId && l.NewAmount != null && l.NewAmount != l.OldAmount
            group l by new { ci.ItemCode, ci.ItemName } into g
            select new RecalcItemChangeRow
            {
                ItemCode = g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                Lines = g.Count(),
                OldAmount = g.Sum(x => x.OldAmount),
                NewAmount = g.Sum(x => x.NewAmount ?? 0m)
            }).ToListAsync(ct);

        return new RecalcResult
        {
            IsSuccess = true,
            Run = run,
            Issues = issues,
            TopChanges = changes.OrderByDescending(c => Math.Abs(c.Difference)).Take(15).ToList()
        };
    }

    public async Task<List<RecalcRunSummary>> GetAppliedRecalcRunsAsync(
        int? subDistributorId, int take = 20, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var query = context.PriceRecalcRuns.AsNoTracking().Where(r => !r.IsDryRun);
        if (subDistributorId > 0)
            query = query.Where(r => r.SubDistributorId == subDistributorId);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => new RecalcRunSummary
            {
                RunId = r.RunId,
                SubDistributorId = r.SubDistributorId,
                SubdName = r.SubDistributor.SubdName,
                FromDate = r.FromDate,
                IsDryRun = r.IsDryRun,
                CreatedByName = context.Users
                    .Where(u => u.UserId == r.CreatedBy)
                    .Select(u => u.FullName ?? u.Username)
                    .FirstOrDefault(),
                CreatedAt = r.CreatedAt,
                AppliedAt = r.AppliedAt,
                UndoneAt = r.UndoneAt,
                Lines = r.Lines ?? 0,
                LinesChanged = r.LinesChanged ?? 0,
                LinesUnpriced = r.LinesUnpriced ?? 0,
                OldTotal = r.OldTotal ?? 0m,
                NewTotal = r.NewTotal ?? 0m
            })
            .ToListAsync(ct);
    }

    public async Task<RecalcUndoResult> UndoRecalcAsync(int runId, int userId, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var restored = new SqlParameter("@Restored", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var skipped = new SqlParameter("@Skipped", SqlDbType.Int) { Direction = ParameterDirection.Output };

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.sp_UndoInvoiceRecalc @RunId = @RunId, @RunBy = @RunBy, @Restored = @Restored OUTPUT, @Skipped = @Skipped OUTPUT",
                new object[]
                {
                    new SqlParameter("@RunId", SqlDbType.Int) { Value = runId },
                    new SqlParameter("@RunBy", SqlDbType.Int) { Value = userId },
                    restored,
                    skipped
                },
                ct);

            return new RecalcUndoResult(true,
                restored.Value is int r ? r : 0,
                skipped.Value is int s ? s : 0);
        }
        catch (SqlException ex) when (ex.Number == 50200)
        {
            return new RecalcUndoResult(false, 0, 0, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undoing recalculation run {RunId}", runId);
            return new RecalcUndoResult(false, 0, 0, $"Unable to undo: {ex.GetBaseException().Message}");
        }
    }

    public async Task<(List<RecalcInvoiceChangeRow> Items, int Total)> GetRecalcInvoiceChangesAsync(
    int runId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        // One row per invoice in the run, keeping only invoices that change or can't be priced
        var perInvoice = context.PriceRecalcLines.AsNoTracking()
            .Where(l => l.RunId == runId)
            .GroupBy(l => l.SalesInvoiceId)
            .Select(g => new
            {
                SalesInvoiceId = g.Key,
                Lines = g.Count(),
                LinesChanged = g.Sum(x => x.NewAmount != null && x.NewAmount != x.OldAmount ? 1 : 0),
                LinesUnpriced = g.Sum(x => x.NewAmount == null ? 1 : 0),
                OldTotal = g.Sum(x => x.OldAmount),
                NewTotal = g.Sum(x => x.NewAmount ?? x.OldAmount)
            })
            .Where(x => x.LinesChanged > 0 || x.LinesUnpriced > 0);

        var query =
            from x in perInvoice
            join si in context.SalesInvoices.AsNoTracking() on x.SalesInvoiceId equals si.SalesInvoiceId
            select new
            {
                x.SalesInvoiceId,
                si.SalesInvoiceCode,
                si.SalesInvoiceDate,
                si.Customer.CustomerName,
                si.Customer.CustomerCode,
                si.OrderType,
                x.Lines,
                x.LinesChanged,
                x.LinesUnpriced,
                x.OldTotal,
                x.NewTotal
            };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r =>
                r.SalesInvoiceCode.Contains(term) ||
                r.CustomerName.Contains(term) ||
                r.CustomerCode.Contains(term));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.NewTotal - r.OldTotal > 0 ? r.NewTotal - r.OldTotal : r.OldTotal - r.NewTotal)
            .ThenBy(r => r.SalesInvoiceDate)
            .ThenBy(r => r.SalesInvoiceCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RecalcInvoiceChangeRow
            {
                SalesInvoiceId = r.SalesInvoiceId,
                SalesInvoiceCode = r.SalesInvoiceCode,
                SalesInvoiceDate = r.SalesInvoiceDate,
                CustomerName = r.CustomerCode + " – " + r.CustomerName,
                OrderType = r.OrderType,
                Lines = r.Lines,
                LinesChanged = r.LinesChanged,
                LinesUnpriced = r.LinesUnpriced,
                OldTotal = r.OldTotal,
                NewTotal = r.NewTotal
            })
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<List<RecalcLineChangeRow>> GetRecalcInvoiceLinesAsync(
        int runId, int salesInvoiceId, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await (
            from l in context.PriceRecalcLines.AsNoTracking()
            join sii in context.SalesInvoiceItems.AsNoTracking() on l.SalesInvoiceItemId equals sii.SalesInvoiceItemId
            where l.RunId == runId && l.SalesInvoiceId == salesInvoiceId
            orderby sii.SubdItem.SubdItemCode
            select new RecalcLineChangeRow
            {
                SalesInvoiceItemId = l.SalesInvoiceItemId,
                ItemCode = sii.SubdItem.SubdItemCode,
                ItemName = sii.SubdItem.ItemName,
                UomName = sii.ItemsUom.UomName,
                Quantity = sii.Quantity,
                StockPrice = l.StockPrice,
                OldAmount = l.OldAmount,
                NewAmount = l.NewAmount,
                Issue = l.Issue
            })
            .ToListAsync(ct);
    }

    private sealed record PricedItem(int BaseQuantity, decimal StockPrice, int HistoryId, decimal Amount);

    private static (List<PricedItem>? Lines, string? Error) PriceItems(
        IReadOnlyList<(int SubdItemId, int ItemsUomId, int Quantity)> items,
        IReadOnlyDictionary<int, UomPriceInfo> prices,
        bool isCredit)
    {
        var lines = new List<PricedItem>(items.Count);

        foreach (var (subdItemId, uomId, qty) in items)
        {
            if (!prices.TryGetValue(uomId, out var p) || p.SubdItemId != subdItemId)
                return (null, "A selected unit of measure does not belong to its item or sub distributor.");

            if (!p.IsPriced)
                return (null, $"{p.SubdItemCode} ({p.UomName}): {p.Issue}. Fix the item mapping or company price first.");

            lines.Add(new PricedItem(
                p.BaseQuantityFor(qty),
                p.StockPrice!.Value,
                p.CompanyItemPriceHistoryId!.Value,
                p.AmountFor(qty, isCredit)));
        }

        return (lines, null);
    }

    public async Task<List<RecalcIssueItemRow>> GetRecalcIssueItemsAsync(
        int runId, string issue, CancellationToken ct = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        // Line-level rows for this issue (issue lists are small, so group in memory)
        var lines = await (
            from l in context.PriceRecalcLines.AsNoTracking()
            join sii in context.SalesInvoiceItems.AsNoTracking() on l.SalesInvoiceItemId equals sii.SalesInvoiceItemId
            join si in context.SalesInvoices.AsNoTracking() on l.SalesInvoiceId equals si.SalesInvoiceId
            where l.RunId == runId && l.Issue == issue
            select new
            {
                sii.SubdItemId,
                sii.SubdItem.SubdItemCode,
                sii.SubdItem.ItemName,
                CompanyItemCode = sii.SubdItem.CompanyItem.ItemCode,
                sii.ItemsUomId,
                sii.ItemsUom.UomName,
                sii.ItemsUom.ConversionToBase,
                UomActive = sii.ItemsUom.IsActive,
                l.SalesInvoiceId,
                si.SalesInvoiceCode,
                l.OldAmount
            })
            .ToListAsync(ct);

        if (lines.Count == 0)
            return new List<RecalcIssueItemRow>();

        // Current active UOMs of those items, to compare against the one the lines use
        var subdItemIds = lines.Select(x => x.SubdItemId).Distinct().ToList();
        var activeUoms = await context.ItemsUoms.AsNoTracking()
            .Where(u => subdItemIds.Contains(u.SubdItemId) && u.IsActive)
            .Select(u => new { u.SubdItemId, u.UomName, u.ConversionToBase, u.IsStockUom })
            .ToListAsync(ct);

        var activeBySubdItem = activeUoms
            .GroupBy(u => u.SubdItemId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.OrderBy(u => u.ConversionToBase)
                    .Select(u => $"{u.UomName} ({u.ConversionToBase}{(u.IsStockUom ? ", stock" : "")})")));

        return lines
            .GroupBy(x => new
            {
                x.SubdItemId,
                x.SubdItemCode,
                x.ItemName,
                x.CompanyItemCode,
                x.ItemsUomId,
                x.UomName,
                x.ConversionToBase,
                x.UomActive
            })
            .Select(g => new RecalcIssueItemRow
            {
                SubdItemCode = g.Key.SubdItemCode,
                ItemName = g.Key.ItemName,
                CompanyItemCode = g.Key.CompanyItemCode,
                UomName = g.Key.UomName,
                ConversionToBase = g.Key.ConversionToBase,
                UomActive = g.Key.UomActive,
                ActiveUoms = activeBySubdItem.TryGetValue(g.Key.SubdItemId, out var a) ? a : "(none)",
                Lines = g.Count(),
                Invoices = g.Select(x => x.SalesInvoiceId).Distinct().Count(),
                Amount = g.Sum(x => x.OldAmount),
                SampleInvoices = string.Join(", ", g.Select(x => x.SalesInvoiceCode).Distinct().OrderBy(c => c).Take(5))
                                 + (g.Select(x => x.SalesInvoiceId).Distinct().Count() > 5 ? ", …" : "")
            })
            .OrderByDescending(r => Math.Abs(r.Amount))
            .ThenBy(r => r.SubdItemCode)
            .ToList();
    }
    private static bool IsCredit(string orderType)
        => string.Equals(orderType?.Trim(), "Credit", StringComparison.OrdinalIgnoreCase);
}