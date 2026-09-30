using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using STTproject.Features.Admin.ImportTemplate.DTOs;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public sealed class TemplateDraftService
{
    private readonly ProtectedLocalStorage _store;
    public TemplateDraftService(ProtectedLocalStorage store) => _store = store;

    public sealed record Draft(ImportTemplateEditDto Model, bool IsGlobal, DateTime SavedAt);

    private static string Key(int? id) => $"import-template-draft:{(id?.ToString() ?? "new")}";

    public async Task SaveAsync(int? id, ImportTemplateEditDto model, bool isGlobal)
    {
        try { await _store.SetAsync(Key(id), new Draft(model, isGlobal, DateTime.Now)); }
        catch { /* a draft failing must never break editing */ }
    }

    public async Task<Draft?> LoadAsync(int? id)
    {
        try
        {
            var r = await _store.GetAsync<Draft>(Key(id));
            return r.Success ? r.Value : null;
        }
        catch { return null; }   
    }

    public async Task ClearAsync(int? id)
    {
        try { await _store.DeleteAsync(Key(id)); } catch { }
    }
}