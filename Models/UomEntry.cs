namespace STTproject.Models;

public sealed class UomEntry
{
    public string? ConversionBasedOn { get; set; } 
    public decimal Conversion { get; set; } // pieces per ConversionBasedOn
    public decimal? Price { get; set; }
    public bool IsAutoCalculated { get; set; }
    public bool IsActive { get; set; } = true;
}
