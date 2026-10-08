using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.SalesInvoice.DTOs;
//TODO: Allow to select Items Uom with out price or conversion. 
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
                        UomPrice = context.ItemsUomPriceHistories
                                .Where(h =>
                                    h.ItemsUomId == item.ItemsUomId &&
                                    h.AppliedDate != null &&
                                    h.AppliedDate <= si.SalesInvoiceDate.ToDateTime(TimeOnly.MaxValue))
                                .OrderByDescending(h => h.AppliedDate)
                                .Select(h => (decimal?)h.NewPrice)
                                .FirstOrDefault()
                            ?? context.ItemsUomPriceHistories
                                .Where(h => h.ItemsUomId == item.ItemsUomId && h.AppliedDate != null)
                                .OrderBy(h => h.AppliedDate)
                                .Select(h => (decimal?)h.OldPrice)
                                .FirstOrDefault()
                            ?? item.ItemsUom.Price ?? 0m,
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
            .Where(c => c.IsActive)
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

    var invoiceDate = salesInvoiceDate.ToDateTime(TimeOnly.MinValue);

    return await context.SubdItems
        .AsNoTracking()
        .Where(si =>
            si.SubDistributorId == subDistributorId &&
            si.IsActive)
        .OrderBy(si => si.SubdItemCode)
        .Select(si => new SalesInvoiceSubdItemDropdownItem
        {
            SubdItemId = si.SubdItemId,
            SubdItemCode = si.SubdItemCode,
            ItemName = si.ItemName,

            Uoms = context.ItemsUoms
                .Where(u =>
                    u.SubdItemId == si.SubdItemId &&
                    u.IsActive)
                .Select(u => new SalesInvoiceUomOption
                {
                    ItemsUomId = u.ItemsUomId,
                    UomName = u.UomName,

                    Price = context.ItemsUomPriceHistories
                            .Where(h =>
                                h.ItemsUomId == u.ItemsUomId &&
                                h.AppliedDate != null &&
                                h.AppliedDate <= invoiceDate)
                            .OrderByDescending(h => h.AppliedDate)
                            .ThenByDescending(h => h.ItemsUomPriceHistoryId)
                            .Select(h => (decimal?)h.NewPrice)
                            .FirstOrDefault()
                        ?? context.ItemsUomPriceHistories
                            .Where(h => h.ItemsUomId == u.ItemsUomId && h.AppliedDate != null)
                            .OrderBy(h => h.AppliedDate)
                            .Select(h => (decimal?)h.OldPrice)
                            .FirstOrDefault()
                        ?? u.Price ?? 0m,

                    ConversionToBase = u.ConversionToBase ?? 1
                })
                .OrderBy(u => u.UomName)
                .ToList()
        })
        .ToListAsync(cancellationToken);
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
                SalesInvoiceItems = dto.Items.Select(i => new SalesInvoiceItem
                {
                    SubdItemId = i.SubdItemId,
                    ItemsUomId = i.ItemsUomId,
                    Quantity = i.Quantity,
                    Amount = i.Amount,
                    CreatedDate = DateTime.UtcNow,
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

            existing.SalesInvoiceCode = dto.SalesInvoiceCode.Trim();
            existing.SalesInvoiceDate = dto.SalesInvoiceDate;
            existing.CustomerId = dto.CustomerId;
            existing.SubDistributorId = dto.SubDistributorId;
            existing.OrderType = dto.OrderType.Trim();
            existing.SalesMan = dto.SalesMan?.Trim();
            existing.UpdatedDate = DateTime.UtcNow;
            existing.UpdatedBy = updatedByUserId;

            // Reconcile items: delete removed, update existing, add new
            var incomingIds = dto.Items
                .Where(i => i.SalesInvoiceItemId > 0)
                .Select(i => i.SalesInvoiceItemId)
                .ToHashSet();

            var toDelete = existing.SalesInvoiceItems
                .Where(i => !incomingIds.Contains(i.SalesInvoiceItemId))
                .ToList();

            context.SalesInvoiceItems.RemoveRange(toDelete);

            foreach (var itemDto in dto.Items)
            {
                if (itemDto.SalesInvoiceItemId > 0)
                {
                    var existingItem = existing.SalesInvoiceItems
                        .FirstOrDefault(i => i.SalesInvoiceItemId == itemDto.SalesInvoiceItemId);

                    if (existingItem is not null)
                    {
                        existingItem.SubdItemId = itemDto.SubdItemId;
                        existingItem.ItemsUomId = itemDto.ItemsUomId;
                        existingItem.Quantity = itemDto.Quantity;
                        existingItem.Amount = itemDto.Amount;
                        existingItem.UpdatedDate = DateTime.UtcNow;
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
                        Amount = itemDto.Amount,
                        CreatedDate = DateTime.UtcNow,
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

    public async Task<BatchDeleteResult> DeleteSelectedAsync(
        IReadOnlyCollection<int> invoiceIds, int deletedByUserId, CancellationToken ct = default)
    {
        if (invoiceIds.Count == 0)
            return new BatchDeleteResult(true, 0);

        await using var context = _contextFactory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var deleted = await ArchiveAndDeleteAsync(context, new ArchiveRequest(
                DeletionType: "Selected",
                SubDistributorId: null,   // selection may span several sub-distributors
                IdsCsv: string.Join(",", invoiceIds),
                DeletedByUserId: deletedByUserId), ct);

            await tx.CommitAsync(ct);
            return new BatchDeleteResult(true, deleted);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error deleting selected sales invoices");
            return new BatchDeleteResult(false, 0,
                $"Unable to delete the selected invoices: {ex.GetBaseException().Message}");
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
                 CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
            SELECT d.DeletedSalesInvoiceId, sii.SalesInvoiceItemId, sii.SubdItemId, sii.ItemsUomId,
                   sii.Quantity, sii.Amount, sii.CreatedDate, sii.CreatedBy, sii.UpdatedDate, sii.UpdatedBy
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

    public async Task<(List<DeletedBatchRow> Items, int Total)> GetDeletedBatchesPagedAsync(
        int page, int pageSize, string? type, string? status, int? subDistributorId,
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

        var total = await query.CountAsync(ct);

        var raw = await query
            .OrderByDescending(b => b.DeletedDate)
            .ThenByDescending(b => b.DeletionBatchId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Resolve names in memory (no guessing table names in raw SQL)
        var subdNames = await context.SubDistributors.AsNoTracking()
            .ToDictionaryAsync(s => s.SubDistributorId, s => s.SubdName, ct);

        var userIds = raw.Select(b => (int?)b.DeletedBy)
            .Concat(raw.Select(b => b.RestoredBy))
            .Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();

        var userNames = new Dictionary<int, string?>();
        foreach (var id in userIds)
            userNames[id] = await GetUserNameByIdAsync(id);

        string? Name(int? id) => id.HasValue && userNames.TryGetValue(id.Value, out var n) ? n : null;

        var items = raw.Select(b => new DeletedBatchRow
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
        }).ToList();

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
                    CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
                SELECT i.OriginalSalesInvoiceItemId, d.OriginalSalesInvoiceId, i.SubdItemId, i.ItemsUomId,
                    i.Quantity, i.Amount, i.CreatedDate, i.CreatedBy, i.UpdatedDate, i.UpdatedBy
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
}