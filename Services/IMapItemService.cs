using Microsoft.EntityFrameworkCore;

using STTproject.Models;
using STTproject.Data;
using STTproject.Features.User.MapItem.DTOs;
using Microsoft.Data.SqlClient;
namespace STTproject.Services;

public interface IMapItemService
{
    Task<List<string>> GetMapItemPrincipalsAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<MapCompanyItemRow>> GetMapCompanyItemsAsync(int userId, int subDistributorId, string? principal, CompanyItemFilterMode filterMode = CompanyItemFilterMode.All, CancellationToken cancellationToken = default);
    Task<List<MapSubDistributorItemRow>> GetMapSubDistributorItemsAsync(int userId, int subDistributorId, string? principal, bool includeAllSubDistributors = false, CancellationToken cancellationToken = default);
    Task<List<CompanyItemDropdownItem>> GetCompanyItemsForDropdownAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
    Task<List<string>> GetCompanyItemUomsAsync(int companyItemId, CancellationToken cancellationToken = default);
    Task<ItemsUom?> GetSubdItemUomAsync(int subdItemId, CancellationToken cancellationToken = default);
    Task<List<ItemsUom>> GetSubdItemUomsAsync(int subdItemId, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<bool> AddSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default);
    Task<UpdateSubdItemResult> UpdateSubdItemAsync(SubdItem item, CancellationToken cancellationToken = default);
    Task<DeleteSubdItemResult> DeleteSubdItemAsync(int subdItemId, CancellationToken cancellationToken = default);
    Task<bool> SubdItemCodeExistsAsync(int subDistributorId, string subdItemCode, int? excludeSubdItemId = null, CancellationToken cancellationToken = default);
    Task<bool> SaveSubdItemUomPricesAsync(int subdItemId, Dictionary<string, UomEntry> uomEntries, int currentUserId, CancellationToken cancellationToken = default, bool requireStockUom = true);   
    Task<List<TemplateRow>> GetTemplateDataAsync(int subDistributorId, string? principal, CancellationToken cancellationToken = default);
    Task<List<string>> GetInvoiceUsedUomNamesAsync(int subdItemId, CancellationToken cancellationToken = default);
}

public enum CompanyItemFilterMode
{
    All,
    Unmapped,
    Mapped
}

public class MapItemService : IMapItemService
{
    private readonly IDbContextFactory<EntrielContext> _contextFactory;
    private readonly ILogger<MapItemService> _logger;

    public MapItemService(IDbContextFactory<EntrielContext> contextFactory, ILogger<MapItemService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
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

        var recentCutoff = DateTime.Now.AddDays(-7);

    var query = context.CompanyItems
        .AsNoTracking()
        .Where(ci => ci.IsActive)
        .Select(ci => new MapCompanyItemRow
        {
            CompanyItemCode = ci.ItemCode,
            Description = ci.ItemName,
            Principal = ci.Principal,
            PPG = ci.Ppg ?? string.Empty,
            CompanyItemId = ci.CompanyItemId,
            StockPrice = ci.StockPrice,
            EffectivityDate = ci.CompanyItemPriceHistories
                .Where(h => h.AppliedDate == null)
                .OrderByDescending(h => h.EffectivityDate)
                .Select(h => (DateTime?)h.EffectivityDate)
                .FirstOrDefault(),
            PriceIncreaseAmount = ci.CompanyItemPriceHistories
                .Where(h => h.AppliedDate == null)
                .OrderByDescending(h => h.EffectivityDate)
                .Select(h => (decimal?)h.PriceIncreaseAmount)
                .FirstOrDefault(),
            RecentAppliedDate = ci.ItemsUomPriceHistories
                .Where(h => h.AppliedDate >= recentCutoff)
                .OrderByDescending(h => h.AppliedDate)
                .Select(h => (DateTime?)h.AppliedDate)
                .FirstOrDefault(),
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


    public async Task<List<MapSubDistributorItemRow>> GetMapSubDistributorItemsAsync(int userId, int subDistributorId, string? principal, bool includeAllSubDistributors = false, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var subdItemsQuery = context.SubdItems
            .AsNoTracking()
            .Where(si => si.IsActive)
            .Where(si => si.SubDistributor.IsActive);

        if (!includeAllSubDistributors)
        {
            subdItemsQuery = subdItemsQuery
                .Where(si => si.SubDistributor.EncoderId == userId);
        }

        var uomsQuery = context.ItemsUoms
            .AsNoTracking()
            .Where(u => u.IsActive);

        var query =
            from si in subdItemsQuery
            join uom in uomsQuery
                on si.SubdItemId equals uom.SubdItemId into uomGroup
            from uom in uomGroup.DefaultIfEmpty()
            select new
            {
                si.SubdItemId,
                si.SubDistributorId,
                si.SubdItemCode,
                si.ItemName,
                si.CompanyItemId,
                CompanyItemName = si.CompanyItem.ItemName,
                Price = uom != null ? uom.Price : (decimal?)null,
                Principal = si.CompanyItem.Principal,
                UomName = uom != null ? uom.UomName : string.Empty,
                ConversionToBase = uom != null
                    ? uom.ConversionToBase
                    : (int?)null,
                IsStockUom = uom != null && uom.IsStockUom,
                IsBaseUnit = uom != null && uom.IsBaseUnit,
                si.SubDistributor.SubdName,
                si.CreatedDate
            };

        if (subDistributorId > 0)
        {
            query = query.Where(
                item => item.SubDistributorId == subDistributorId);
        }

        if (!string.IsNullOrWhiteSpace(principal))
        {
            query = query.Where(item => item.Principal == principal);
        }

        var results = await query
            .OrderBy(item => item.SubdItemCode)
            .ThenBy(item => item.UomName)
            .ToListAsync(cancellationToken);

        return results
            .GroupBy(item => item.SubdItemId)
            .Select(group =>
            {
                var first = group.First();

                var uoms = group
                    .Where(item => !string.IsNullOrWhiteSpace(item.UomName))
                    .Select(item => new MapSubDistributorUomRow
                    {
                        UomName = item.UomName,
                        Price = item.Price,
                        ConversionToBase = item.ConversionToBase,
                        IsStockUom = item.IsStockUom,
                        IsBaseUnit = item.IsBaseUnit
                    })
                    .ToList();

                return new MapSubDistributorItemRow
                {
                    SubdItemId = group.Key,
                    SubDistributorId = first.SubDistributorId,
                    SubItemCode = first.SubdItemCode,
                    Description = first.ItemName,
                    Price = first.Price ?? 0m,
                    Principal = first.Principal,
                    CompanyItemId = first.CompanyItemId,
                    CompanyItemName = first.CompanyItemName,
                    SubdName = first.SubdName,
                    CreatedDate = first.CreatedDate,
                    UomName = string.Join(", ", uoms.Select(u => u.UomName)),
                    Uoms = uoms
                };
            })
            .OrderBy(item => item.SubItemCode)
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
            var result = await context.CompanyItems
                .AsNoTracking()
                .Where(ci => ci.IsActive)
                .OrderBy(ci => ci.ItemName)
                .Select(ci => new CompanyItemDropdownItem
                {
                    CompanyItemId = ci.CompanyItemId,
                    ItemCode = ci.ItemCode,
                    ItemName = ci.ItemName,
                    Principal = ci.Principal,
                    StockUomName = ci.StockUomName,
                    StockPrice = ci.StockPrice,
                    StockPriceRegional = ci.StockPriceRegional,
                    StockPriceNational = ci.StockPriceNational,
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
        return await context.ItemsUoms
            .AsNoTracking()
            .Where(u => u.SubdItemId == subdItemId && u.IsActive)
            .OrderByDescending(u => u.IsBaseUnit)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ItemsUom>> GetSubdItemUomsAsync(int subdItemId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        var query = context.ItemsUoms
            .AsNoTracking()
            .Where(u => u.SubdItemId == subdItemId);

        if (!includeInactive)
        {
            query = query.Where(u => u.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
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
                bool coreFieldsChanged =
                    !string.Equals(existing.SubdItemCode, item.SubdItemCode, StringComparison.Ordinal) ||
                    !string.Equals(existing.ItemName, item.ItemName, StringComparison.Ordinal);

                if (coreFieldsChanged)
                {
                    return UpdateSubdItemResult.InUse(
                        "The SKU code and item name cannot be changed because this item is already used by one or more invoices. You can still change the company item.");
                }
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

    public async Task<bool> SaveSubdItemUomPricesAsync(
        int subdItemId, Dictionary<string, UomEntry> uomEntries, int currentUserId,
        CancellationToken cancellationToken = default, bool requireStockUom = true)
    {
        if (uomEntries == null) return false;

        if (requireStockUom)
        {
            var stocks = uomEntries.Where(kv => kv.Value.IsActive && kv.Value.IsStockUom).ToList();
            if (stocks.Count == 0)
                throw new InvalidOperationException("Select which UOM is the stock UOM before saving.");
            if (stocks.Count > 1)
                throw new InvalidOperationException("Only one UOM can be the stock UOM.");
            if (stocks[0].Value.Conversion is not > 0)
                throw new InvalidOperationException("The stock UOM needs a valid conversion.");
            if (!stocks[0].Value.Price.HasValue)
                throw new InvalidOperationException("The stock UOM needs a price.");
        }

        try
        {
            await using var context = _contextFactory.CreateDbContext();
            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await context.ItemsUoms
                    .Where(u => u.SubdItemId == subdItemId && u.IsStockUom)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsStockUom, false), cancellationToken);

                var existingUoms = await context.ItemsUoms
                    .Where(u => u.SubdItemId == subdItemId)
                    .ToListAsync(cancellationToken);

                var uomIdsInUse = await context.SalesInvoiceItems
                    .AsNoTracking()
                    .Where(sii => sii.SubdItemId == subdItemId)
                    .Select(sii => sii.ItemsUomId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var existingIds = existingUoms.Select(e => e.ItemsUomId).ToList();
                var uomIdsWithHistory = await context.ItemsUomPriceHistories
                    .AsNoTracking()
                    .Where(h => existingIds.Contains(h.ItemsUomId))
                    .Select(h => h.ItemsUomId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var now = DateTime.Now;
                var matchedExistingIds = new HashSet<int>();

                foreach (var kv in uomEntries)
                {
                    var name = kv.Key.Trim();
                    var entry = kv.Value;

                    var existing = entry.ItemsUomId.HasValue
                        ? existingUoms.FirstOrDefault(e => e.ItemsUomId == entry.ItemsUomId.Value)
                        : existingUoms.FirstOrDefault(e =>
                            string.Equals(e.UomName, name, StringComparison.OrdinalIgnoreCase));
                    var nameOwner = existingUoms.FirstOrDefault(e =>
                        string.Equals(e.UomName, name, StringComparison.OrdinalIgnoreCase) &&
                        (existing == null || e.ItemsUomId != existing.ItemsUomId));

                    if (nameOwner != null)
                    {
                        if (nameOwner.IsActive)
                        {
                            continue;
                        }
                        existing = nameOwner;
                    }

                    var isBaseUnit = entry.Conversion == 1;
                    var isStock = entry.IsActive && entry.IsStockUom;

                    if (existing != null)
                    {
                        matchedExistingIds.Add(existing.ItemsUomId);

                        existing.UomName = name;
                        existing.ConversionToBase = entry.Conversion;
                        existing.Price = entry.Price ?? 0m;
                        existing.IsBaseUnit = isBaseUnit;
                        existing.IsStockUom = isStock;          
                        existing.IsActive = entry.IsActive;    
                        existing.UpdatedBy = currentUserId > 0 ? currentUserId : existing.UpdatedBy;
                        existing.UpdatedDate = now;
                        context.ItemsUoms.Update(existing);
                    }
                    else
                    {
                        context.ItemsUoms.Add(new ItemsUom
                        {
                            UomName = name,
                            ConversionToBase = entry.Conversion,
                            Price = entry.Price ?? 0m,
                            IsBaseUnit = isBaseUnit,
                            IsStockUom = isStock,              
                            IsActive = entry.IsActive,
                            SubdItemId = subdItemId,
                            CreatedDate = now,
                            UpdatedDate = now,
                            CreatedBy = currentUserId > 0 ? currentUserId : null,
                            UpdatedBy = currentUserId > 0 ? currentUserId : null
                        });
                    }
                }

                var toRemove = existingUoms
                    .Where(e => !matchedExistingIds.Contains(e.ItemsUomId))
                    .ToList();

                var toDeactivate = toRemove
                    .Where(e => uomIdsInUse.Contains(e.ItemsUomId) || uomIdsWithHistory.Contains(e.ItemsUomId))
                    .ToList();
                var toHardDelete = toRemove.Except(toDeactivate).ToList();

                foreach (var entry in toDeactivate)
                {
                    entry.IsActive = false;
                    entry.IsStockUom = false;                 
                    entry.UpdatedBy = currentUserId > 0 ? currentUserId : entry.UpdatedBy;
                    entry.UpdatedDate = now;
                    context.ItemsUoms.Update(entry);
                }

                if (toHardDelete.Any())
                    context.ItemsUoms.RemoveRange(toHardDelete);

                await context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql
                                            && sql.Message.Contains("OneStockPerSubdItem"))
            {
                await tx.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("Only one UOM can be the stock UOM for this item.");
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
    public async Task<List<string>> GetInvoiceUsedUomNamesAsync(int subdItemId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.SalesInvoiceItems
            .AsNoTracking()
            .Where(sii => sii.SubdItemId == subdItemId)
            .Select(sii => sii.ItemsUom.UomName)
            .Distinct()
            .ToListAsync(cancellationToken);
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

            // Remove any UOM rows that belong to this SubdItem first
            var existingUoms = await context.ItemsUoms
                .Where(u => u.SubdItemId == subdItemId)
                .ToListAsync(cancellationToken);

            if (existingUoms.Any())
            {
                context.ItemsUoms.RemoveRange(existingUoms);
                // persist deletion of dependent rows first to avoid relationship/severed errors
                await context.SaveChangesAsync(cancellationToken);
            }

            // Now remove the subd item
            context.SubdItems.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
            return DeleteSubdItemResult.Success();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            var baseMsg = dbEx.GetBaseException()?.Message ?? dbEx.Message;
            _logger?.LogError(dbEx, "DB update error deleting SubdItem {SubdItemId}: {Message}", subdItemId, baseMsg);
            return DeleteSubdItemResult.Failed($"Unable to delete the sub distributor item: {baseMsg}");
        }
        catch (Exception ex)
        {
            var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
            _logger?.LogError(ex, "Error deleting SubdItem {SubdItemId}: {Message}", subdItemId, baseMsg);
            return DeleteSubdItemResult.Failed($"Unable to delete the sub distributor item: {baseMsg}");
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



public sealed class MapCompanyItemRow
{
    public string CompanyItemCode { get; set; } = string.Empty;
    public string SubItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? StockPrice { get; set; }
    public string Principal { get; set; } = string.Empty;
    public string PPG { get; set; } = string.Empty;
    public int CompanyItemId { get; set; }
    public string UomName { get; set; } = string.Empty;
    public DateTime? EffectivityDate { get; set; }
    public decimal? PriceIncreaseAmount { get; set; }
    public DateTime? RecentAppliedDate { get; set; }
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