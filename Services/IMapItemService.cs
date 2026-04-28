using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface IMapItemService
{
    Task<List<string>> GetMapItemPrincipalsAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<MapCompanyItemRow>> GetMapCompanyItemsAsync(int userId, int subDistributorId, string? principal, CompanyItemFilterMode filterMode = CompanyItemFilterMode.All, CancellationToken cancellationToken = default);
    Task<List<MapSubDistributorItemRow>> GetMapSubDistributorItemsAsync(int userId, int subDistributorId, string? principal, CancellationToken cancellationToken = default);
    Task<List<CompanyItemDropdownItem>> GetCompanyItemsForDropdownAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<string>> GetCompanyItemUomsAsync(int companyItemId, CancellationToken cancellationToken = default);
    Task<bool> AddSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default);
    Task<UpdateSubdItemResult> UpdateSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default);
    Task<DeleteSubdItemResult> DeleteSubdItemAsync(int subdItemId, CancellationToken cancellationToken = default);
    Task<bool> SubdItemCodeExistsAsync(int subDistributorId, string subdItemCode, int? excludeSubdItemId = null, CancellationToken cancellationToken = default);
}

public enum CompanyItemFilterMode
{
    All,
    Unmapped,
    Mapped
}

public class MapItemService : IMapItemService
{
    private readonly SttprojectContext _context;

    public MapItemService(SttprojectContext context)
    {
        _context = context;
    }

    public async Task<List<string>> GetMapItemPrincipalsAsync(
        int userId,
        int subDistributorId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CompanyItems
            .AsNoTracking()
            .Where(ci => ci.IsActive)
            .Select(ci => ci.Principal)
            .Where(principal => !string.IsNullOrWhiteSpace(principal))
            .Distinct()
            .OrderBy(principal => principal)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<MapCompanyItemRow>> GetMapCompanyItemsAsync(
        int userId,
        int subDistributorId,
        string? principal,
        CompanyItemFilterMode filterMode = CompanyItemFilterMode.All,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CompanyItems
            .AsNoTracking()
            .Where(ci => ci.IsActive)
            .Select(ci => new MapCompanyItemRow
            {
                CompanyItemCode = ci.ItemCode,
                Description = ci.ItemName,
                Price = ci.ItemsUom != null ? ci.ItemsUom.Price : 0m,
                Principal = ci.Principal,
                UomName = ci.ItemsUom != null ? ci.ItemsUom.UomName : string.Empty,
                CompanyItemId = ci.CompanyItemId,
            });

        if (!string.IsNullOrWhiteSpace(principal))
        {
            query = query.Where(item => item.Principal == principal);
        }

        if (subDistributorId > 0 && filterMode != CompanyItemFilterMode.All)
        {
            var mappedCompanyItemIds = _context.SubdItems
                .AsNoTracking()
                .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
                .Select(si => si.CompanyItemId);

            query = filterMode == CompanyItemFilterMode.Mapped
                ? query.Where(item => mappedCompanyItemIds.Contains(item.CompanyItemId))
                : query.Where(item => !mappedCompanyItemIds.Contains(item.CompanyItemId));
        }

        return await query
            .OrderBy(item => item.CompanyItemCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<MapSubDistributorItemRow>> GetMapSubDistributorItemsAsync(
        int userId,
        int subDistributorId,
        string? principal,
        CancellationToken cancellationToken = default)
    {
        // Get all sub-distributor items with their UOM and price
        var query = _context.SubdItems
            .AsNoTracking()
            .Where(si => si.IsActive)
            .Where(si => si.SubDistributor.EncoderId == userId)
            .Where(si => si.SubDistributor.IsActive)
            .Select(si => new
            {
                si.SubdItemId,
                si.SubDistributorId,
                si.SubdItemCode,
                si.ItemName,
                si.CompanyItemId,
                CompanyItemName = si.CompanyItem.ItemName,
                Price = si.CompanyItem.ItemsUom != null ? si.CompanyItem.ItemsUom.Price : 0m,
                Principal = si.CompanyItem.Principal,
                UomName = si.CompanyItem.ItemsUom != null ? si.CompanyItem.ItemsUom.UomName : string.Empty
            });

        if (subDistributorId > 0)
        {
            query = query.Where(item => item.SubDistributorId == subDistributorId);
        }

        if (!string.IsNullOrWhiteSpace(principal))
        {
            query = query.Where(item => item.Principal == principal);
        }

        var results = await query
            .OrderBy(item => item.SubdItemCode)
            .ThenBy(item => item.UomName)
            .ToListAsync(cancellationToken);

        // Group by SubItemCode and ItemName - show each UOM with its price
        return results
            .GroupBy(x => new { x.SubdItemCode, x.ItemName })
            .Select(group => new MapSubDistributorItemRow
            {
                SubdItemId = group.First().SubdItemId,
                SubDistributorId = group.First().SubDistributorId,
                SubItemCode = group.Key.SubdItemCode,
                Description = group.Key.ItemName,
                Price = group.First().Price,
                Principal = group.First().Principal,
                CompanyItemId = group.First().CompanyItemId,
                CompanyItemName = group.First().CompanyItemName,
                // Format: "Box of 12 - 120.00, Piece - 10.00"
                UomName = string.Join(", ", group
                    .Where(x => !string.IsNullOrWhiteSpace(x.UomName))
                    .Select(x => $"{x.UomName} - {x.Price:N2}"))
            })
            .OrderBy(x => x.SubItemCode)
            .ToList();
    }

    public async Task<List<CompanyItemDropdownItem>> GetCompanyItemsForDropdownAsync(
        int userId,
        int subDistributorId,
        CancellationToken cancellationToken = default)
    {
        // Get all company items that are NOT already connected to the selected sub-distributor
        var connectedCompanyItemIds = await _context.SubdItems
            .AsNoTracking()
            .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
            .Select(si => si.CompanyItemId)
            .ToListAsync(cancellationToken);

        return await _context.CompanyItems
            .AsNoTracking()
            .Where(ci => ci.IsActive)
            .Where(ci => !connectedCompanyItemIds.Contains(ci.CompanyItemId))
            .OrderBy(ci => ci.ItemName)
            .Select(ci => new CompanyItemDropdownItem
            {
                CompanyItemId = ci.CompanyItemId,
                ItemCode = ci.ItemCode,
                ItemName = ci.ItemName,
                Principal = ci.Principal
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetCompanyItemUomsAsync(
        int companyItemId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ItemsUoms
            .AsNoTracking()
            .Where(u => u.CompanyItemId == companyItemId)
            .OrderBy(u => u.UomName)
            .Select(u => u.UomName)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AddSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.SubdItems.Add(item);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<UpdateSubdItemResult> UpdateSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.SubdItems
                .FirstOrDefaultAsync(si => si.SubdItemId == item.SubdItemId, cancellationToken);

            if (existing is null)
            {
                return UpdateSubdItemResult.NotFound();
            }

            var inUseByInvoices = await _context.SalesInvoiceItems
                .AsNoTracking()
                .AnyAsync(sii => sii.SubdItemId == item.SubdItemId, cancellationToken);

            if (inUseByInvoices)
            {
                return UpdateSubdItemResult.InUse("This sub distributor item cannot be updated because it is already used by one or more invoices.");
            }

            existing.SubdItemCode = item.SubdItemCode;
            existing.ItemName = item.ItemName;
            existing.CompanyItemId = item.CompanyItemId;
            existing.UpdatedBy = item.UpdatedBy;
            existing.UpdatedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return UpdateSubdItemResult.Success();
        }
        catch
        {
            return UpdateSubdItemResult.Failed("Unable to update the sub distributor item.");
        }
    }

    public async Task<DeleteSubdItemResult> DeleteSubdItemAsync(int subdItemId, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _context.SubdItems
                .FirstOrDefaultAsync(si => si.SubdItemId == subdItemId, cancellationToken);

            if (existing is null)
            {
                return DeleteSubdItemResult.NotFound();
            }

            var inUseByInvoices = await _context.SalesInvoiceItems
                .AsNoTracking()
                .AnyAsync(sii => sii.SubdItemId == subdItemId, cancellationToken);

            if (inUseByInvoices)
            {
                return DeleteSubdItemResult.InUse("This sub distributor item cannot be deleted because it is already used by one or more invoices.");
            }

            _context.SubdItems.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
            return DeleteSubdItemResult.Success();
        }
        catch
        {
            return DeleteSubdItemResult.Failed("Unable to delete the sub distributor item.");
        }
    }

    public async Task<bool> SubdItemCodeExistsAsync(
        int subDistributorId,
        string subdItemCode,
        int? excludeSubdItemId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = subdItemCode.Trim();

        return await _context.SubdItems
            .AsNoTracking()
            .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
            .Where(si => si.SubdItemCode == normalizedCode)
            .Where(si => !excludeSubdItemId.HasValue || si.SubdItemId != excludeSubdItemId.Value)
            .AnyAsync(cancellationToken);
    }
}

public sealed class CompanyItemDropdownItem
{
    public int CompanyItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
}

public sealed class MapCompanyItemRow
{
    public string CompanyItemCode { get; set; } = string.Empty;
    public string SubItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Principal { get; set; } = string.Empty;
    public int CompanyItemId { get; set; }
    public string UomName { get; set; } = string.Empty;
}

public sealed class MapSubDistributorItemRow
{
    public int SubdItemId { get; set; }
    public int SubDistributorId { get; set; }
    public string SubItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Principal { get; set; } = string.Empty;
    public int CompanyItemId { get; set; }
    public string CompanyItemName { get; set; } = string.Empty;
    public string UomName { get; set; } = string.Empty;
}

public sealed class DeleteSubdItemResult
{
    public bool IsDeleted { get; init; }
    public string? ErrorMessage { get; init; }

    public static DeleteSubdItemResult Success() => new() { IsDeleted = true };
    public static DeleteSubdItemResult NotFound() => new() { IsDeleted = false, ErrorMessage = "The selected sub distributor item was not found." };
    public static DeleteSubdItemResult InUse(string message) => new() { IsDeleted = false, ErrorMessage = message };
    public static DeleteSubdItemResult Failed(string message) => new() { IsDeleted = false, ErrorMessage = message };
}

public sealed class UpdateSubdItemResult
{
    public bool IsUpdated { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateSubdItemResult Success() => new() { IsUpdated = true };
    public static UpdateSubdItemResult NotFound() => new() { IsUpdated = false, ErrorMessage = "The selected sub distributor item was not found." };
    public static UpdateSubdItemResult InUse(string message) => new() { IsUpdated = false, ErrorMessage = message };
    public static UpdateSubdItemResult Failed(string message) => new() { IsUpdated = false, ErrorMessage = message };
}