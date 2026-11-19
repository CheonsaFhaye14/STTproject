namespace STTproject.Features.User.MapItem.DTOs;

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
    public DateTime? CreatedDate { get; set; }

    /// <summary>The company item's stock UOM (conversion 1). The item may or may not have a row for it.</summary>
    public string? CompanyStockUomName { get; set; }

    public List<MapSubDistributorUomRow> Uoms { get; set; } = new();
}

public sealed class MapSubDistributorUomRow
{
    public string UomName { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    /// <summary>Relative to the company stock UOM (stock UOM = 1).</summary>
    public decimal? ConversionToBase { get; set; }

    /// <summary>This unit is the company stock UOM (matched by name and UOM synonyms, not stored).</summary>
    public bool IsStockUom { get; set; }
}