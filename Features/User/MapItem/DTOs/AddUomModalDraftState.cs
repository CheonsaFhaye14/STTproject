using STTproject.Models;

namespace STTproject.Features.User.MapItem.DTOs;

public class AddUomModalDraftState
{
    public Dictionary<string, UomEntry> WorkingUomEntries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SelectedUomOption { get; set; } = string.Empty;
    public string CustomUom { get; set; } = string.Empty;
    public string ConversionInput { get; set; } = string.Empty;
    public string PriceInput { get; set; } = string.Empty;

    /// <summary>
    /// The saved units the draft was started from. When the item's units change in the database (saved elsewhere, imported,
    /// fixed by script), the draft no longer matches and is dropped so stale units don't come back.
    /// </summary>
    public string? BaseFingerprint { get; set; }
}