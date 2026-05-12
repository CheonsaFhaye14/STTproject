using System.Text.Json;
using Microsoft.JSInterop;
using STTproject.Features.User.MapItem.DTOs;

namespace STTproject.Features.User.MapItem.Services;

public sealed class MapItemDraftService
{
    private readonly IJSRuntime jsRuntime;
    private IJSObjectReference? jsModule;

    public MapItemDraftService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    public async Task SaveDraftStoreAsync(string storageKey, MapItemDraftStore draftStore)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("saveSalesInvoiceDraft", storageKey, JsonSerializer.Serialize(draftStore));
    }

    public async Task<MapItemDraftStore> LoadDraftStoreAsync(string storageKey)
    {
        var module = await GetModuleAsync();
        var draftJson = await module.InvokeAsync<string?>("loadSalesInvoiceDraft", storageKey);

        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return new MapItemDraftStore();
        }

        return JsonSerializer.Deserialize<MapItemDraftStore>(draftJson) ?? new MapItemDraftStore();
    }

    public async Task ClearDraftStoreAsync(string storageKey)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("clearSalesInvoiceDraft", storageKey);
    }

    public async Task SaveSelectionStateAsync(string storageKey, MapItemSelectionState selectionState)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("saveSalesInvoiceDraft", storageKey, JsonSerializer.Serialize(selectionState));
    }

    public async Task<MapItemSelectionState?> LoadSelectionStateAsync(string storageKey)
    {
        var module = await GetModuleAsync();
        var selectionJson = await module.InvokeAsync<string?>("loadSalesInvoiceDraft", storageKey);

        if (string.IsNullOrWhiteSpace(selectionJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<MapItemSelectionState>(selectionJson);
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        jsModule ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
        return jsModule;
    }
}
