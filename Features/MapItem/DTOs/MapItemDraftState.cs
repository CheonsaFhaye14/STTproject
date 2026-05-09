using STTproject.Models;

namespace STTproject.Features.MapItem.DTOs;

public sealed class MapItemDraftState
{
    public int SelectedSubdId { get; set; }
    public bool ShowAddUomModal { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? SelectedPrincipal { get; set; }
    public string? SelectedDropdownPrincipal { get; set; }
    public int? SelectedCompanyItemId { get; set; }
    public string? SelectedCompanyItemDisplayName { get; set; }
    public int? EditingSubdItemId { get; set; }
    public string? SelectedCompanyItemsFilterString { get; set; }
    public string? SelectedCompanyItemsCategoryString { get; set; }
    public Dictionary<string, UomEntry> UomEntries { get; set; } = new();
}
