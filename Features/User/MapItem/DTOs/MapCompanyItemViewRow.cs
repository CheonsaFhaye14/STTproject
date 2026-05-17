namespace STTproject.Features.User.MapItem.DTOs;

public sealed class MapCompanyItemViewRow
{
    public int CompanyItemId { get; set; }
    public string CompanyItemCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UomName { get; set; } = string.Empty;
    public DateTime? EffectivityDate { get; set; }
    public decimal? PriceIncreasePercent { get; set; }
    public DateTime? RecentAppliedDate { get; set; }
    public bool HasRecentIncrease => RecentAppliedDate.HasValue;
}
