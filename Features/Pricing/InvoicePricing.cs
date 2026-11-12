using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.Pricing;

/// <summary>Price of one subd UOM on a given invoice date, derived from company price history.</summary>
public sealed record UomPriceInfo(
    int ItemsUomId,
    int SubdItemId,
    string SubdItemCode,
    string UomName,
    int ConversionToBase,
    int StockConvToBase,
    decimal? StockPrice,
    int? CompanyItemPriceHistoryId,
    string? Issue)
{
    public bool IsPriced => Issue is null && StockPrice.HasValue;

    /// <summary>Display price of one unit of this UOM.</summary>
    public decimal UnitPrice => IsPriced
        ? Math.Round(StockPrice!.Value * ConversionToBase / StockConvToBase, 2, MidpointRounding.AwayFromZero)
        : 0m;

    public int BaseQuantityFor(int quantity) => Math.Abs(quantity) * ConversionToBase;

    /// <summary>Line amount, rounded once: qty x conversion x stock price / stock conversion.</summary>
    public decimal AmountFor(int quantity, bool isCredit)
    {
        if (!IsPriced) return 0m;
        var amount = Math.Round(
            Math.Abs(quantity) * ConversionToBase * StockPrice!.Value / StockConvToBase,
            2, MidpointRounding.AwayFromZero);
        return isCredit ? -amount : amount;
    }
}

public static class InvoicePricing
{
    /// <summary>
    /// Prices every UOM (active or not) of the sub distributor's items on the invoice date.
    /// Key = ItemsUomId. Lines that cannot be priced carry an Issue instead of a price.
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
                IsDual = si.CompanyItem.StockPriceRegional != null,
                Uoms = si.ItemsUoms
                    .Select(u => new { u.ItemsUomId, u.UomName, u.ConversionToBase, u.IsStockUom, u.IsActive })
                    .ToList()
            })
            .ToListAsync(ct);

        var companyIds = items.Select(i => i.CompanyItemId).Distinct().ToList();
        var cutoff = invoiceDate.AddDays(1).ToDateTime(TimeOnly.MinValue);   // effective on or before the invoice date

        var history = await context.CompanyItemPriceHistories.AsNoTracking()
            .Where(h => companyIds.Contains(h.CompanyItemId) && h.EffectivityDate < cutoff)
            .Select(h => new { h.CompanyItemId, h.PriceListType, h.EffectivityDate, h.CompanyItemPriceHistoryId, h.NewPrice })
            .ToListAsync(ct);

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
            var stock = item.Uoms.Where(u => u.IsStockUom && u.IsActive).ToList();
            string? itemIssue = null;
            var stockConv = 0;

            if (stock.Count == 0) itemIssue = "No stock UOM";
            else if (stock.Count > 1) itemIssue = "More than one stock UOM";
            else if ((stock[0].ConversionToBase ?? 0) <= 0) itemIssue = "Stock UOM has no conversion";
            else stockConv = stock[0].ConversionToBase!.Value;

            string? listType = item.IsDual ? priceListType : null;
            if (itemIssue is null && item.IsDual && string.IsNullOrWhiteSpace(priceListType))
                itemIssue = "Sub distributor has no price list (Regional/National)";

            var hasPrice = latest.TryGetValue((item.CompanyItemId, listType), out var price);
            if (itemIssue is null && !hasPrice)
                itemIssue = "No company price on or before the invoice date";

            foreach (var u in item.Uoms)
            {
                var conv = u.ConversionToBase ?? 0;
                var issue = itemIssue
                            ?? (!u.IsActive ? "UOM is inactive" : null)
                            ?? (conv <= 0 ? "UOM has no conversion" : null);

                result[u.ItemsUomId] = new UomPriceInfo(
                    u.ItemsUomId, item.SubdItemId, item.SubdItemCode, u.UomName,
                    conv, stockConv,
                    issue is null ? price!.NewPrice : null,
                    issue is null ? price!.CompanyItemPriceHistoryId : null,
                    issue);
            }
        }

        return result;
    }
}