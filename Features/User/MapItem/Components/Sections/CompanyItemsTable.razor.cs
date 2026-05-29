using Microsoft.AspNetCore.Components;
using STTproject.Features.User.MapItem.DTOs;

namespace STTproject.Features.User.MapItem.Components.Sections;

public partial class CompanyItemsTable
{
    private enum CompanyItemSortColumn
    {
        CompanyItemCode,
        Category,
        ItemName
    }

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

    private string SearchText { get; set; } = string.Empty;
    private CompanyItemSortColumn SortColumn { get; set; } = CompanyItemSortColumn.CompanyItemCode;
    private bool SortAscending { get; set; } = true;

    private IEnumerable<MapCompanyItemViewRow> FilteredCompanyItems => ApplySort(CompanyItems.Where(MatchesSearch));

    private Task SetSortByColumnAsync(CompanyItemSortColumn column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }

        return Task.CompletedTask;
    }

    private string GetSortIndicator(CompanyItemSortColumn column)
    {
        if (SortColumn != column)
        {
            return string.Empty;
        }

        return SortAscending ? " ▲" : " ▼";
    }

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

    private bool MatchesSearch(MapCompanyItemViewRow item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return ContainsIgnoreCase(item.CompanyItemCode, search)
            || ContainsIgnoreCase(item.Category, search)
            || ContainsIgnoreCase(item.ItemName, search);
    }

    private static bool ContainsIgnoreCase(string value, string search)
    {
        return value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<MapCompanyItemViewRow> ApplySort(IEnumerable<MapCompanyItemViewRow> items)
    {
        return SortColumn switch
        {
            CompanyItemSortColumn.Category => SortAscending
                ? items.OrderBy(item => string.IsNullOrWhiteSpace(item.Category) ? 1 : 0)
                    .ThenBy(item => item.Category)
                    .ThenBy(item => item.CompanyItemCode)
                : items.OrderBy(item => string.IsNullOrWhiteSpace(item.Category) ? 1 : 0)
                    .ThenByDescending(item => item.Category)
                    .ThenBy(item => item.CompanyItemCode),
            CompanyItemSortColumn.ItemName => SortAscending
                ? items.OrderBy(item => item.ItemName).ThenBy(item => item.CompanyItemCode)
                : items.OrderByDescending(item => item.ItemName).ThenBy(item => item.CompanyItemCode),
            _ => SortAscending
                ? items.OrderBy(item => item.CompanyItemCode).ThenBy(item => item.ItemName)
                : items.OrderByDescending(item => item.CompanyItemCode).ThenBy(item => item.ItemName)
        };
    }
}