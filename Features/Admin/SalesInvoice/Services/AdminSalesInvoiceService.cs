using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.SalesInvoice.DTOs;

namespace STTproject.Features.Admin.SalesInvoice.Services;

// ─── Interface ───────────────────────────────────────────────────────────────

public interface IAdminSalesInvoiceService
{
    // View
    Task<List<SalesInvoiceListRow>> GetSalesInvoicesAsync(int subDistributorId, CancellationToken cancellationToken = default);
    Task<SalesInvoiceDetailDto?> GetSalesInvoiceDetailAsync(int salesInvoiceId, CancellationToken cancellationToken = default);
    Task<bool> InvoiceCodeExistsAsync(string code, int subDistributorId, int? excludeId = null, CancellationToken cancellationToken = default);

    // Dropdowns
    Task<List<SalesInvoiceCustomerDropdownItem>> GetCustomersForDropdownAsync(int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<SalesInvoiceSubdItemDropdownItem>> GetSubdItemsForDropdownAsync(int subDistributorId, CancellationToken cancellationToken = default);

    // CRUD
    Task<SalesInvoiceResult> CreateSalesInvoiceAsync(CreateSalesInvoiceDto dto, int createdByUserId, CancellationToken cancellationToken = default);
    Task<SalesInvoiceResult> UpdateSalesInvoiceAsync(UpdateSalesInvoiceDto dto, int updatedByUserId, CancellationToken cancellationToken = default);
    Task<DeleteSalesInvoiceResult> DeleteSalesInvoiceAsync(int salesInvoiceId, CancellationToken cancellationToken = default);

    // User
    Task<string?> GetUserNameByIdAsync(int? userId);
}

// ─── Implementation ──────────────────────────────────────────────────────────

public class AdminSalesInvoiceService : IAdminSalesInvoiceService
{
    private readonly IDbContextFactory<SttprojectContext> _contextFactory;
    private readonly ILogger<AdminSalesInvoiceService> _logger;

    public AdminSalesInvoiceService(
        IDbContextFactory<SttprojectContext> contextFactory,
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
                SalesInvoiceId   = si.SalesInvoiceId,
                SalesInvoiceCode = si.SalesInvoiceCode,
                SalesInvoiceDate = si.SalesInvoiceDate,
                CustomerName     = si.Customer.CustomerName,
                CustomerCode     = si.Customer.CustomerCode,
                SubdName         = si.SubDistributor.SubdName,
                OrderType        = si.OrderType,
                SalesMan         = si.SalesMan,
                TotalAmount      = si.SalesInvoiceItems.Sum(item => item.Amount),
                TotalItems       = si.SalesInvoiceItems.Count,
                CreatedDate      = si.CreatedDate,
                UpdatedDate      = si.UpdatedDate,
                CreatedByName    = si.CreatedByNavigation != null
                                     ? (si.CreatedByNavigation.FullName ?? si.CreatedByNavigation.Username)
                                     : null,
                UpdatedByName    = si.UpdatedByNavigation != null
                                     ? (si.UpdatedByNavigation.FullName ?? si.UpdatedByNavigation.Username)
                                     : null,
            })
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
                SalesInvoiceId   = si.SalesInvoiceId,
                SalesInvoiceCode = si.SalesInvoiceCode,
                SalesInvoiceDate = si.SalesInvoiceDate,
                CustomerId       = si.CustomerId,
                CustomerName     = si.Customer.CustomerName,
                CustomerCode     = si.Customer.CustomerCode,
                SubDistributorId = si.SubDistributorId,
                SubdName         = si.SubDistributor.SubdName,
                OrderType        = si.OrderType,
                SalesMan         = si.SalesMan,
                CreatedDate      = si.CreatedDate,
                UpdatedDate      = si.UpdatedDate,
                CreatedByName    = si.CreatedByNavigation != null
                                     ? (si.CreatedByNavigation.FullName ?? si.CreatedByNavigation.Username)
                                     : null,
                UpdatedByName    = si.UpdatedByNavigation != null
                                     ? (si.UpdatedByNavigation.FullName ?? si.UpdatedByNavigation.Username)
                                     : null,
                Items = si.SalesInvoiceItems
                    .Select(item => new SalesInvoiceItemDto
                    {
                        SalesInvoiceItemId = item.SalesInvoiceItemId,
                        SubdItemId         = item.SubdItemId,
                        SubdItemCode       = item.SubdItem.SubdItemCode,
                        ItemName           = item.SubdItem.ItemName,
                        ItemsUomId         = item.ItemsUomId,
                        UomName            = item.ItemsUom.UomName,
                        UomPrice           = item.ItemsUom.Price,
                        Quantity           = item.Quantity,
                        Amount             = item.Amount
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
                CustomerId   = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SalesInvoiceSubdItemDropdownItem>> GetSubdItemsForDropdownAsync(
        int subDistributorId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.SubdItems
            .AsNoTracking()
            .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
            .OrderBy(si => si.SubdItemCode)
            .Select(si => new SalesInvoiceSubdItemDropdownItem
            {
                SubdItemId   = si.SubdItemId,
                SubdItemCode = si.SubdItemCode,
                ItemName     = si.ItemName,
                Uoms         = context.ItemsUoms
                    .Where(u => u.SubdItemId == si.SubdItemId)
                    .Select(u => new SalesInvoiceUomOption
                    {
                        ItemsUomId       = u.ItemsUomId,
                        UomName          = u.UomName,
                        Price            = u.Price,
                        ConversionToBase = u.ConversionToBase
                    })
                    .OrderBy(u => u.UomName)
                    .ToList()
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
                SalesInvoiceCode  = dto.SalesInvoiceCode.Trim(),
                SalesInvoiceDate  = dto.SalesInvoiceDate,
                CustomerId        = dto.CustomerId,
                SubDistributorId  = dto.SubDistributorId,
                OrderType         = dto.OrderType.Trim(),
                SalesMan          = dto.SalesMan?.Trim(),
                CreatedDate       = DateTime.UtcNow,
                CreatedBy         = createdByUserId,
                SalesInvoiceItems = dto.Items.Select(i => new SalesInvoiceItem
                {
                    SubdItemId  = i.SubdItemId,
                    ItemsUomId  = i.ItemsUomId,
                    Quantity    = i.Quantity,
                    Amount      = i.Amount,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy   = createdByUserId
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
            existing.CustomerId       = dto.CustomerId;
            existing.SubDistributorId = dto.SubDistributorId;
            existing.OrderType        = dto.OrderType.Trim();
            existing.SalesMan         = dto.SalesMan?.Trim();
            existing.UpdatedDate      = DateTime.UtcNow;
            existing.UpdatedBy        = updatedByUserId;

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
                        existingItem.SubdItemId  = itemDto.SubdItemId;
                        existingItem.ItemsUomId  = itemDto.ItemsUomId;
                        existingItem.Quantity    = itemDto.Quantity;
                        existingItem.Amount      = itemDto.Amount;
                        existingItem.UpdatedDate = DateTime.UtcNow;
                        existingItem.UpdatedBy   = updatedByUserId;
                    }
                }
                else
                {
                    existing.SalesInvoiceItems.Add(new SalesInvoiceItem
                    {
                        SubdItemId  = itemDto.SubdItemId,
                        ItemsUomId  = itemDto.ItemsUomId,
                        Quantity    = itemDto.Quantity,
                        Amount      = itemDto.Amount,
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy   = updatedByUserId
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

    // ─── Delete ──────────────────────────────────────────────────────────────

    public async Task<DeleteSalesInvoiceResult> DeleteSalesInvoiceAsync(
        int salesInvoiceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var existing = await context.SalesInvoices
                .Include(si => si.SalesInvoiceItems)
                .FirstOrDefaultAsync(si => si.SalesInvoiceId == salesInvoiceId, cancellationToken);

            if (existing is null)
                return DeleteSalesInvoiceResult.NotFound();

            if (existing.SalesInvoiceItems.Any())
            {
                context.SalesInvoiceItems.RemoveRange(existing.SalesInvoiceItems);
                await context.SaveChangesAsync(cancellationToken);
            }

            context.SalesInvoices.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return DeleteSalesInvoiceResult.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            var msg = ex.GetBaseException()?.Message ?? ex.Message;
            _logger.LogError(ex, "Error deleting SalesInvoice {Id}: {Message}", salesInvoiceId, msg);
            return DeleteSalesInvoiceResult.Failed($"Unable to delete the sales invoice: {msg}");
        }
    }
}