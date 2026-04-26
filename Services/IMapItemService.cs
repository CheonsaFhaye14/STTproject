using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface IMapItemService
{
    Task<List<string>> GetMapItemPrincipalsAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<MapCompanyItemRow>> GetMapCompanyItemsAsync(int userId, int subDistributorId, string? principal, CancellationToken cancellationToken = default);
    Task<List<MapSubDistributorItemRow>> GetMapSubDistributorItemsAsync(int userId, int subDistributorId, string? principal, CancellationToken cancellationToken = default);
    Task<List<CompanyItemDropdownItem>> GetCompanyItemsForDropdownAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<string>> GetCompanyItemUomsAsync(int companyItemId, CancellationToken cancellationToken = default);
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
        CancellationToken cancellationToken = default)
    {
        var query = _context.CompanyItems
            .AsNoTracking()
            .Where(ci => ci.IsActive)
            .SelectMany(ci => ci.ItemsUoms.DefaultIfEmpty(), (ci, uom) => new MapCompanyItemRow
            {
                CompanyItemCode = ci.ItemCode,
                Description = ci.ItemName,
                Principal = ci.Principal,
                UomName = uom != null ? uom.UomName : string.Empty
            });

        if (!string.IsNullOrWhiteSpace(principal))
        {
            query = query.Where(item => item.Principal == principal);
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
                si.SubDistributorId,
                si.SubdItemCode,
                si.ItemName,
                si.Price,
                Principal = si.CompanyItem.Principal,
                UomName = si.ItemsUom != null ? si.ItemsUom.UomName : string.Empty
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
                SubDistributorId = group.First().SubDistributorId,
                SubItemCode = group.Key.SubdItemCode,
                Description = group.Key.ItemName,
                Price = group.First().Price,
                Principal = group.First().Principal,
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
    public string UomName { get; set; } = string.Empty;
}

public sealed class MapSubDistributorItemRow
{
    public int SubDistributorId { get; set; }
    public string SubItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Principal { get; set; } = string.Empty;
    public string UomName { get; set; } = string.Empty;
}