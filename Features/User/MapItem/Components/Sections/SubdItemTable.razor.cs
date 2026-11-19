using Microsoft.AspNetCore.Components;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Services;
using STTproject.Features.User.MapItem.Services;

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
    [Parameter] public HashSet<int> CompanyItemsWithStockUom { get; set; } = new();

    private bool CompanyRequiresStockUom(MapSubDistributorItemRow item) =>
        CompanyItemsWithStockUom.Contains(item.CompanyItemId) || !string.IsNullOrWhiteSpace(item.CompanyStockUomName);

    /// <summary>The company stock UOM isn't one of the item's rows. That's fine: it is always 1.</summary>
    private bool StockUomImplicit(MapSubDistributorItemRow item) =>
        CompanyRequiresStockUom(item)
        && !string.IsNullOrWhiteSpace(item.CompanyStockUomName)
        && !item.Uoms.Any(u => u.IsStockUom);

    /// <summary>Priced units without a conversion can't be priced.</summary>
    private bool HasMissingConversion(MapSubDistributorItemRow item) =>
        CompanyRequiresStockUom(item) && item.Uoms.Any(u => u.ConversionToBase is not > 0);
    private string SearchText { get; set; } = string.Empty;
    private SubItemSortOrder SortOrder { get; set; } = SubItemSortOrder.CreatedDateDesc;
    private SubItemSortColumn SortColumn { get; set; } = SubItemSortColumn.SubItemCode;
    private bool SortAscending { get; set; } = true;

    // Matches "Label - Value" pairs directly against the raw string instead of
    // splitting on every comma — values can contain thousand-separator commas
    // (e.g. "CS - 1,013.62"), which a naive Split(',') would break apart.
    private static readonly System.Text.RegularExpressions.Regex UomEntryPattern =
        new(@"([A-Za-z]+)\s*-\s*([\d,]+\.\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<(string Label, string Value)> ParseUomEntries(string? uomText)
    {
        var entries = new List<(string Label, string Value)>();
        if (string.IsNullOrWhiteSpace(uomText))
        {
            return entries;
        }

        foreach (System.Text.RegularExpressions.Match match in UomEntryPattern.Matches(uomText))
        {
            entries.Add((match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()));
        }

        return entries;
    }

    private IReadOnlyList<MapSubDistributorItemRow>? _lastSubdItemsRef;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_lastSubdItemsRef, SubdItems))
        {
            return;
        }

        _lastSubdItemsRef = SubdItems;
        CurrentPage = 1;
        RefreshFilteredItems();
    }

    private IEnumerable<MapSubDistributorItemRow> FilteredSubdItems => ApplySort(SubdItems.Where(MatchesSearch));
    private List<MapSubDistributorItemRow> _filteredSubdItems = new();
    private void RefreshFilteredItems()
    {
        var items = SubdItems.Where(MatchesSearch);

        _filteredSubdItems = ApplySort(items).ToList();
    }

    private void SetSortByColumn(SubItemSortColumn column)
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
        CurrentPage = 1;
        RefreshFilteredItems();
    }

    private void HandleSearchChanged(ChangeEventArgs e)
    {
        SearchText = e.Value?.ToString() ?? string.Empty;

        CurrentPage = 1;

        RefreshFilteredItems();
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
    private readonly Dictionary<string, List<(string Label, string Value)>> _uomCache = new();
    private List<(string Label, string Value)> GetUomEntries(string? uomText)
    {
        if (string.IsNullOrWhiteSpace(uomText))
            return new();

        if (_uomCache.TryGetValue(uomText, out var cached))
            return cached;

        var entries = ParseUomEntries(uomText);

        _uomCache[uomText] = entries;

        return entries;
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
    private int CurrentPage { get; set; } = 1;
    private const int PageSize = 50;

    private int TotalPages =>
        Math.Max(1, (int)Math.Ceiling(
            _filteredSubdItems.Count / (double)PageSize));

    private IEnumerable<MapSubDistributorItemRow> PagedItems =>
        _filteredSubdItems
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

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

    /// <summary>One line of the UOM cell: name, size in stock units, price.</summary>
    private sealed record UomLine(string Name, string Conversion, string Price, string Kind, string Tooltip);

    /// <summary>
    /// The item's units as small lines: the stock UOM first (a dashed line when the item has no row for it, it is
    /// always 1), then the rest by size. A stock-named unit at another size is a bundle (e.g. buy 1 take 1: PK = 2 PK).
    /// </summary>
    private static List<UomLine> UomLines(MapSubDistributorItemRow item)
    {
        var stock = item.CompanyStockUomName?.Trim();
        var free = string.IsNullOrWhiteSpace(stock);
        var lines = new List<UomLine>();

        // Company stock price, worked back from any priced unit (unit price = stock price × conversion).
        var stockPrice = item.Uoms
            .Where(u => u.Price is not null && u.ConversionToBase is > 0)
            .Select(u => u.Price!.Value / u.ConversionToBase!.Value)
            .Cast<decimal?>()
            .FirstOrDefault();

        static string Short(decimal v) => v.ToString(v >= 100 ? "#,0.##" : "0.####", System.Globalization.CultureInfo.InvariantCulture);
        string PriceText(decimal? p) => free ? "Free" : p is decimal v ? $"₱{v:N2}" : "-";

        if (!free && !item.Uoms.Any(u => u.IsStockUom))
        {
            lines.Add(new UomLine(stock!, "1", PriceText(stockPrice), "implicit",
                $"Company stock UOM ({stock}) · always 1 · not saved as a unit"));
        }

        foreach (var u in item.Uoms
                     .OrderByDescending(u => u.IsStockUom)
                     .ThenByDescending(u => u.ConversionToBase ?? decimal.MinValue))
        {
            if (u.IsStockUom)
            {
                lines.Add(new UomLine(u.UomName, "1", PriceText(u.Price), "stock", $"Company stock UOM ({stock}) · conversion 1"));
            }
            else if (u.ConversionToBase is not decimal c || c <= 0)
            {
                lines.Add(new UomLine(u.UomName, "no size", "no size", "missing", "No conversion: this unit can't be priced"));
            }
            else
            {
                var bundle = !free && StockUomSuggester.Canon(u.UomName) == StockUomSuggester.Canon(stock) && c != 1m;
                lines.Add(new UomLine(u.UomName, free ? Short(c) : $"{Short(c)} {stock}", PriceText(u.Price),
                    bundle ? "bundle" : "unit",
                    bundle
                        ? $"Bundle: 1 {u.UomName} = {Short(c)} {stock} (e.g. buy 1 take 1)"
                        : free ? $"Free item · 1 {u.UomName} = {Short(c)}" : $"1 {u.UomName} = {Short(c)} {stock}"));
            }
        }

        return lines;
    }

    // Conversions are relative to the company stock UOM (stock UOM = 1).
    private static string GetUomTooltip(MapSubDistributorItemRow item, MapSubDistributorUomRow entry)
    {
        var stock = item.CompanyStockUomName?.Trim();

        if (entry.IsStockUom)
            return $"Company stock UOM ({stock}) · conversion 1";

        if (string.IsNullOrWhiteSpace(stock))
            return "Free item: no stock UOM, priced at 0";

        if (entry.ConversionToBase is not > 0)
            return "No conversion: this unit can't be priced";

        return $"1 {entry.UomName} = {FormatConversion(entry.ConversionToBase.Value)} {stock}";
    }

    private static string FormatConversion(decimal value) =>
        value == Math.Round(value) ? value.ToString("N0") : value.ToString("0.######");
}