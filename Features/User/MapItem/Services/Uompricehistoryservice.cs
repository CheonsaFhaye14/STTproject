using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.User.MapItem.Services;

/// <summary>One company price row for the Add UOM price history (price per company stock UOM).</summary>
public sealed record CompanyPriceHistoryRow(
    int CompanyItemPriceHistoryId,
    DateTime EffectivityDate,
    string? PriceListType,
    decimal OldPrice,
    decimal NewPrice,
    decimal PriceIncreaseAmount,
    DateTime? AppliedDate);

/// <summary>
/// Reads CompanyItemPriceHistory for the Add UOM window. Register as scoped:
///     builder.Services.AddScoped&lt;UomPriceHistoryService&gt;();
/// </summary>
public class UomPriceHistoryService
{
    private readonly EntrielContext _context;

    public UomPriceHistoryService(EntrielContext context) => _context = context;

    public async Task<List<CompanyPriceHistoryRow>> GetAsync(int companyItemId, string? priceListType, CancellationToken ct = default)
    {
        var list = string.IsNullOrWhiteSpace(priceListType) ? null : priceListType.Trim();

        return await _context.CompanyItemPriceHistories.AsNoTracking()
            .Where(h => h.CompanyItemId == companyItemId && h.PriceListType == list)
            .OrderByDescending(h => h.EffectivityDate)
            .ThenByDescending(h => h.CompanyItemPriceHistoryId)
            .Select(h => new CompanyPriceHistoryRow(
                h.CompanyItemPriceHistoryId,
                h.EffectivityDate,
                h.PriceListType,
                h.OldPrice,
                h.NewPrice,
                h.PriceIncreaseAmount,
                h.AppliedDate))
            .ToListAsync(ct);
    }
}