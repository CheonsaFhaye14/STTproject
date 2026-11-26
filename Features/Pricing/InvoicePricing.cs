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
    bool IsFree = false,
    // The company had no price yet on the invoice date, so the item's earliest price was used (see InvoicePricing).
    bool IsFallbackPrice = false,
    DateOnly? PriceEffectiveDate = null)
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
    ///   price but no StockUomName, no conversion, no price list -> Issue
    ///
    /// No price on or before the invoice date: the item's EARLIEST price is used instead (IsFallbackPrice = true).
    /// A company item's first price is usually entered after it was already being sold, so invoices dated before
    /// that first row would otherwise never price. Only when the item has no price row at all (for that price list)
    /// is it an Issue. Note: sp_RecalculateInvoiceAmounts / fn_CompanyPriceAtDate need the same fallback, or a
    /// recalculation will undo these prices.
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

        // Every price row of these items (any date): the dated ones price the invoice, the earliest is the fallback.
        var allHistory = await context.CompanyItemPriceHistories.AsNoTracking()
            .Where(h => companyIds.Contains(h.CompanyItemId))
            .Select(h => new { h.CompanyItemId, h.PriceListType, h.EffectivityDate, h.CompanyItemPriceHistoryId, h.NewPrice })
            .ToListAsync(ct);

        // Company items that have ever had a price row (any list, any date): these are never "free".
        var everPriced = allHistory.Select(h => h.CompanyItemId).ToHashSet();

        // Latest row on or before the invoice date per (company item, price list) — same ordering as fn_CompanyPriceAtDate
        var latest = allHistory
            .Where(h => h.EffectivityDate < cutoff)
            .GroupBy(h => (h.CompanyItemId, h.PriceListType))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(h => h.EffectivityDate.Date)
                      .ThenByDescending(h => h.CompanyItemPriceHistoryId)
                      .First());

        // Earliest row per (company item, price list): used when the invoice is older than the item's first price.
        var earliest = allHistory
            .GroupBy(h => (h.CompanyItemId, h.PriceListType))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(h => h.EffectivityDate.Date)
                      .ThenBy(h => h.CompanyItemPriceHistoryId)
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
            var isFallback = false;
            DateOnly? effectiveDate = null;

            if (!isFree)
            {
                if (!hasStockUom)
                    itemIssue = "Company item has a price but no stock UOM";
                else if (item.IsDual && string.IsNullOrWhiteSpace(priceListType))
                    itemIssue = "Sub distributor has no price list (Regional/National)";
                else
                {
                    var key = (item.CompanyItemId, listType);
                    hasPrice = latest.TryGetValue(key, out var price);
                    if (!hasPrice && earliest.TryGetValue(key, out var first))
                    {
                        // The invoice predates the item's first price: use that first price.
                        price = first;
                        hasPrice = true;
                        isFallback = true;
                    }

                    if (!hasPrice)
                        itemIssue = "The company item has no price yet";
                    else
                    {
                        newPrice = price!.NewPrice;
                        historyId = price.CompanyItemPriceHistoryId;
                        effectiveDate = DateOnly.FromDateTime(price.EffectivityDate);
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
                    IsFree: isFree && issue is null,
                    IsFallbackPrice: issue is null && !isFree && isFallback,
                    PriceEffectiveDate: issue is null && !isFree ? effectiveDate : null);
            }
        }

        return result;
    }
}