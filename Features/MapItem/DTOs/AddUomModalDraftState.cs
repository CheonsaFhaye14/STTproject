using STTproject.Models;

namespace STTproject.Features.MapItem.DTOs;

public class AddUomModalDraftState
{
    public Dictionary<string, UomEntry> WorkingUomEntries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SelectedUomOption { get; set; } = string.Empty;
    public string CustomUom { get; set; } = string.Empty;
    public string ConversionInput { get; set; } = string.Empty;
    public string PriceInput { get; set; } = string.Empty;
}