namespace STTproject.Features.MapItem.DTOs;

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
    public string SubdName { get; set; } = string.Empty;
    public string UomName { get; set; } = string.Empty;
}
