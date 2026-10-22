using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Features.User.MapItem.Validators;

namespace STTproject.Features.User.MapItem.Components.Modals;

public partial class AddUom
{
    private const string ReviewPanelId = "uom-review-panel";
    private const string RefField = "ref";

    private static readonly HashSet<string> AddFormKeys =
        new(StringComparer.Ordinal) { "uom", "customUom", "conversion", "price" };

    private bool hasTriedSave;
    private UomReviewPanel? reviewPanel;
    private IJSObjectReference? reviewJs;
    private Dictionary<string, UomMessageSeverity> cellStates = new(StringComparer.Ordinal);

    private IReadOnlyList<UomMessage> BuildMessages()
    {
        var rows = UomRows;
        var rowOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            rowOrder[rows[i].Name] = i;
        }

        var messages = new List<UomMessage>();
        var states = new Dictionary<string, UomMessageSeverity>(StringComparer.Ordinal);

        void Mark(string? row, string? field, UomMessageSeverity severity)
        {
            if (row is null || field is null || severity == UomMessageSeverity.Note) return;
            var key = CellKey(row, field);
            if (!states.TryGetValue(key, out var existing) || severity < existing)
            {
                states[key] = severity;
            }
        }

        void Add(UomMessage message)
        {
            // A message pointing at a row that no longer exists becomes table-level.
            if (message.RowName is not null && !rowOrder.ContainsKey(message.RowName))
            {
                message = message with { RowName = null, Field = null, TargetRow = null };
            }

            messages.Add(message);
            Mark(message.RowName, message.Field, message.Severity);
        }

        // Errors
        foreach (var (key, text) in validationErrors)
        {
            if (AddFormKeys.Contains(key) || string.IsNullOrWhiteSpace(text)) continue;
            Add(ErrorMessage(key, text));
        }

        // Warnings and notes
        if (!string.IsNullOrWhiteSpace(ReferenceNote))
        {
            Add(new UomMessage("ref-note", UomMessageSeverity.Warning, ReferenceNote!, null, RefField));
        }

        var stock = CurrentStockEntry;
        var stockName = CurrentStockName;

        if (stock is null)
        {
            if (StockUomRequired && rows.Any(r => r.Entry.IsActive) && !validationErrors.ContainsKey("stockuom"))
            {
                Add(new UomMessage("stock-missing", UomMessageSeverity.Warning,
                    "Select which unit matches the company's stock-price unit.", null, StockColumn));
            }
        }
        else
        {
            if (StockVariancePct is decimal pct && pct != 0
                && stock.Price is decimal mapped && ApplicableStockPrice is decimal company)
            {
                var companyUnit = string.IsNullOrWhiteSpace(CompanyStockUomName) ? "stock UOM" : CompanyStockUomName;

                if (StockVarianceIsLarge)
                {
                    Add(new UomMessage("stock-variance", UomMessageSeverity.Warning,
                        $"{mapped:N2} is far from the company price {company:N2}. Is it the same unit as “{companyUnit}”?",
                        stockName, nameof(UomRow.Price)));
                }
                else
                {
                    Add(new UomMessage("stock-variance", UomMessageSeverity.Note,
                        $"{pct:+0.#;-0.#}% vs the company price {company:N2}. Sub-distributor markup is expected.",
                        stockName, nameof(UomRow.Price)));
                }
            }

            if (stockName is not null && IsSuggested(stockName) && !stockUomChosenByUser
                && !string.IsNullOrWhiteSpace(suggestionReason))
            {
                Add(new UomMessage("stock-suggested", UomMessageSeverity.Note,
                    $"Suggested as the stock unit: {suggestionReason}.", stockName, StockColumn));
            }

            var gaps = rows
                .Select(r => (Row: r, Gap: PriceGap(r)))
                .Where(x => x.Gap is not null)
                .Select(x => (x.Row, Gap: x.Gap!.Value))
                .ToList();

            if (gaps.Count > 3)
            {
                Add(new UomMessage("price-gaps", UomMessageSeverity.Warning,
                    $"{gaps.Count} prices differ from the {stockName} price × conversion.",
                    null, nameof(UomRow.Price), gaps[0].Row.Name));

                foreach (var g in gaps)
                {
                    Mark(g.Row.Name, nameof(UomRow.Price), UomMessageSeverity.Warning);
                }
            }
            else
            {
                foreach (var g in gaps)
                {
                    var sign = g.Gap.Diff > 0 ? "+" : "−";
                    Add(new UomMessage($"price-gap:{g.Row.Name}", UomMessageSeverity.Warning,
                        $"Expected {g.Gap.Expected:N2} ({sign}{Math.Abs(g.Gap.Diff):N2}): " +
                        $"{stockName} price ÷ {stock.Conversion} × {g.Row.Entry.Conversion}.",
                        g.Row.Name, nameof(UomRow.Price)));
                }
            }

            if (gaps.Count > 0)
            {
                Add(new UomMessage("price-gaps-kept", UomMessageSeverity.Note,
                    $"Prices that differ from the {stockName} price and conversions are kept when prices change."));
            }
        }

        cellStates = states;

        return messages
            .OrderBy(m => (int)m.Severity)
            .ThenBy(m => m.JumpRow is not null && rowOrder.TryGetValue(m.JumpRow, out var index) ? index : -1)
            .ToList();
    }

    private UomMessage ErrorMessage(string key, string text)
    {
        switch (key)
        {
            case "stockuom":
                {
                    var stock = CurrentStockEntry;
                    if (stock is null)
                    {
                        return new UomMessage(key, UomMessageSeverity.Error, text, null, StockColumn);
                    }

                    var field = stock.Conversion is not > 0 ? nameof(UomRow.Conversion) : nameof(UomRow.Price);
                    return new UomMessage(key, UomMessageSeverity.Error, text, CurrentStockName, field);
                }
            case "prices":
                return new UomMessage(key, UomMessageSeverity.Error, text);
            case "conversion_base":
                return new UomMessage(key, UomMessageSeverity.Error, text, BaseUomName, nameof(UomRow.Conversion));
        }

        if (key.StartsWith("uomname_", StringComparison.Ordinal))
            return new UomMessage(key, UomMessageSeverity.Error, text, key["uomname_".Length..], nameof(UomRow.UnitLabel));
        if (key.StartsWith("conversion_", StringComparison.Ordinal))
            return new UomMessage(key, UomMessageSeverity.Error, text, key["conversion_".Length..], nameof(UomRow.Conversion));
        if (key.StartsWith("price_", StringComparison.Ordinal))
            return new UomMessage(key, UomMessageSeverity.Error, text, key["price_".Length..], nameof(UomRow.Price));

        return new UomMessage(key, UomMessageSeverity.Error, text);
    }

    private static string CellKey(string row, string field) => $"{row.ToUpperInvariant()}|{field}";

    private string? GetCellClass(UomRow row, string column) =>
        cellStates.TryGetValue(CellKey(row.Name, column), out var severity)
            ? severity == UomMessageSeverity.Error ? "cell-error" : "cell-warn"
            : null;


    private void DeriveBaseUnitPrice()
    {
        var baseEntry = workingUomEntries.Values.FirstOrDefault(e => e.Conversion == 1);
        if (baseEntry is null || baseEntry.Price.HasValue) return;

        var source = workingUomEntries.Values.FirstOrDefault(entry =>
            entry != baseEntry && entry.Price.HasValue && entry.Conversion.HasValue && entry.Conversion.Value != 0);
        if (source is null) return;

        baseEntry.Price = Math.Round(source.Price!.Value / source.Conversion!.Value, 2, MidpointRounding.AwayFromZero);
        baseEntry.IsAutoCalculated = true;
    }

    private Dictionary<string, string> BuildFinalErrors()
    {
        var errors = AddUomValidator.ValidateFinalUomEntries(workingUomEntries, BaseUomName, pricesRequired: StockUomRequired);

        var baseEntry = workingUomEntries.Values.FirstOrDefault(e => e.Conversion == 1);
        if (baseEntry is not null && workingUomEntries.Values.Any(e =>
                e != baseEntry && e.Price.HasValue && e.Conversion.HasValue && e.Conversion.Value != 0))
        {
            errors.Remove($"price_{BaseUomName}");
        }

        if (StockUomRequired)
        {
            var stock = CurrentStockEntry;
            if (stock is null)
                errors["stockuom"] = "Select which UOM corresponds to the company's stock-price unit.";
            else if (stock.Conversion is not > 0)
                errors["stockuom"] = "The stock UOM needs a valid conversion.";
            else if (!stock.Price.HasValue)
                errors["stockuom"] = "The stock UOM needs a price.";
        }

        foreach (var (name, entry) in workingUomEntries.Where(kv => kv.Value.IsActive))
        {
            foreach (var kv in AddUomValidator.ValidateUomConversion(name, entry.Conversion, workingUomEntries, BaseUomName))
            {
                errors.TryAdd(kv.Key, kv.Value);
            }
        }

        foreach (var kv in validationErrors.Where(kv => kv.Key.StartsWith("uomname_", StringComparison.Ordinal)))
        {
            errors.TryAdd(kv.Key, kv.Value);
        }

        return errors;
    }
    private void RefreshFinalErrors()
    {
        if (!hasTriedSave) return;

        var fresh = BuildFinalErrors();
        foreach (var kv in validationErrors.Where(kv => AddFormKeys.Contains(kv.Key)))
        {
            fresh.TryAdd(kv.Key, kv.Value);
        }

        validationErrors = fresh;
    }

    private static string UomRowDomId(string name) => "uom-row-" + Regex.Replace(name.Trim(), @"\s+", "_");

    private async Task JumpToMessageAsync(UomMessage message)
    {
        string targetId;
        string? column = null;

        if (message.JumpRow is { } row)
        {
            targetId = UomRowDomId(row);
            column = message.Field;
        }
        else
        {
            targetId = message.Field == RefField ? "uom-ref" : "uom-table";
        }

        await InvokeReviewJsAsync("jumpTo", targetId, column);
    }

    private async Task RevealFirstErrorAsync()
    {
        var messages = BuildMessages();

        reviewPanel?.Open();
        await InvokeAsync(StateHasChanged);

        await InvokeReviewJsAsync("flash", ReviewPanelId, "uom-flash-panel");

        var first = messages.FirstOrDefault(m => m.Severity == UomMessageSeverity.Error);
        if (first is not null)
        {
            await JumpToMessageAsync(first);
        }
    }

    private async Task InvokeReviewJsAsync(string function, params object?[] args)
    {
        try
        {
            reviewJs ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/uomreview.js");
            await reviewJs.InvokeVoidAsync(function, args);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"uomreview.js '{function}' failed: {ex.Message}");
        }
    }
}