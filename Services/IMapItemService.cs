using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using STTproject.Models;

namespace STTproject.Services;

public interface IMapItemService
{
    Task<List<string>> GetMapItemPrincipalsAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<MapCompanyItemRow>> GetMapCompanyItemsAsync(int userId, int subDistributorId, string? principal, CompanyItemFilterMode filterMode = CompanyItemFilterMode.All, CancellationToken cancellationToken = default);
    Task<List<MapSubDistributorItemRow>> GetMapSubDistributorItemsAsync(int userId, int subDistributorId, string? principal, CancellationToken cancellationToken = default);
    Task<List<CompanyItemDropdownItem>> GetCompanyItemsForDropdownAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<string>> GetCompanyItemUomsAsync(int companyItemId, CancellationToken cancellationToken = default);
    Task<ItemsUom?> GetSubdItemUomAsync(int subdItemId, CancellationToken cancellationToken = default);
    Task<List<ItemsUom>> GetSubdItemUomsAsync(int subdItemId, CancellationToken cancellationToken = default);
    Task<bool> AddSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default);
    Task<UpdateSubdItemResult> UpdateSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default);
    Task<DeleteSubdItemResult> DeleteSubdItemAsync(int subdItemId, CancellationToken cancellationToken = default);
    Task<bool> SubdItemCodeExistsAsync(int subDistributorId, string subdItemCode, int? excludeSubdItemId = null, CancellationToken cancellationToken = default);
    Task<bool> SaveSubdItemUomPricesAsync(int subdItemId, Dictionary<string, UomEntry> uomEntries, CancellationToken cancellationToken = default);
    Task<List<TemplateRow>> GetTemplateDataAsync(int subDistributorId, string? principal, CancellationToken cancellationToken = default);
}

public enum CompanyItemFilterMode
{
    All,
    Unmapped,
    Mapped
}

public class MapItemService : IMapItemService
{
    private readonly IDbContextFactory<SttprojectContext> _contextFactory;

    public MapItemService(IDbContextFactory<SttprojectContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<string>> GetMapItemPrincipalsAsync(
        int userId,
        int subDistributorId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.CompanyItems
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
        await using var context = _contextFactory.CreateDbContext();

        var query = context.CompanyItems
            .AsNoTracking()
            .Where(ci => ci.IsActive)
            .Select(ci => new MapCompanyItemRow
            {
                CompanyItemCode = ci.ItemCode,
                Description = ci.ItemName,
                Principal = ci.Principal,
                CompanyItemId = ci.CompanyItemId,
            });

        if (!string.IsNullOrWhiteSpace(principal))
        {
            query = query.Where(item => item.Principal == principal);
        }

        if (subDistributorId > 0 && filterMode != CompanyItemFilterMode.All)
        {
            var mappedCompanyItemIds = context.SubdItems
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
        await using var context = _contextFactory.CreateDbContext();

        // Get all sub-distributor items with each UOM as a separate row so grouping can show all UOMs
        var query = context.SubdItems
            .AsNoTracking()
            .Where(si => si.IsActive)
            .Where(si => si.SubDistributor.EncoderId == userId)
            .Where(si => si.SubDistributor.IsActive)
            .SelectMany(si => si.ItemsUoms.DefaultIfEmpty(), (si, u) => new
            {
                si.SubdItemId,
                si.SubDistributorId,
                si.SubdItemCode,
                si.ItemName,
                si.CompanyItemId,
                CompanyItemName = si.CompanyItem.ItemName,
                Price = u != null ? u.Price : 0m,
                Principal = si.CompanyItem.Principal,
                UomName = u != null ? u.UomName : string.Empty
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
        await using var context = _contextFactory.CreateDbContext();

        try
        {
            // First, materialize the list of already-connected company item IDs
            var connectedIds = await context.SubdItems
                .AsNoTracking()
                .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
                .Select(si => si.CompanyItemId)
                .ToListAsync(cancellationToken);

            // Ensure the first query is fully complete before starting the second
            if (cancellationToken.IsCancellationRequested)
                return new();

            // Then query company items not in that list (this is now LINQ to Objects, not DbContext)
            var result = await context.CompanyItems
                .AsNoTracking()
                .Where(ci => ci.IsActive)
                .Where(ci => !connectedIds.Contains(ci.CompanyItemId))
                .OrderBy(ci => ci.ItemName)
                .Select(ci => new CompanyItemDropdownItem
                {
                    CompanyItemId = ci.CompanyItemId,
                    ItemCode = ci.ItemCode,
                    ItemName = ci.ItemName,
                    Principal = ci.Principal
                })
                .ToListAsync(cancellationToken);

            return result;
        }
        catch (OperationCanceledException)
        {
            return new();
        }
    }

    public async Task<List<string>> GetCompanyItemUomsAsync(
        int companyItemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var query = context.ItemsUoms
            .AsNoTracking()
            .Where(u => !string.IsNullOrWhiteSpace(u.UomName))
            .Select(u => new { u.UomName, CompanyItemId = u.SubdItem.CompanyItemId });

        if (companyItemId > 0)
        {
            query = query.Where(x => x.CompanyItemId == companyItemId);
        }

        return await query
            .Select(x => x.UomName)
            .Distinct()
            .OrderBy(u => u)
            .ToListAsync(cancellationToken);
    }

    public async Task<ItemsUom?> GetSubdItemUomAsync(int subdItemId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        // Return the primary/base UOM if available
        return await context.ItemsUoms
            .AsNoTracking()
            .Where(u => u.SubdItemId == subdItemId)
            .OrderByDescending(u => u.IsBaseUnit)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ItemsUom>> GetSubdItemUomsAsync(int subdItemId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.ItemsUoms
            .AsNoTracking()
            .Where(u => u.SubdItemId == subdItemId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AddSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        try
        {
            context.SubdItems.Add(item);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }



    public async Task<UpdateSubdItemResult> UpdateSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        try
        {
            var existing = await context.SubdItems
                .Include(si => si.ItemsUoms)
                .FirstOrDefaultAsync(si => si.SubdItemId == item.SubdItemId, cancellationToken);

            if (existing is null)
            {
                return UpdateSubdItemResult.NotFound();
            }

            var inUseByInvoices = await context.SalesInvoiceItems
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

            await context.SaveChangesAsync(cancellationToken);
            return UpdateSubdItemResult.Success();
        }
        catch
        {
            return UpdateSubdItemResult.Failed("Unable to update the sub distributor item.");
        }
    }

    public async Task<bool> SaveSubdItemUomPricesAsync(int subdItemId, Dictionary<string, UomEntry> uomEntries, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        try
        {
            if (uomEntries == null) return false;

            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Load existing UOMs for the subd item
                var existingUoms = await context.ItemsUoms
                    .Where(u => u.SubdItemId == subdItemId)
                    .ToListAsync(cancellationToken);

                // Determine which names to keep
                var incomingNames = uomEntries.Keys.Select(k => k.Trim()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();

                // Update or insert incoming entries
                foreach (var kv in uomEntries)
                {
                    var name = kv.Key.Trim();
                    var entry = kv.Value;

                    var existing = existingUoms.FirstOrDefault(e => string.Equals(e.UomName, name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.ConversionToBase = entry.Conversion;
                        existing.Price = entry.Price ?? 0m;
                        existing.IsBaseUnit = string.Equals(name, "Piece", StringComparison.OrdinalIgnoreCase);
                        existing.UpdatedBy = entry.IsAutoCalculated ? null : entry.IsAutoCalculated == false ? existing.UpdatedBy : existing.UpdatedBy;
                        existing.UpdatedDate = DateTime.UtcNow;
                        context.ItemsUoms.Update(existing);
                    }
                    else
                    {
                        var created = new ItemsUom
                        {
                            UomName = name,
                            ConversionToBase = entry.Conversion,
                            Price = entry.Price ?? 0m,
                            IsBaseUnit = string.Equals(name, "Piece", StringComparison.OrdinalIgnoreCase),
                            SubdItemId = subdItemId,
                            CreatedDate = DateTime.UtcNow,
                            UpdatedDate = DateTime.UtcNow,
                            CreatedBy = null,
                            UpdatedBy = null
                        };
                        context.ItemsUoms.Add(created);
                    }
                }

                // Remove any existing uom not present in incoming list
                var toRemove = existingUoms.Where(e => !incomingNames.Any(n => string.Equals(n, e.UomName, StringComparison.OrdinalIgnoreCase))).ToList();
                if (toRemove.Any())
                {
                    context.ItemsUoms.RemoveRange(toRemove);
                }

                await context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<DeleteSubdItemResult> DeleteSubdItemAsync(int subdItemId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        try
        {
            var existing = await context.SubdItems
                .Include(si => si.ItemsUoms)
                .FirstOrDefaultAsync(si => si.SubdItemId == subdItemId, cancellationToken);

            if (existing is null)
            {
                return DeleteSubdItemResult.NotFound();
            }

            var inUseByInvoices = await context.SalesInvoiceItems
                .AsNoTracking()
                .AnyAsync(sii => sii.SubdItemId == subdItemId, cancellationToken);

            if (inUseByInvoices)
            {
                return DeleteSubdItemResult.InUse("This sub distributor item cannot be deleted because it is already used by one or more invoices.");
            }

            if (existing.ItemsUoms is not null && existing.ItemsUoms.Any())
            {
                context.ItemsUoms.RemoveRange(existing.ItemsUoms);
            }

            context.SubdItems.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
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
        await using var context = _contextFactory.CreateDbContext();
        var normalizedCode = subdItemCode.Trim();

        return await context.SubdItems
            .AsNoTracking()
            .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
            .Where(si => si.SubdItemCode == normalizedCode)
            .Where(si => !excludeSubdItemId.HasValue || si.SubdItemId != excludeSubdItemId.Value)
            .AnyAsync(cancellationToken);
    }

    public async Task<List<TemplateRow>> GetTemplateDataAsync(int subDistributorId, string? principal, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        if (subDistributorId == 0)
        {
            // For "All Sub Distributors": generate rows for each subdistributor with their unmapped items
            var subdistributors = await context.SubDistributors
                .AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => new { s.SubDistributorId, s.SubdCode })
                .ToListAsync(cancellationToken);

            var results = new List<TemplateRow>();

            foreach (var subd in subdistributors)
            {
                // Get company items already mapped to this specific subdistributor
                var mappedItemIds = context.SubdItems
                    .AsNoTracking()
                    .Where(si => si.SubDistributorId == subd.SubDistributorId && si.IsActive)
                    .Select(si => si.CompanyItemId)
                    .ToList();

                // Get company items NOT mapped to this subdistributor
                var query = context.CompanyItems
                    .AsNoTracking()
                    .Where(ci => ci.IsActive)
                    .Where(ci => !mappedItemIds.Contains(ci.CompanyItemId));

                if (!string.IsNullOrWhiteSpace(principal))
                {
                    query = query.Where(ci => ci.Principal == principal);
                }

                var items = await query
                    .OrderBy(ci => ci.ItemCode)
                    .Select(ci => new
                    {
                        ci.ItemCode,
                        ci.ItemName,
                        ci.Principal
                    })
                    .ToListAsync(cancellationToken);

                foreach (var item in items)
                {
                    results.Add(new TemplateRow
                    {
                        CompanyItemCode = item.ItemCode,
                        CompanyItemName = item.ItemName,
                        Principal = item.Principal,
                        SubDistributorCode = subd.SubdCode,
                        SubdItemName = string.Empty,
                        SubdItemCode = string.Empty,
                        UOM = string.Empty,
                        Conversion = null,
                        Price = null
                    });
                }
            }

            return results;
        }
        else
        {
            // For specific subdistributor: get only items not mapped to that subdistributor
            var mappedCompanyItemIds = context.SubdItems
                .AsNoTracking()
                .Where(si => si.SubDistributorId == subDistributorId && si.IsActive)
                .Select(si => si.CompanyItemId)
                .ToList();

            var subdCode = await context.SubDistributors
                .Where(s => s.SubDistributorId == subDistributorId)
                .Select(s => s.SubdCode)
                .FirstOrDefaultAsync(cancellationToken);

            var query = context.CompanyItems
                .AsNoTracking()
                .Where(ci => ci.IsActive)
                .Where(ci => !mappedCompanyItemIds.Contains(ci.CompanyItemId));

            if (!string.IsNullOrWhiteSpace(principal))
            {
                query = query.Where(ci => ci.Principal == principal);
            }

            var results = await query
                .OrderBy(ci => ci.ItemCode)
                .Select(ci => new
                {
                    ci.ItemCode,
                    ci.ItemName,
                    ci.Principal
                })
                .ToListAsync(cancellationToken);

            var finalResults = new List<TemplateRow>();
            foreach (var item in results)
            {
                finalResults.Add(new TemplateRow
                {
                    CompanyItemCode = item.ItemCode,
                    CompanyItemName = item.ItemName,
                    Principal = item.Principal,
                    SubDistributorCode = subdCode ?? string.Empty,
                    SubdItemName = string.Empty,
                    SubdItemCode = string.Empty,
                    UOM = string.Empty,
                    Conversion = null,
                    Price = null
                });
            }

            return finalResults;
        }
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

public sealed class TemplateRow
{
    public string CompanyItemCode { get; set; } = string.Empty;
    public string CompanyItemName { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
    public string SubDistributorCode { get; set; } = string.Empty;
    public string SubdItemName { get; set; } = string.Empty;
    public string SubdItemCode { get; set; } = string.Empty;
    public string UOM { get; set; } = string.Empty;
    public decimal? Conversion { get; set; }
    public decimal? Price { get; set; }
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