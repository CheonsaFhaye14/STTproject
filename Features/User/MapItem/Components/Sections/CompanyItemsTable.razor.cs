using Microsoft.AspNetCore.Components;
using STTproject.Features.User.MapItem.DTOs;

namespace STTproject.Features.User.MapItem.Components.Sections;

public partial class CompanyItemsTable : IDisposable
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

    // ── Search (debounced) ──
    private string _searchInput = "";
    private string _committedSearch = "";
    private CancellationTokenSource? _debounceCts;

    private string SearchText
    {
        get => _searchInput;
        set
        {
            _searchInput = value;

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            _ = DebounceSearchAsync(value, _debounceCts.Token);
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }

    private async Task DebounceSearchAsync(string value, CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        _committedSearch = value;
        CurrentPage = 1;
        await InvokeAsync(StateHasChanged);
    }

    // ── Pagination ──
    private int CurrentPage { get; set; } = 1;
    private const int PageSize = 50;

    private int TotalPages =>
        Math.Max(1, (int)Math.Ceiling(FilteredCompanyItems.Count() / (double)PageSize));

    private IEnumerable<MapCompanyItemViewRow> PagedCompanyItems =>
        FilteredCompanyItems.Skip((CurrentPage - 1) * PageSize).Take(PageSize);

    // ── Filter/sort cache (single declaration) ──
    private List<MapCompanyItemViewRow>? _cachedFiltered;
    private string? _cacheKey;

    private CompanyItemSortColumn SortColumn { get; set; } = CompanyItemSortColumn.CompanyItemCode;
    private bool SortAscending { get; set; } = true;

    private IEnumerable<MapCompanyItemViewRow> FilteredCompanyItems
    {
        get
        {
            var key = $"{_committedSearch}|{SelectedCompanyItemsCategoryString}|{SelectedCompanyItemsFilterString}|{SortColumn}|{SortAscending}|{CompanyItems.Count}";
            if (_cacheKey != key)
            {
                _cachedFiltered = ApplySort(CompanyItems.Where(MatchesSearch).Where(MatchesFilter)).ToList();
                _cacheKey = key;
            }
            return _cachedFiltered!;
        }
    }

    private bool MatchesFilter(MapCompanyItemViewRow item)
    {
        return SelectedCompanyItemsFilterString switch
        {
            "Unmapped" => !item.IsMapped,
            "Mapped" => item.IsMapped,
            _ => true 
        };
    }

    private Task SetSortByColumnAsync(CompanyItemSortColumn column)
    {
        if (SortColumn == column) SortAscending = !SortAscending;
        else { SortColumn = column; SortAscending = true; }
        CurrentPage = 1;
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
        CurrentPage = 1;
        if (SelectedCompanyItemsCategoryStringChanged.HasDelegate)
            await SelectedCompanyItemsCategoryStringChanged.InvokeAsync(SelectedCompanyItemsCategoryString);
        if (OnCompanyItemsCategoryChanged.HasDelegate)
            await OnCompanyItemsCategoryChanged.InvokeAsync();
    }

    private async Task HandleFilterChanged()
    {
        CurrentPage = 1;
        if (SelectedCompanyItemsFilterStringChanged.HasDelegate)
            await SelectedCompanyItemsFilterStringChanged.InvokeAsync(SelectedCompanyItemsFilterString);
        if (OnCompanyItemsFilterStringChanged.HasDelegate)
            await OnCompanyItemsFilterStringChanged.InvokeAsync();
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
        if (string.IsNullOrWhiteSpace(_committedSearch))
        {
            return true;
        }

        var search = _committedSearch.Trim();
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
                ? items.OrderBy(item => item.IsMapped ? 0 : 1)  // mapped first
                    .ThenBy(item => item.CompanyItemCode)
                    .ThenBy(item => item.ItemName)
                : items.OrderBy(item => item.IsMapped ? 0 : 1)
                    .ThenByDescending(item => item.CompanyItemCode)
                    .ThenBy(item => item.ItemName)
        };
    }
}