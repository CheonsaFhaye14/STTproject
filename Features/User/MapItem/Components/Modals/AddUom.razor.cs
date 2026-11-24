using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using STTproject.Models;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Features.User.MapItem.Validators;
using STTproject.Features.User.MapItem.Services;
using STTproject.Shared.Components.Forms;

namespace STTproject.Features.User.MapItem.Components.Modals;

/// <summary>
/// Units of measure for one subd item.
///
/// Conversions are stored relative to the company item's stock UOM (stock UOM = 1, PC of a 12-piece case = 0.0833333333),
/// the same as ItemsUom.ConversionToBase. They can be shown per stock UOM or per piece. Prices are not entered here:
/// each one is the company stock price × conversion, shown for reference only.
/// </summary>
public partial class AddUom : IDisposable
{
    /// <summary>Name used for the piece unit (the page passes "Piece").</summary>
    [Parameter] public string BaseUomName { get; set; } = "PC";
    [Parameter] public HashSet<string> InUseUomNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [Parameter] public bool ShowAddUomModal { get; set; }
    [Parameter] public Dictionary<string, UomEntry> ExistingUomEntries { get; set; } = new();
    [Parameter] public string? DraftStorageKey { get; set; }
    [Parameter] public string? ItemCode { get; set; }
    [Parameter] public string? ItemName { get; set; }
    [Parameter] public EventCallback<Dictionary<string, UomEntry>> OnAdd { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public int? CompanyItemId { get; set; }
    [Parameter] public string? CompanyStockUomName { get; set; }
    /// <summary>Matches unit names to the company stock UOM (built-in spellings + this subd's UOM synonyms).</summary>
    [Parameter] public UomNameMatcher? UomMatcher { get; set; }
    [Parameter] public int SubDistributorId { get; set; }
    [Parameter] public decimal? ApplicableStockPrice { get; set; }
    [Parameter] public string? PriceListLabel { get; set; }
    [Parameter] public string? ReferenceNote { get; set; }
    /// <summary>A unit name to have ready in the Unit of Measure field when the window opens (for example from an import error).</summary>
    [Parameter] public string? SuggestedUomName { get; set; }

    [Inject] private UomPriceHistoryService PriceHistoryService { get; set; } = default!;

    private const string PieceCanon = "PIECE";

    private Dictionary<string, UomEntry> workingUomEntries = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> validationErrors = new();

    // ── Stock UOM and piece reference ──────────────────────────────────────

    /// <summary>The company item has a stock UOM. Without one it is a free item and conversions don't affect prices.</summary>
    private bool StockUomRequired => !string.IsNullOrWhiteSpace(CompanyStockUomName);

    private string StockCanon => StockUomSuggester.Canon(CompanyStockUomName);

    private bool StockIsPiece => StockCanon == PieceCanon;

    private bool IsStockName(string? name) =>
        (UomMatcher ?? UomNameMatcher.BuiltInOnly).IsStockUom(SubDistributorId, name, CompanyStockUomName);

    private bool IsPieceName(string? name) => StockUomSuggester.Canon(name) == PieceCanon;

    /// <summary>The working entry that is the company stock UOM (matched by name, CS = CASE and so on).</summary>
    /// <remarks>
    /// Only a row at conversion 1 is the stock UOM. A stock-named row the subd sells differently (e.g. Pack = 2 PK for buy 1
    /// take 1) is an ordinary unit: it can be renamed and resized, and the company stock UOM is then implicit (always 1).
    /// </remarks>
    private string? StockEntryName =>
        workingUomEntries.Where(kv => kv.Value.IsActive && IsStockName(kv.Key) && (kv.Value.Conversion is null || kv.Value.Conversion == 1m))
                         .Select(kv => kv.Key).FirstOrDefault();

    /// <summary>Key for the company stock UOM when the item has no row for it (it is always 1).</summary>
    private const string StockKey = "__stock";

    /// <summary>The stock UOM's key: its row, or <see cref="StockKey"/> when there is none.</summary>
    private string StockRefKey => StockEntryName ?? StockKey;

    /// <summary>Text for a unit key ("__stock" → the company stock UOM name).</summary>
    private string UnitText(string? key)
    {
        if (key == StockKey) return CompanyStockUomName?.Trim() ?? "stock UOM";
        if (key is null) return string.Empty;
        return IsBundle(key) && workingUomEntries[key].Conversion is decimal c ? $"{key} ×{FormatNumber(c)}" : key;
    }

    /// <summary>A stock-named unit the subd sells at another conversion (buy 1 take 1: PK = 2 PK).</summary>
    /// <remarks>Not while the item still needs rescaling: then the stock-named row is per piece, not a bundle.</remarks>
    private bool IsBundle(string name) =>
        StockUomRequired && !StockNeedsRescale
        && workingUomEntries.TryGetValue(name, out var e) && e.IsActive
        && IsStockName(name) && e.Conversion is decimal c && c > 0 && c != 1m;

    private UomEntry? StockEntry => StockEntryName is { } n ? workingUomEntries[n] : null;

    /// <summary>
    /// The company stock UOM isn't one of the item's units. That's allowed: it is always 1, so it still works as the
    /// unit conversions are counted in and as the price reference.
    /// </summary>
    private bool StockRowImplicit => StockUomRequired && StockEntryName is null;

    private string StockDisplayName => StockEntryName ?? CompanyStockUomName?.Trim() ?? "stock UOM";

    /// <summary>
    /// An item never moved to stock-based conversions: a stock-named row above 1 while the piece row is 1 (CS 12, PC 1).
    /// A stock-named row above 1 with no piece row at 1 is taken as the subd's own bundle (B1T1) and left alone.
    /// </summary>
    private string? RescaleRowName =>
        PieceEntryName is { } p && workingUomEntries[p].Conversion == 1m && !StockIsPiece
            ? workingUomEntries.Where(kv => kv.Value.IsActive && IsStockName(kv.Key) && kv.Value.Conversion is > 1m)
                               .Select(kv => kv.Key).FirstOrDefault()
            : null;

    private string? PieceEntryName =>
        workingUomEntries.Where(kv => kv.Value.IsActive && IsPieceName(kv.Key) && kv.Value.Conversion is > 0)
                         .Select(kv => kv.Key).FirstOrDefault();

    private string PieceDisplayName => PieceEntryName ?? BaseUomName;

    /// <summary>
    /// Pieces in one stock UOM: 1 when the stock UOM is a piece, otherwise 1 ÷ the piece row's conversion.
    /// Null when the item has no piece unit, so it can't be shown per piece.
    /// </summary>
    private decimal? PiecesPerStock
    {
        get
        {
            if (!StockUomRequired) return null;
            if (StockIsPiece) return 1m;
            return PieceEntryName is { } p && workingUomEntries[p].Conversion is decimal c && c > 0
                ? 1m / c
                : null;
        }
    }

    /// <summary>The stock-named row isn't 1 while the piece row is: the item's conversions are still per piece (not migrated).</summary>
    private bool StockNeedsRescale => RescaleRowName is not null;

    // ── Conversion view: per any unit (default the stock UOM) ───────────

    /// <summary>The unit conversions are shown in. Empty = the stock UOM (or as stored for a free item).</summary>
    private string conversionView = string.Empty;

    /// <summary>Every active unit with a conversion, plus the company stock UOM when the item has no row for it.</summary>
    private List<string> ConversionViewOptions
    {
        get
        {
            var options = workingUomEntries
                .Where(kv => kv.Value.IsActive && kv.Value.Conversion is > 0)
                .OrderByDescending(kv => string.Equals(kv.Key, StockEntryName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(kv => kv.Value.Conversion)
                .Select(kv => kv.Key)
                .ToList();
            if (StockRowImplicit) options.Insert(0, StockKey);
            return options;
        }
    }

    private string ConversionViewText(string key) => UnitText(key);

    /// <summary>The reference unit: the chosen one, else the stock UOM. Null for a free item shown as stored.</summary>
    private string? RefUnitName =>
        !string.IsNullOrWhiteSpace(conversionView) && ConversionViewOptions.Contains(conversionView, StringComparer.OrdinalIgnoreCase)
            ? conversionView
            : StockUomRequired ? StockRefKey : null;

    private decimal RefConversion => RefUnitName is { } r && BasisConversion(r) is decimal c && c > 0 ? c : 1m;

    /// <summary>Multiplier from stored (stock) conversions to what is shown: ÷ the reference unit.</summary>
    private decimal ViewFactor => 1m / RefConversion;

    private string ViewUnitName => RefUnitName is { } r ? UnitText(r) : "base";

    private bool IsRefUnit(string name) =>
        RefUnitName is { } r && string.Equals(r, name, StringComparison.OrdinalIgnoreCase);

    private Task OnConversionViewChanged(string value)
    {
        conversionView = value ?? string.Empty;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>The same relation read the other way: "1 CS = 24 PC" under PC, "Reference unit" under the reference.</summary>
    private string? OtherViewText(UomRow row)
    {
        if (IsRefUnit(row.Name)) return RefUnitName is null ? null : "Reference unit";
        if (row.Entry.Conversion is not decimal c || c <= 0 || RefUnitName is null) return null;
        return $"1 {RefUnitName} = {FormatNumber(RefConversion / c)} {row.Name}";
    }

    // ── Tabs (desktop: add form always shown, right side units or prices; phone: one at a time) ──

    private const string TabAdd = "add";
    private const string TabUnits = "units";
    private const string TabHistory = "history";
    private string activeTab = TabUnits;

    private async Task SetTabAsync(string tab)
    {
        activeTab = tab;
        if (tab == TabHistory && priceHistory.Count == 0 && !priceHistoryLoading) await LoadPriceHistoryAsync();
        await InvokeAsync(StateHasChanged);
    }

    // ── Search and filter (search shows from 10 units, filter when something is inactive) ──

    private const string FilterAll = "all";
    private const string FilterActive = "active";
    private const string FilterInactive = "inactive";
    private string listFilter = FilterAll;
    private string listQuery = string.Empty;

    private bool ShowSearch => workingUomEntries.Count >= 10;
    private bool ShowFilter => workingUomEntries.Values.Any(e => !e.IsActive);
    private int ActiveCount => workingUomEntries.Values.Count(e => e.IsActive);
    private int InactiveCount => workingUomEntries.Values.Count(e => !e.IsActive);

    private List<UomRow> VisibleUomRows
    {
        get
        {
            var filter = ShowFilter ? listFilter : FilterAll;
            var q = ShowSearch ? listQuery.Trim() : string.Empty;
            return UomRows.Where(r =>
                    (r.IsStock || filter == FilterAll
                               || (filter == FilterActive && r.Entry.IsActive)
                               || (filter == FilterInactive && !r.Entry.IsActive))
                    && (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    private Task OnListQueryChanged(string? value)
    {
        listQuery = value ?? string.Empty;
        return InvokeAsync(StateHasChanged);
    }

    private void SetListFilter(string filter) => listFilter = filter;

    /// <summary>"1 BX = 12 PC" under the add form.</summary>
    private string AddEquationUnit => string.IsNullOrWhiteSpace(GetSelectedUomName()) ? "—" : GetSelectedUomName();
    private string AddEquationValue => string.IsNullOrWhiteSpace(conversionInput) ? "—" : conversionInput.Trim();

    // ── Prices (read-only) ─────────────────────────────────────────────────

    private static decimal PriceFor(decimal stockPrice, decimal conversion) =>
        Math.Round(stockPrice * conversion, 2, MidpointRounding.AwayFromZero);

    private decimal? CurrentPriceFor(UomEntry entry) =>
        StockUomRequired && ApplicableStockPrice is decimal p && entry.Conversion is decimal c && c > 0
            ? PriceFor(p, c)
            : null;

    private bool priceHistoryLoading;
    private List<CompanyPriceHistoryRow> priceHistory = new();

    /// <summary>
    /// One column per price: the stock UOM first (row or not), then the other active units, largest first. Units at the
    /// same conversion share one column ("CS / Case"), since their prices are always the same.
    /// </summary>
    private List<(string Name, decimal Conversion, bool IsStock)> HistoryColumns
    {
        get
        {
            var units = workingUomEntries
                .Where(kv => kv.Value.IsActive && kv.Value.Conversion is > 0
                             && !string.Equals(kv.Key, StockEntryName, StringComparison.OrdinalIgnoreCase))
                .Select(kv => (Name: kv.Key, Conv: Math.Round(kv.Value.Conversion!.Value, 6)))
                .ToList();

            string Names(string first, decimal conv) =>
                string.Join(" / ", new[] { first }.Concat(units.Where(u => u.Conv == conv).Select(u => u.Name))
                                                  .Distinct(StringComparer.OrdinalIgnoreCase));

            var cols = new List<(string, decimal, bool)> { (Names(StockDisplayName, 1m), 1m, true) };
            cols.AddRange(units
                .Where(u => u.Conv != 1m)
                .GroupBy(u => u.Conv)
                .OrderByDescending(g => g.Key)
                .Select(g => (string.Join(" / ", g.Select(u => u.Name)), g.Key, false)));
            return cols;
        }
    }

    /// <summary>Change from the previous (older) price, in percent. Null when the price didn't change.</summary>
    private decimal? HistoryDelta(int index)
    {
        if (index + 1 >= priceHistory.Count) return null;
        var prev = priceHistory[index + 1].NewPrice;
        if (prev == 0) return null;
        var pct = (priceHistory[index].NewPrice - prev) / prev * 100m;
        return Math.Abs(pct) < 0.05m ? null : pct;
    }

    private int CurrentHistoryIndex => priceHistory.FindIndex(h => h.AppliedDate is not null);

    private Task TogglePriceHistoryAsync() => SetTabAsync(activeTab == TabHistory ? TabUnits : TabHistory);

    private async Task LoadPriceHistoryAsync()
    {
        if (!CompanyItemId.HasValue || !StockUomRequired) return;

        priceHistoryLoading = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            priceHistory = await PriceHistoryService.GetAsync(CompanyItemId.Value, PriceListLabel);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Price history failed: {ex.GetBaseException().Message}");
            priceHistory = new();
        }
        finally
        {
            priceHistoryLoading = false;
        }
    }

    // ── Add form ───────────────────────────────────────────────────────────

    private string[] DefaultOptions
    {
        get
        {
            var options = new List<string> { BaseUomName, "Case", "Box", "Pack" };
            if (StockUomRequired && !options.Any(o => IsStockName(o)))
                options.Insert(0, CompanyStockUomName!.Trim());
            return options.ToArray();
        }
    }

    /// <summary>
    /// The unit typed or picked in the Unit AutoComplete: one of <see cref="DefaultOptions"/> or any name typed in.
    /// The AutoComplete sends null while the typed text matches nothing yet, so this is never left null.
    /// </summary>
    private string selectedUomOption = string.Empty;
    private string SelectedUomOption
    {
        get => selectedUomOption;
        set
        {
            var v = value ?? string.Empty;
            if (selectedUomOption == v) return;
            selectedUomOption = v;
            validationErrors.Remove("uom");
            validationErrors.Remove("conversion");
            if (selectedUomOption != "__custom") customUom = string.Empty;
        }
    }

    private string customUom = string.Empty;
    private string CustomUom
    {
        get => customUom;
        set
        {
            if (customUom == value) return;
            customUom = value;
            validationErrors.Remove("uom");
            validationErrors.Remove("conversion");
        }
    }

    private string conversionInput = string.Empty;
    private string ConversionInput
    {
        get => conversionInput;
        set
        {
            conversionInput = value;
            validationErrors.Remove("conversion");
        }
    }

    /// <summary>The unit the entered conversion is counted in. Defaults to the piece unit, else the stock UOM.</summary>
    private string conversionBasedOnInput = string.Empty;

    private string ConversionBasisName =>
        !string.IsNullOrWhiteSpace(conversionBasedOnInput) && ConversionBasedOnOptions.Contains(conversionBasedOnInput, StringComparer.OrdinalIgnoreCase)
            ? conversionBasedOnInput
            : PieceEntryName ?? (StockUomRequired ? StockRefKey : null) ?? ConversionBasedOnOptions.FirstOrDefault() ?? string.Empty;

    private List<string> ConversionBasedOnOptions
    {
        get
        {
            var options = workingUomEntries
                .Where(kv => kv.Value.IsActive && kv.Value.Conversion is > 0
                             && !string.Equals(kv.Key, GetSelectedUomName(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kv => kv.Value.Conversion)
                .Select(kv => kv.Key)
                .ToList();

            // The company stock UOM can be the basis even without a row (it is always 1).
            if (StockRowImplicit && !IsStockName(GetSelectedUomName()))
                options.Add(StockKey);

            return options;
        }
    }

    /// <summary>Stored conversion of a basis unit: 1 for the company stock UOM (row or not), else its row.</summary>
    private decimal? BasisConversion(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name == StockKey) return 1m;
        if (workingUomEntries.TryGetValue(name, out var e) && e.IsActive && e.Conversion is > 0) return e.Conversion;
        return IsStockName(name) && !workingUomEntries.ContainsKey(name) ? 1m : null;
    }

    private string ConversionLabel
    {
        get
        {
            var uom = string.IsNullOrWhiteSpace(GetSelectedUomName()) ? "unit" : GetSelectedUomName();
            var basis = string.IsNullOrWhiteSpace(ConversionBasisName) ? "units" : UnitText(ConversionBasisName);
            return $"How many {basis} in one {uom}?";
        }
    }

    private bool IsSelectedStockUom => IsStockName(GetSelectedUomName());

    // "__custom" + CustomUom is from the old Select with an "Other…" option; kept so older saved drafts still load.
    private string GetSelectedUomName() =>
        selectedUomOption == "__custom" ? (customUom ?? string.Empty).Trim() : (selectedUomOption ?? string.Empty).Trim();

    private async Task OnUomSelectChanged(string? value)
    {
        SelectedUomOption = value ?? string.Empty;
        await PersistDraftAsync();
    }

    private async Task OnConversionBasedOnChanged(string value)
    {
        conversionBasedOnInput = value;
        await PersistDraftAsync();
    }

    private async Task AddUomEntryAsync()
    {
        var uomName = GetSelectedUomName();
        decimal? conversion;

        if (IsStockName(uomName))
        {
            conversion = 1m;
        }
        else if (string.IsNullOrWhiteSpace(conversionInput))
        {
            conversion = null;
        }
        else if (decimal.TryParse(conversionInput, NumberStyles.Number, CultureInfo.InvariantCulture, out var count) && count > 0
                 && BasisConversion(ConversionBasisName) is decimal basisConv)
        {
            conversion = SnapConversion(count * basisConv);
        }
        else
        {
            conversion = null;   // the validator reports the bad input
        }

        validationErrors = AddUomValidator.ValidateUomEntry(
            uomName, IsStockName(uomName) ? "1" : conversionInput, conversion, workingUomEntries,
            conversionRequired: StockUomRequired);

        // The stock UOM is always 1; another unit already at 1 (e.g. PC = 1 PK) is that unit's problem, flagged in the review.
        if (IsStockName(uomName)) validationErrors.Remove("conversion");

        if (validationErrors.Any()) return;

        var prior = workingUomEntries.TryGetValue(uomName, out var priorEntry) ? priorEntry : null;

        workingUomEntries[uomName] = new UomEntry
        {
            ItemsUomId = prior?.ItemsUomId,
            Conversion = conversion,
            ConversionBasedOn = IsStockName(uomName) ? null : UnitText(ConversionBasisName),
            IsActive = true
        };

        selectedUomOption = string.Empty;
        customUom = string.Empty;
        conversionInput = string.Empty;
        listQuery = string.Empty;
        listFilter = FilterAll;
        if (activeTab != TabUnits) activeTab = TabUnits;   // phone: show the list with the new unit

        RefreshStockFlags();
        RefreshFinalErrors();
        await PersistDraftAsync();
        pendingJumpRow = uomName;
        shouldFocusUomSelect = true;
        await InvokeAsync(StateHasChanged);
    }

    // ── Table ──────────────────────────────────────────────────────────────

    private sealed class UomRow
    {
        public required string Name { get; init; }
        public required UomEntry Entry { get; init; }
        public required bool IsStock { get; init; }
        /// <summary>The company stock UOM shown without a saved row (always 1).</summary>
        public bool IsVirtual { get; init; }
        /// <summary>The unit conversions are shown in; its own conversion is 1 and isn't edited here.</summary>
        public bool IsReference { get; init; }
        public int Number { get; set; }
        public required Func<decimal> Factor { get; init; }
        public required Func<UomEntry, decimal?> PriceOf { get; init; }

        public string UnitLabel { get; set; } = string.Empty;

        /// <summary>Shown in the selected view; stored per stock UOM. The stock UOM stays at 1.</summary>
        public decimal? Conversion
        {
            get => Entry.Conversion is decimal c ? Math.Round(c * Factor(), 6, MidpointRounding.AwayFromZero) : null;
            set
            {
                if (IsStock) { Entry.Conversion = 1m; return; }
                if (IsReference) return;
                Entry.Conversion = value is decimal v && v > 0
                    ? Math.Round(v / Factor(), 10, MidpointRounding.AwayFromZero)
                    : value;
            }
        }

        public decimal? Price => PriceOf(Entry);
    }

    private List<UomRow> UomRows
    {
        get
        {
            var rows = workingUomEntries
                .OrderByDescending(x => string.Equals(x.Key, StockEntryName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.Value.IsActive)
                .ThenByDescending(x => x.Value.Conversion ?? decimal.MinValue)
                .Select(kv => new UomRow
                {
                    Name = kv.Key,
                    Entry = kv.Value,
                    IsStock = string.Equals(kv.Key, StockEntryName, StringComparison.OrdinalIgnoreCase),
                    IsReference = kv.Value.IsActive && IsRefUnit(kv.Key),
                    Factor = () => ViewFactor,
                    PriceOf = CurrentPriceFor,
                    UnitLabel = kv.Key
                })
                .ToList();

            // The stock UOM is listed on top even without a row: it is always 1 (nothing is saved for it).
            // Not when a B1T1 bundle of the same name is listed: two "PK" cards would only confuse.
            if (ShowVirtualStockCard)
            {
                var name = StockKey;
                rows.Insert(0, new UomRow
                {
                    Name = name,
                    Entry = new UomEntry { Conversion = 1m, IsActive = true },
                    IsStock = true,
                    IsVirtual = true,
                    IsReference = IsRefUnit(name),
                    Factor = () => ViewFactor,
                    PriceOf = CurrentPriceFor,
                    UnitLabel = CompanyStockUomName!.Trim()
                });
            }

            for (var i = 0; i < rows.Count; i++) rows[i].Number = i + 1;
            return rows;
        }
    }

    // ── Unit cards ─────────────────────────────────────────────────────────

    private static string CardClass(UomRow r, ISet<string> flagged) =>
        string.Join(' ', new[]
        {
            "au-u",
            r.IsStock && r.Entry.IsActive ? "stock" : null,
            r.IsVirtual ? "virtual" : null,
            !r.Entry.IsActive ? "off" : null,
            flagged.Contains(r.Name) ? "flag" : null
        }.Where(c => c is not null));

    /// <summary>"16 PK" — the unit's conversion counted in the "Show per" unit.</summary>
    private string CardSize(UomRow r)
    {
        if (r.Entry.Conversion is not decimal c || c <= 0) return "No conversion";
        if (IsRefUnit(r.Name)) return $"1 {ViewUnitName}";
        return $"{FormatNumber(c / RefConversion)} {ViewUnitName}";
    }

    /// <summary>"1 PK = 0.0625 Case" — the same thing read the other way.</summary>
    private string? CardSizeHint(UomRow r)
    {
        if (r.Entry.Conversion is not decimal c || c <= 0) return "can't be priced";
        if (IsRefUnit(r.Name)) return RefUnitName is null ? null : $"shown per {ViewUnitName}";
        return RefUnitName is null ? null : $"1 {ViewUnitName} = {FormatNumber(RefConversion / c)} {CardName(r)}";
    }

    private string CardName(UomRow r) => r.IsVirtual ? r.UnitLabel : r.Name;

    /// <summary>Show the company stock UOM card only when no stock-named unit is listed (a B1T1 bundle already names it).</summary>
    private bool ShowVirtualStockCard =>
        StockRowImplicit && !workingUomEntries.Any(kv => kv.Value.IsActive && IsStockName(kv.Key));

    private string CardPrice(UomRow r) =>
        !StockUomRequired ? "Free" : r.Price is decimal p ? $"₱{p:N2}" : "-";

    /// <summary>Units the inline review calls out (shown with a left mark on their card).</summary>
    private static HashSet<string> FlaggedRows(IEnumerable<UomMessage> messages) =>
        messages.Where(m => m.Severity != UomMessageSeverity.Note && m.JumpRow is not null)
                .Select(m => m.JumpRow!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NoteClass(UomMessageSeverity s) => s switch
    {
        UomMessageSeverity.Error => "au-n err",
        UomMessageSeverity.Warning => "au-n warn",
        _ => "au-n info"
    };

    private static string NoteIcon(UomMessageSeverity s) => s switch
    {
        UomMessageSeverity.Error => "ti ti-alert-circle",
        UomMessageSeverity.Warning => "ti ti-alert-triangle",
        _ => "ti ti-info-circle"
    };

    // ── Inline edit (click a card) ─────────────────────────────────────────

    private string? editingName;
    private string editNameInput = string.Empty;
    private string editSizeInput = string.Empty;
    private string editBasis = string.Empty;
    private Dictionary<string, string> editErrors = new();

    /// <summary>Units the conversion can be counted in while editing: every other active unit, plus the stock UOM.</summary>
    private List<string> EditBasisOptions
    {
        get
        {
            var options = workingUomEntries
                .Where(kv => kv.Value.IsActive && kv.Value.Conversion is > 0
                             && !string.Equals(kv.Key, editingName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kv => string.Equals(kv.Key, StockEntryName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(kv => kv.Value.Conversion)
                .Select(kv => kv.Key)
                .ToList();

            // The company stock UOM (1) can always be the basis — also while editing the stock row itself (B1T1: Pack = 2 PK).
            var stockRowIsOther = StockEntryName is { } s && !string.Equals(s, editingName, StringComparison.OrdinalIgnoreCase);
            if (StockUomRequired && !stockRowIsOther) options.Insert(0, StockKey);
            return options;
        }
    }

    /// <summary>Stored conversion from what was typed while editing: size × the basis unit's conversion.</summary>
    private decimal? EditConversion(decimal size, string basisKey)
    {
        if (size <= 0 || BasisConversion(basisKey) is not decimal b) return null;
        return SnapConversion(size * b);
    }

    /// <summary>What to show in the size box for a stored conversion and basis (the reverse of <see cref="EditConversion"/>).</summary>
    private string EditSizeText(decimal conversion, string basisKey) =>
        conversion > 0 && BasisConversion(basisKey) is decimal b ? FormatNumber(conversion / b) : string.Empty;

    /// <summary>
    /// Rounds away the drift that 6-decimal display values leave behind (0.166667 × 12 = 2.000004 → 2,
    /// 0.0833334 → 1/12), then keeps 10 decimals like ItemsUom.ConversionToBase.
    /// </summary>
    /// <summary>Snaps every unit's conversion (see <see cref="SnapConversion"/>), so exact checks like "== 1" hold.</summary>
    private void SnapAllConversions()
    {
        foreach (var e in workingUomEntries.Values)
            if (e.Conversion is decimal c && c > 0) e.Conversion = SnapConversion(c);
    }

    /// <remarks>Internal so the Map Item page can mark the stock UOM with the same rule.</remarks>
    internal static decimal SnapConversion(decimal value)
    {
        if (value <= 0) return value;

        var whole = Math.Round(value);
        if (whole > 0 && Math.Abs(value - whole) <= 0.00001m * whole) return whole;

        if (value < 1m)
        {
            var per = Math.Round(1m / value);   // e.g. 12 for 0.0833333
            if (per > 1 && Math.Abs(1m / value - per) <= 0.0001m * per)
                return Math.Round(1m / per, 10, MidpointRounding.AwayFromZero);
        }

        return Math.Round(value, 10, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The exact stored conversion behind the size box, and the text the box was filled with. As long as the box
    /// still shows that text, saving keeps the exact value instead of re-reading the rounded text (no drift).
    /// </summary>
    private decimal? editExactConversion;
    private string editSizeShown = string.Empty;

    private void ShowEditSize(decimal? conversion, string basisKey)
    {
        editExactConversion = conversion;
        editSizeInput = editSizeShown = conversion is decimal c ? EditSizeText(c, basisKey) : string.Empty;
    }

    /// <summary>The conversion the edit row currently means: the exact one if untouched, else what was typed.</summary>
    private decimal? CurrentEditConversion =>
        editSizeInput.Trim() == editSizeShown && editExactConversion is decimal exact ? exact
        : decimal.TryParse(editSizeInput, NumberStyles.Number, CultureInfo.InvariantCulture, out var size)
            ? EditConversion(size, editBasis)
            : null;

    /// <summary>"1 PC = 0.083333 PK · ₱11.35" under the edit row, so the number can be read in the stock UOM whatever the basis.</summary>
    private string? EditPreview
    {
        get
        {
            if (CurrentEditConversion is not decimal c || c <= 0) return null;
            var name = string.IsNullOrWhiteSpace(editNameInput) ? editingName : editNameInput.Trim();
            var text = $"1 {name} = {FormatNumber(c)} {UnitText(StockKey)}";
            if (PiecesPerStock is decimal pps && !IsPieceName(name) && PieceEntryName is { } piece
                && !string.Equals(piece, editingName, StringComparison.OrdinalIgnoreCase))
                text += $" = {FormatNumber(c * pps)} {piece}";
            if (StockUomRequired && ApplicableStockPrice is decimal p) text += $" · ₱{PriceFor(p, c):N2}";
            return text;
        }
    }

    private async Task OnEditBasisChanged(string value)
    {
        // Keep the same size when switching basis: re-express the amount in the new unit (exactly, not from the rounded text).
        if (CurrentEditConversion is decimal conv) ShowEditSize(conv, value);
        editBasis = value;
        await InvokeAsync(StateHasChanged);
    }

    private void BeginEdit(string name)
    {
        if (!workingUomEntries.TryGetValue(name, out var entry) || !entry.IsActive) return;

        editingName = name;
        editNameInput = name;
        editErrors = new();

        var options = EditBasisOptions;
        editBasis = RefUnitName is { } r && options.Contains(r, StringComparer.OrdinalIgnoreCase) ? r
                  : options.Contains(StockKey) ? StockKey
                  : options.FirstOrDefault() ?? string.Empty;

        ShowEditSize(entry.Conversion, editBasis);
    }

    private void CancelEdit()
    {
        editingName = null;
        editErrors = new();
    }

    private async Task SaveEditAsync()
    {
        if (editingName is not { } oldName || !workingUomEntries.TryGetValue(oldName, out var entry)) return;

        editErrors = new();
        var newName = editNameInput.Trim();
        if (string.IsNullOrWhiteSpace(newName)) editErrors["name"] = "Enter a unit name.";

        decimal? conversion = null;
        if (editSizeInput.Trim() == editSizeShown && editExactConversion is decimal exact)
            conversion = SnapConversion(exact);   // untouched: keep the stored value (cleaned of old drift)
        else if (!decimal.TryParse(editSizeInput, NumberStyles.Number, CultureInfo.InvariantCulture, out var size) || size <= 0)
            editErrors["size"] = "Enter a number greater than 0.";
        else if (EditConversion(size, editBasis) is decimal worked)
            conversion = worked;
        else
            editErrors["size"] = "Pick what the conversion is counted in.";

        if (conversion is decimal conv)
        {
            foreach (var kv in AddUomValidator.ValidateUomConversion(oldName, conv, workingUomEntries, isStock: false))
                editErrors["size"] = kv.Value;
        }

        if (!editErrors.ContainsKey("name") && !string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var kv in AddUomValidator.ValidateUomRename(oldName, newName, workingUomEntries))
                editErrors["name"] = kv.Value;
        }

        if (editErrors.Any())
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        entry.Conversion = conversion;
        entry.ConversionBasedOn = UnitText(editBasis);
        validationErrors.Remove($"conversion_{oldName}");

        editingName = null;
        if (!string.Equals(newName, oldName, StringComparison.Ordinal))
            await HandleUomRenamedAsync(oldName, newName);

        RefreshStockFlags();
        RefreshFinalErrors();
        await PersistDraftAsync();
        pendingJumpRow = newName;
        await InvokeAsync(StateHasChanged);
    }

    // ── Add form preview ───────────────────────────────────────────────────

    private decimal? AddPreviewConversion =>
        !IsSelectedStockUom
        && decimal.TryParse(conversionInput, NumberStyles.Number, CultureInfo.InvariantCulture, out var c) && c > 0
        && BasisConversion(ConversionBasisName) is decimal b
            ? c * b
            : null;

    private bool CanAdd =>
        !string.IsNullOrWhiteSpace(GetSelectedUomName()) && (IsSelectedStockUom || AddPreviewConversion is not null || !StockUomRequired);

    private string AddButtonTitle => string.IsNullOrWhiteSpace(GetSelectedUomName()) ? "Add unit" : $"Add {GetSelectedUomName()}";

    private async Task HandleUomRenamedAsync(string oldName, string? newNameRaw)
    {
        var newName = (newNameRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase)) return;
        if (!workingUomEntries.TryGetValue(oldName, out var entry)) return;

        var renameErrors = AddUomValidator.ValidateUomRename(oldName, newName, workingUomEntries);

        if (renameErrors.Any())
        {
            foreach (var kv in renameErrors) validationErrors[kv.Key] = kv.Value;
            await InvokeAsync(StateHasChanged);
            return;
        }

        validationErrors.Remove($"uomname_{oldName}");

        if (workingUomEntries.TryGetValue(newName, out var conflicting) && !conflicting.IsActive)
            workingUomEntries.Remove(newName);

        workingUomEntries.Remove(oldName);
        workingUomEntries[newName] = entry;

        foreach (var other in workingUomEntries.Values)
            if (string.Equals(other.ConversionBasedOn, oldName, StringComparison.OrdinalIgnoreCase))
                other.ConversionBasedOn = newName;

        if (string.Equals(conversionBasedOnInput, oldName, StringComparison.OrdinalIgnoreCase))
            conversionBasedOnInput = newName;

        if (validationErrors.Remove($"conversion_{oldName}", out var convErr))
            validationErrors[$"conversion_{newName}"] = convErr;

        RefreshStockFlags();
        RefreshFinalErrors();
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleRowConversionChangedAsync(string uomName)
    {
        if (!workingUomEntries.TryGetValue(uomName, out var entry)) return;

        validationErrors.Remove($"conversion_{uomName}");
        foreach (var kv in AddUomValidator.ValidateUomConversion(uomName, entry.Conversion, workingUomEntries,
                     isStock: false))
            validationErrors[kv.Key] = kv.Value;

        RefreshFinalErrors();
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task RemoveUomEntry(string uomName)
    {
        // The stock UOM row can be removed too: the item doesn't need it (it is always 1).
        if (!workingUomEntries.TryGetValue(uomName, out var entry)) return;

        if (string.Equals(editingName, uomName, StringComparison.OrdinalIgnoreCase)) editingName = null;

        if (IsUomInUse(uomName))
        {
            entry.IsActive = false;
            entry.IsStockUom = false;
        }
        else
        {
            workingUomEntries.Remove(uomName);
        }

        RefreshStockFlags();
        RefreshFinalErrors();
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    private bool showReactivateConflictModal;
    private string reactivateConflictMessage = string.Empty;

    private async Task ReactivateUomEntry(string uomName)
    {
        if (!workingUomEntries.TryGetValue(uomName, out var entry)) return;

        var conflict = workingUomEntries.FirstOrDefault(kv =>
            kv.Value.IsActive &&
            !string.Equals(kv.Key, uomName, StringComparison.OrdinalIgnoreCase) &&
            kv.Value.Conversion == entry.Conversion);

        if (!string.IsNullOrEmpty(conflict.Key))
        {
            reactivateConflictMessage =
                $"Cannot reactivate '{uomName}': its conversion is already used by '{conflict.Key}'. Update the conversion first.";
            showReactivateConflictModal = true;
            await InvokeAsync(StateHasChanged);
            return;
        }

        entry.IsActive = true;
        RefreshStockFlags();
        RefreshFinalErrors();
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    private void DismissReactivateConflictModal()
    {
        showReactivateConflictModal = false;
        reactivateConflictMessage = string.Empty;
    }

    /// <summary>
    /// For items whose conversions are still per piece: divides every UOM by the stock row's value, so the stock UOM becomes 1
    /// (CS 12 → 1, PC 1 → 0.0833333333). Saved only when the user clicks Save.
    /// </summary>
    private async Task RescaleToStockAsync()
    {
        if (RescaleRowName is not { } row || workingUomEntries[row].Conversion is not decimal factor || factor <= 0) return;

        foreach (var e in workingUomEntries.Values.Where(e => e.Conversion is > 0))
            e.Conversion = SnapConversion(e.Conversion!.Value / factor);

        workingUomEntries[row].Conversion = 1m;
        RefreshStockFlags();          // the stock-named row is now 1, so it's the stock UOM again
        RefreshFinalErrors();
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    // ── Stock row housekeeping ─────────────────────────────────────────────

    /// <summary>
    /// Marks which entry is the company stock UOM (by name and UOM synonyms). This is a marker for the page only:
    /// nothing about it is saved, and the item doesn't need a row for the stock UOM.
    /// </summary>
    private void RefreshStockFlags()
    {
        var stockName = StockEntryName;
        foreach (var (name, e) in workingUomEntries)
            e.IsStockUom = stockName is not null && string.Equals(name, stockName, StringComparison.OrdinalIgnoreCase);
    }

    // ── Open / save / cancel ───────────────────────────────────────────────

    private bool wasShown;
    private bool shouldFocusUomSelect;
    private string? pendingJumpRow;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Scroll to (and flash) a unit just added, once it is on the page.
        if (pendingJumpRow is { } row)
        {
            pendingJumpRow = null;
            await InvokeReviewJsAsync("jumpTo", UomRowDomId(row), null);
        }

        // Window-wide keys (Enter / Esc / Ctrl+S) only while the window is open, so they don't touch the page behind it.
        if (!ShowAddUomModal)
        {
            if (shortcutsOn)
            {
                shortcutsOn = false;
                await InvokeKeysJsAsync("unshortcuts");
            }
            return;
        }

        selfRef ??= DotNetObjectReference.Create(this);
        if (!shortcutsOn)
        {
            shortcutsOn = true;
            await InvokeKeysJsAsync("shortcuts", selfRef);
        }

        // Enter-key flow of the add form (conversion → "counted in" → add). The script wires each form element only once.
        await InvokeKeysJsAsync("wire", AddFormId, selfRef);

        // Focus moves wait for the render that shows the field (the conversion input is hidden for the stock UOM).
        if (pendingFocusId is { } id)
        {
            pendingFocusId = null;
            await InvokeKeysJsAsync("focusIn", id);
        }
        else if (shouldFocusUomSelect && unitAutoComplete is not null)
        {
            shouldFocusUomSelect = false;
            try { await unitAutoComplete.GetInputRef().FocusAsync(); }
            catch (JSException) { }
            catch (InvalidOperationException) { }   // input not rendered yet: tried again on the next render
        }
    }

    // ── Keyboard flow of the add form ──────────────────────────────────────

    private const string AddFormId = "uom-add-form";
    private const string AddConvId = "uom-add-conv";
    private const string AddBasisId = "uom-add-basis";
    private const string AddButtonId = "uom-add-btn";

    private AutoComplete<string>? unitAutoComplete;
    private DotNetObjectReference<AddUom>? selfRef;
    private IJSObjectReference? keysJs;
    private string? pendingFocusId;
    private bool shortcutsOn;

    /// <summary>Ctrl+S with no unit being edited: the same as Save changes.</summary>
    [JSInvokable]
    public Task SaveAllFromKeyboard() => InvokeAsync(async () =>
    {
        if (showClearDraftConfirmModal || showReactivateConflictModal || editingName is not null) return;
        await AddAsync();
        StateHasChanged();
    });

    /// <summary>Esc with no unit being edited: closes an open confirm first, else the same as Cancel.</summary>
    [JSInvokable]
    public Task CancelFromKeyboard() => InvokeAsync(async () =>
    {
        if (showClearDraftConfirmModal) { CancelClearDraftConfirm(); StateHasChanged(); return; }
        if (showReactivateConflictModal) { DismissReactivateConflictModal(); StateHasChanged(); return; }
        await CancelAsync();
    });

    /// <summary>Enter in the Unit field with a unit chosen: on to the conversion (or the Add button for the stock UOM, which has none).</summary>
    private Task OnUnitConfirmedAsync()
    {
        if (string.IsNullOrWhiteSpace(GetSelectedUomName())) return Task.CompletedTask;

        pendingFocusId = IsSelectedStockUom ? AddButtonId : AddConvId;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Enter on the "counted in" select after its list was opened. Runs the same add as the button; when the add is
    /// refused, focus goes to the field showing the error so it can be fixed straight away.
    /// </summary>
    [JSInvokable]
    public Task AddUnitFromKeyboard() => InvokeAsync(async () =>
    {
        await AddUomEntryAsync();

        // A call from JS doesn't re-render on its own the way a button click does, so the errors would stay hidden.
        if (validationErrors.ContainsKey("uom") || validationErrors.ContainsKey("customUom"))
            shouldFocusUomSelect = true;
        else if (validationErrors.ContainsKey("conversion"))
            pendingFocusId = AddConvId;

        StateHasChanged();
    });

    /// <summary>Enter in the unit being edited. Returns true when it was saved (false leaves the errors on screen).</summary>
    [JSInvokable]
    public async Task<bool> SaveEditFromKeyboard()
    {
        var saved = false;
        await InvokeAsync(async () =>
        {
            if (editingName is null) return;
            await SaveEditAsync();
            saved = editingName is null;
            StateHasChanged();
        });
        return saved;
    }

    /// <summary>Esc in the unit being edited.</summary>
    [JSInvokable]
    public Task CancelEditFromKeyboard() => InvokeAsync(() =>
    {
        CancelEdit();
        StateHasChanged();
    });

    private async Task InvokeKeysJsAsync(string function, params object?[] args)
    {
        try
        {
            keysJs ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/uomaddkeys.js");
            await keysJs.InvokeVoidAsync(function, args);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"uomaddkeys.js '{function}' failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Leaving the page with the window open: take the window-wide keys off (best effort, the circuit may be gone).
        if (shortcutsOn && keysJs is not null)
            _ = keysJs.InvokeVoidAsync("unshortcuts").AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
        selfRef?.Dispose();
    }
    private bool showClearDraftConfirmModal;
    private AddUomModalDraftState? loadedDraft;
    private IJSObjectReference? jsModule;

    protected override async Task OnParametersSetAsync()
    {
        if (ShowAddUomModal && !wasShown)
        {
            loadedDraft = await LoadDraftAsync();

            // A draft started from different saved units is stale (e.g. the item was saved since): start fresh.
            if (loadedDraft is not null && loadedDraft.BaseFingerprint != ExistingFingerprint())
            {
                loadedDraft = null;
                await ClearDraftStorageAsync();
            }

            if (loadedDraft is not null)
            {
                ApplyDraftState(loadedDraft);
            }
            else
            {
                CloneExistingEntries();
                selectedUomOption = string.Empty;
                customUom = string.Empty;
                conversionInput = string.Empty;
                conversionBasedOnInput = string.Empty;
                validationErrors.Clear();
            }

            SnapAllConversions();   // older saves can hold 0.9999996 for 1: treat it as 1 (saved clean on Save changes)
            RefreshStockFlags();
            ApplySuggestedUom();
            conversionView = PieceEntryName ?? string.Empty;   // show per piece when there is one, else per stock UOM
            editingName = null;
            activeTab = TabUnits;
            listFilter = FilterAll;
            listQuery = string.Empty;
            priceHistory = new();
            hasTriedSave = false;
            showClearDraftConfirmModal = false;
            shouldFocusUomSelect = true;
            wasShown = true;
            await PersistDraftAsync();
        }
        else if (!ShowAddUomModal)
        {
            wasShown = false;
        }
    }

    // Puts the suggested unit in the Unit field: the matching standard option's spelling, else the name as typed.
    private void ApplySuggestedUom()
    {
        var name = SuggestedUomName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || !string.IsNullOrEmpty(selectedUomOption)) return;
        if (workingUomEntries.ContainsKey(name)) return;

        SelectedUomOption = DefaultOptions.FirstOrDefault(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }

    private async Task AddAsync()
    {
        hasTriedSave = true;
        validationErrors = BuildFinalErrors();

        if (validationErrors.Any())
        {
            await RevealFirstErrorAsync();
            return;
        }

        // Prices are reference values only (company price × conversion), filled in so summaries can show them.
        foreach (var e in workingUomEntries.Values)
        {
            e.Price = CurrentPriceFor(e);
            e.IsAutoCalculated = true;
        }
        RefreshStockFlags();

        if (!string.IsNullOrWhiteSpace(DraftStorageKey))
        {
            jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
            await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", DraftStorageKey);
        }

        await OnAdd.InvokeAsync(new Dictionary<string, UomEntry>(workingUomEntries, StringComparer.OrdinalIgnoreCase));
    }

    private async Task CancelAsync()
    {
        showClearDraftConfirmModal = false;
        await OnCancel.InvokeAsync();
    }

    private string GetFieldError(string fieldKey) =>
        validationErrors.TryGetValue(fieldKey, out var message) ? message : string.Empty;

    private void CloneExistingEntries()
    {
        workingUomEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ExistingUomEntries)
        {
            workingUomEntries[entry.Key.Trim()] = new UomEntry
            {
                ItemsUomId = entry.Value.ItemsUomId,
                Conversion = entry.Value.Conversion,
                ConversionBasedOn = entry.Value.ConversionBasedOn,
                IsActive = entry.Value.IsActive,
                IsStockUom = entry.Value.IsStockUom
            };
        }
    }

    private void ApplyDraftState(AddUomModalDraftState draft)
    {
        workingUomEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in draft.WorkingUomEntries)
            workingUomEntries[entry.Key.Trim()] = entry.Value;

        // An older draft may hold "__custom" + a typed name: the AutoComplete takes the name directly now.
        selectedUomOption = draft.SelectedUomOption == "__custom"
            ? (draft.CustomUom ?? string.Empty).Trim()
            : draft.SelectedUomOption ?? string.Empty;
        customUom = string.Empty;
        conversionInput = draft.ConversionInput ?? string.Empty;
        validationErrors.Clear();
    }

    private async Task PersistDraftAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftStorageKey)) return;

        jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");

        var draftState = new AddUomModalDraftState
        {
            WorkingUomEntries = new Dictionary<string, UomEntry>(workingUomEntries, StringComparer.OrdinalIgnoreCase),
            SelectedUomOption = selectedUomOption,
            CustomUom = customUom,
            ConversionInput = conversionInput,
            PriceInput = string.Empty,
            BaseFingerprint = ExistingFingerprint()
        };

        await jsModule.InvokeVoidAsync("saveSalesInvoiceDraft", DraftStorageKey, JsonSerializer.Serialize(draftState));
    }

    private async Task<AddUomModalDraftState?> LoadDraftAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftStorageKey)) return null;

        jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");

        string? draftJson;
        try
        {
            draftJson = await jsModule.InvokeAsync<string?>("loadSalesInvoiceDraft", DraftStorageKey);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(draftJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<AddUomModalDraftState>(draftJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The saved units passed in (name, conversion, active), in a stable order.</summary>
    private string ExistingFingerprint() =>
        string.Join("|", ExistingUomEntries
            .OrderBy(kv => kv.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key.Trim().ToUpperInvariant()}={kv.Value.Conversion?.ToString(CultureInfo.InvariantCulture)}:{(kv.Value.IsActive ? 1 : 0)}"));

    private async Task ClearDraftStorageAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftStorageKey)) return;
        try
        {
            jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
            await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", DraftStorageKey);
        }
        catch (JSDisconnectedException) { }
    }

    private void ShowClearDraftConfirmModal() => showClearDraftConfirmModal = true;

    private async Task ConfirmClearDraftAsync()
    {
        showClearDraftConfirmModal = false;

        if (!string.IsNullOrWhiteSpace(DraftStorageKey))
        {
            jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
            await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", DraftStorageKey);
        }

        CloneExistingEntries();
        RefreshStockFlags();
        selectedUomOption = string.Empty;
        customUom = string.Empty;
        conversionInput = string.Empty;
        conversionBasedOnInput = string.Empty;
        validationErrors.Clear();
        hasTriedSave = false;
        editingName = null;
        shouldFocusUomSelect = true;

        await InvokeAsync(StateHasChanged);
    }

    private void CancelClearDraftConfirm() => showClearDraftConfirmModal = false;

    private bool IsUomInUse(string? uomName) =>
        !string.IsNullOrWhiteSpace(uomName) && InUseUomNames.Contains(uomName.Trim());

    private static string FormatNumber(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero).ToString("0.######", CultureInfo.InvariantCulture);

    private string getModalSubtitle()
    {
        if (!string.IsNullOrWhiteSpace(ItemCode) && !string.IsNullOrWhiteSpace(ItemName)) return $"Item: {ItemCode} - {ItemName}";
        if (!string.IsNullOrWhiteSpace(ItemCode)) return $"Item: {ItemCode}";
        if (!string.IsNullOrWhiteSpace(ItemName)) return $"Item: {ItemName}";
        return string.Empty;
    }
}