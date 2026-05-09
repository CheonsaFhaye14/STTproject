namespace STTproject.Features.MapItem.DTOs;

public sealed class CompanyItemDropdownItem
{
    public int CompanyItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Principal { get; set; } = string.Empty;
}
