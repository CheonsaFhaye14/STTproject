using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.Pricing;

/// <summary>
/// Price of one subd UOM on a given invoice date, derived from company price history.
/// ConversionToBase is relative to the company item's stock UOM (stock UOM = 1), so
/// one unit of this UOM costs StockPrice × ConversionToBase.
/// </summary>
public sealed record UomPriceInfo(
    int ItemsUomId,
    int SubdItemId,
    string SubdItemCode,
    string UomName,
    decimal ConversionToBase,
    decimal? StockPrice,
    int? CompanyItemPriceHistoryId,
    string? Issue,
    bool IsFree = false)
{
    /// <summary>Kept so existing callers compile: the stock UOM is always 1 now.</summary>
    public decimal StockConvToBase => 1m;

    /// <summary>Free company items (no stock UOM, never priced) count as priced at ₱0.</summary>
    public bool IsPriced => Issue is null && (IsFree || StockPrice.HasValue);

    /// <summary>Display price of one unit of this UOM.</summary>
    public decimal UnitPrice => IsPriced && !IsFree
        ? Math.Round(StockPrice!.Value * ConversionToBase, 2, MidpointRounding.AwayFromZero)
        : 0m;

    /// <summary>Quantity in company stock units (can be fractional: 3 PC = 0.25 CS).</summary>
    public decimal BaseQuantityFor(int quantity) =>
        Math.Round(Math.Abs(quantity) * ConversionToBase, 6, MidpointRounding.AwayFromZero);

    /// <summary>Line amount, rounded once: |qty| × conversion × stock price. Same rule as sp_RecalculateInvoiceAmounts.</summary>
    public decimal AmountFor(int quantity, bool isCredit)
    {
        if (!IsPriced || IsFree) return 0m;
        var amount = Math.Round(
            Math.Abs(quantity) * ConversionToBase * StockPrice!.Value,
            2, MidpointRounding.AwayFromZero);
        return isCredit ? -amount : amount;
    }
}

public static class InvoicePricing
{
    /// <summary>
    /// Prices every UOM (active or not) of the sub distributor's items on the invoice date.
    /// Key = ItemsUomId. Lines that cannot be priced carry an Issue instead of a price.
    /// The rules match sp_RecalculateInvoiceAmounts:
    ///   free company item (no StockUomName, no price, no history) -> priced at ₱0
    ///   price but no StockUomName, no conversion, no price list, no price on the date -> Issue
    /// </summary>
    public static async Task<Dictionary<int, UomPriceInfo>> LoadUomPricesAsync(
        EntrielContext context,
        int subDistributorId,
        DateOnly invoiceDate,
        IEnumerable<int>? subdItemIds,
        CancellationToken ct = default)
    {
        var priceListType = await context.SubDistributors.AsNoTracking()
            .Where(s => s.SubDistributorId == subDistributorId)
            .Select(s => s.PriceListType)
            .FirstOrDefaultAsync(ct);

        var itemsQuery = context.SubdItems.AsNoTracking()
            .Where(si => si.SubDistributorId == subDistributorId);

        var ids = subdItemIds?.Distinct().ToList();
        if (ids is not null)
            itemsQuery = itemsQuery.Where(si => ids.Contains(si.SubdItemId));

        var items = await itemsQuery
            .Select(si => new
            {
                si.SubdItemId,
                si.SubdItemCode,
                si.CompanyItemId,
                si.CompanyItem.StockUomName,
                si.CompanyItem.StockPrice,
                IsDual = si.CompanyItem.StockPriceRegional != null,
                HasListPrice = si.CompanyItem.StockPriceRegional != null || si.CompanyItem.StockPriceNational != null,
                Uoms = si.ItemsUoms
                    .Select(u => new { u.ItemsUomId, u.UomName, u.ConversionToBase, u.IsActive })
                    .ToList()
            })
            .ToListAsync(ct);

        var companyIds = items.Select(i => i.CompanyItemId).Distinct().ToList();
        var cutoff = invoiceDate.AddDays(1).ToDateTime(TimeOnly.MinValue);   // effective on or before the invoice date

        var history = await context.CompanyItemPriceHistories.AsNoTracking()
            .Where(h => companyIds.Contains(h.CompanyItemId) && h.EffectivityDate < cutoff)
            .Select(h => new { h.CompanyItemId, h.PriceListType, h.EffectivityDate, h.CompanyItemPriceHistoryId, h.NewPrice })
            .ToListAsync(ct);

        // Company items that have ever had a price row (any list, any date): these are never "free".
        var everPriced = (await context.CompanyItemPriceHistories.AsNoTracking()
                .Where(h => companyIds.Contains(h.CompanyItemId))
                .Select(h => h.CompanyItemId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        // Latest row per (company item, price list) — same ordering as fn_CompanyPriceAtDate
        var latest = history
            .GroupBy(h => (h.CompanyItemId, h.PriceListType))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(h => h.EffectivityDate.Date)
                      .ThenByDescending(h => h.CompanyItemPriceHistoryId)
                      .First());

        var result = new Dictionary<int, UomPriceInfo>();

        foreach (var item in items)
        {
            var hasStockUom = !string.IsNullOrWhiteSpace(item.StockUomName);
            var hasAnyPrice = (item.StockPrice ?? 0m) != 0m || item.HasListPrice || everPriced.Contains(item.CompanyItemId);
            var isFree = !hasStockUom && !hasAnyPrice;

            string? itemIssue = null;
            string? listType = item.IsDual ? priceListType : null;
            var hasPrice = false;
            decimal? newPrice = null;
            int? historyId = null;

            if (!isFree)
            {
                if (!hasStockUom)
                    itemIssue = "Company item has a price but no stock UOM";
                else if (item.IsDual && string.IsNullOrWhiteSpace(priceListType))
                    itemIssue = "Sub distributor has no price list (Regional/National)";
                else
                {
                    hasPrice = latest.TryGetValue((item.CompanyItemId, listType), out var price);
                    if (!hasPrice)
                        itemIssue = "No company price on or before the invoice date";
                    else
                    {
                        newPrice = price!.NewPrice;
                        historyId = price.CompanyItemPriceHistoryId;
                    }
                }
            }

            foreach (var u in item.Uoms)
            {
                var conv = u.ConversionToBase ?? 0m;

                // Free items only need a UOM that belongs to them and is active; no conversion required.
                var issue = !u.IsActive ? "UOM is inactive"
                          : isFree ? null
                          : itemIssue ?? (conv <= 0m ? "UOM has no conversion" : null);

                result[u.ItemsUomId] = new UomPriceInfo(
                    u.ItemsUomId, item.SubdItemId, item.SubdItemCode, u.UomName,
                    conv,
                    issue is null && !isFree ? newPrice : (isFree && issue is null ? 0m : null),
                    issue is null && !isFree ? historyId : null,
                    issue,
                    IsFree: isFree && issue is null);
            }
        }

        return result;
    }
}