using Microsoft.AspNetCore.Components;
using STTproject.Features.MapItem.DTOs;

namespace STTproject.Features.MapItem.Components.Sections;

public partial class CompanyItemsTable
{
    [Parameter] public bool IsSubDistributorSelected { get; set; }
    [Parameter] public IReadOnlyList<string> CompanyItemCategories { get; set; } = Array.Empty<string>();
    [Parameter] public IReadOnlyList<MapCompanyItemViewRow> CompanyItems { get; set; } = Array.Empty<MapCompanyItemViewRow>();
    [Parameter] public string SelectedCompanyItemsCategoryString { get; set; } = "All";
    [Parameter] public EventCallback<string> SelectedCompanyItemsCategoryStringChanged { get; set; }
    [Parameter] public EventCallback OnCompanyItemsCategoryChanged { get; set; }
    [Parameter] public string SelectedCompanyItemsFilterString { get; set; } = "All";
    [Parameter] public EventCallback<string> SelectedCompanyItemsFilterStringChanged { get; set; }
    [Parameter] public EventCallback OnCompanyItemsFilterStringChanged { get; set; }
    [Parameter] public int? SelectedCompanyItemIdForFilter { get; set; }
    [Parameter] public EventCallback<int?> SelectedCompanyItemIdForFilterChanged { get; set; }
    [Parameter] public EventCallback<MapCompanyItemViewRow> OnCompanyItemRowClicked { get; set; }
    [Parameter] public EventCallback OnClearCompanyItemFilter { get; set; }

    private async Task HandleCategoryChanged()
    {
        if (SelectedCompanyItemsCategoryStringChanged.HasDelegate)
        {
            await SelectedCompanyItemsCategoryStringChanged.InvokeAsync(SelectedCompanyItemsCategoryString);
        }

        if (OnCompanyItemsCategoryChanged.HasDelegate)
        {
            await OnCompanyItemsCategoryChanged.InvokeAsync();
        }
    }

    private async Task HandleFilterChanged()
    {
        if (SelectedCompanyItemsFilterStringChanged.HasDelegate)
        {
            await SelectedCompanyItemsFilterStringChanged.InvokeAsync(SelectedCompanyItemsFilterString);
        }

        if (OnCompanyItemsFilterStringChanged.HasDelegate)
        {
            await OnCompanyItemsFilterStringChanged.InvokeAsync();
        }
    }

    private async Task HandleRowClicked(MapCompanyItemViewRow item)
    {
        if (OnCompanyItemRowClicked.HasDelegate)
        {
            await OnCompanyItemRowClicked.InvokeAsync(item);
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
}