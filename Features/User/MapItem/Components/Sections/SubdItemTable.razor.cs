using Microsoft.AspNetCore.Components;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Services;

namespace STTproject.Features.User.MapItem.Components.Sections;

public partial class SubdItemTable
{
    private enum SubItemSortColumn
    {
        CreatedDate,
        SubItemCode,
        Description,
        UomName,
        SubdName
    }

    private enum SubItemSortOrder
    {
        CreatedDateDesc,
        CreatedDateAsc
    }

    [Parameter] public bool IsSubDistributorSelected { get; set; }
    [Parameter] public IReadOnlyList<MapCompanyItemViewRow> CompanyItems { get; set; } = Array.Empty<MapCompanyItemViewRow>();
    [Parameter] public IReadOnlyList<MapSubDistributorItemRow> SubdItems { get; set; } = Array.Empty<MapSubDistributorItemRow>();
    [Parameter] public int? SelectedCompanyItemIdForFilter { get; set; }
    [Parameter] public EventCallback<int?> SelectedCompanyItemIdForFilterChanged { get; set; }
    [Parameter] public EventCallback<MapSubDistributorItemRow> OnEditItem { get; set; }
    [Parameter] public EventCallback<MapSubDistributorItemRow> OnDeleteItem { get; set; }
    [Parameter] public EventCallback OnClearCompanyItemFilter { get; set; }
    [Parameter] public EventCallback<int> OnMapCompanyItemRequested { get; set; }

    private string SearchText { get; set; } = string.Empty;
    private SubItemSortOrder SortOrder { get; set; } = SubItemSortOrder.CreatedDateDesc;
    private SubItemSortColumn SortColumn { get; set; } = SubItemSortColumn.SubItemCode;
    private bool SortAscending { get; set; } = true;

    private IEnumerable<MapSubDistributorItemRow> FilteredSubdItems => ApplySort(SubdItems.Where(MatchesSearch));

    private Task SetSortByColumnAsync(SubItemSortColumn column)
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

    private Task HandleSortOrderChanged()
    {
        SortColumn = SubItemSortColumn.CreatedDate;
        SortAscending = SortOrder == SubItemSortOrder.CreatedDateAsc;
        return Task.CompletedTask;
    }

    private string GetSortIndicator(SubItemSortColumn column)
    {
        if (SortColumn != column)
        {
            return string.Empty;
        }

        return SortAscending ? " ▲" : " ▼";
    }

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

    private async Task MapSelectedCompanyItemAsync()
    {
        if (!SelectedCompanyItemIdForFilter.HasValue || !OnMapCompanyItemRequested.HasDelegate)
        {
            return;
        }

        await OnMapCompanyItemRequested.InvokeAsync(SelectedCompanyItemIdForFilter.Value);
    }

    private int GetEmptyStateColumnSpan()
        => IsSubDistributorSelected ? 4 : 5;

    private IEnumerable<MapSubDistributorItemRow> ApplySort(IEnumerable<MapSubDistributorItemRow> items)
    {
        if (SortColumn == SubItemSortColumn.CreatedDate)
        {
            return SortAscending
                ? items.OrderBy(item => item.CreatedDate ?? DateTime.MinValue).ThenBy(item => item.SubItemCode)
                : items.OrderByDescending(item => item.CreatedDate ?? DateTime.MinValue).ThenBy(item => item.SubItemCode);
        }

        return SortColumn switch
        {
            SubItemSortColumn.SubItemCode => SortAscending
                ? items.OrderBy(item => item.SubItemCode).ThenBy(item => item.Description)
                : items.OrderByDescending(item => item.SubItemCode).ThenBy(item => item.Description),
            SubItemSortColumn.Description => SortAscending
                ? items.OrderBy(item => item.Description).ThenBy(item => item.SubItemCode)
                : items.OrderByDescending(item => item.Description).ThenBy(item => item.SubItemCode),
            SubItemSortColumn.UomName => SortAscending
                ? items.OrderBy(item => item.UomName).ThenBy(item => item.SubItemCode)
                : items.OrderByDescending(item => item.UomName).ThenBy(item => item.SubItemCode),
            SubItemSortColumn.SubdName => SortAscending
                ? items.OrderBy(item => item.SubdName).ThenBy(item => item.SubItemCode)
                : items.OrderByDescending(item => item.SubdName).ThenBy(item => item.SubItemCode),
            _ => items
        };
    }

    private bool MatchesSearch(MapSubDistributorItemRow item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return ContainsIgnoreCase(item.SubItemCode, search)
            || ContainsIgnoreCase(item.Description, search)
            || ContainsIgnoreCase(item.UomName, search)
            || (!IsSubDistributorSelected && ContainsIgnoreCase(item.SubdName, search));
    }

    private static bool ContainsIgnoreCase(string? value, string search)
    {
        return !string.IsNullOrEmpty(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}