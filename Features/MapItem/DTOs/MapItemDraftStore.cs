namespace STTproject.Features.MapItem.DTOs;

public sealed class MapItemDraftStore
{
    public Dictionary<string, MapItemDraftState> Drafts { get; set; } = new();
}
