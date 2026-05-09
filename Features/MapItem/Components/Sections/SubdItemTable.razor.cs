using Microsoft.AspNetCore.Components;
using STTproject.Features.MapItem.DTOs;
using STTproject.Services;

namespace STTproject.Features.MapItem.Components.Sections;

public partial class SubdItemTable
{
    [Parameter] public bool IsSubDistributorSelected { get; set; }
    [Parameter] public IReadOnlyList<MapCompanyItemViewRow> CompanyItems { get; set; } = Array.Empty<MapCompanyItemViewRow>();
    [Parameter] public IReadOnlyList<MapSubDistributorItemRow> SubdItems { get; set; } = Array.Empty<MapSubDistributorItemRow>();
    [Parameter] public int? SelectedCompanyItemIdForFilter { get; set; }
    [Parameter] public EventCallback<int?> SelectedCompanyItemIdForFilterChanged { get; set; }
    [Parameter] public EventCallback<MapSubDistributorItemRow> OnEditItem { get; set; }
    [Parameter] public EventCallback<MapSubDistributorItemRow> OnDeleteItem { get; set; }
    [Parameter] public EventCallback OnClearCompanyItemFilter { get; set; }

    private async Task HandleEditClicked(MapSubDistributorItemRow item)
    {
        if (OnEditItem.HasDelegate)
        {
            await OnEditItem.InvokeAsync(item);
        }
    }

    private async Task HandleDeleteClicked(MapSubDistributorItemRow item)
    {
        if (OnDeleteItem.HasDelegate)
        {
            await OnDeleteItem.InvokeAsync(item);
        }
    }

    private async Task ClearFilterAsync()
    {
        if (SelectedCompanyItemIdForFilterChanged.HasDelegate)
        {
            await SelectedCompanyItemIdForFilterChanged.InvokeAsync(null);
        }

        if (OnClearCompanyItemFilter.HasDelegate)
        {
            await OnClearCompanyItemFilter.InvokeAsync();
        }
    }

    private int GetEmptyStateColumnSpan()
        => IsSubDistributorSelected ? 4 : 5;
}