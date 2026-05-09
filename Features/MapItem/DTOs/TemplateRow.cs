namespace STTproject.Features.MapItem.DTOs;

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