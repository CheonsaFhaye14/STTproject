namespace STTproject.Models;

public sealed class UomEntry
{
    public decimal Conversion { get; set; } // pieces per unit
    public decimal? Price { get; set; }
    public bool IsAutoCalculated { get; set; }
}
