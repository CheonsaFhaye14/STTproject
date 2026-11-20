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
        new(StringComparer.Ordinal) { "uom", "customUom", "conversion" };

    private bool hasTriedSave;
    private UomReviewPanel? reviewPanel;
    private IJSObjectReference? reviewJs;
    private Dictionary<string, UomMessageSeverity> cellStates = new(StringComparer.Ordinal);

    private IReadOnlyList<UomMessage> BuildMessages()
    {
        var rows = UomRows;
        var rowOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++) rowOrder[rows[i].Name] = i;

        var messages = new List<UomMessage>();
        var states = new Dictionary<string, UomMessageSeverity>(StringComparer.Ordinal);

        void Mark(string? row, string? field, UomMessageSeverity severity)
        {
            if (row is null || field is null || severity == UomMessageSeverity.Note) return;
            var key = CellKey(row, field);
            if (!states.TryGetValue(key, out var existing) || severity < existing) states[key] = severity;
        }

        void Add(UomMessage message)
        {
            // A message pointing at a row that no longer exists becomes table-level.
            if (message.RowName is not null && !rowOrder.ContainsKey(message.RowName))
                message = message with { RowName = null, Field = null, TargetRow = null };

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
            Add(new UomMessage("ref-note", UomMessageSeverity.Warning, ReferenceNote!, null, RefField));

        if (!StockUomRequired)
        {
            Add(new UomMessage("free-item", UomMessageSeverity.Note,
                "The company item has no stock UOM, so it is free (₱0) on invoices. Conversions are kept but not used for prices.",
                null, RefField));
        }
        else
        {
            if (StockNeedsRescale && !validationErrors.ContainsKey("stockuom"))
            {
                Add(new UomMessage("stock-scale", UomMessageSeverity.Warning,
                    $"Conversions look per piece ({RescaleRowName} = {FormatNumber(workingUomEntries[RescaleRowName!].Conversion!.Value)}, " +
                    $"{PieceDisplayName} = 1). Use “Rescale to {CompanyStockUomName}” so the stock UOM is 1. " +
                    $"If the subd really sells {RescaleRowName} as a bundle, leave it.",
                    RescaleRowName, nameof(UomRow.Conversion)));
            }

            if (ApplicableStockPrice is null)
            {
                Add(new UomMessage("no-price", UomMessageSeverity.Note,
                    "There is no company price for this sub-distributor's price list yet, so prices show as “-”.",
                    null, RefField));
            }

            if (StockRowImplicit && workingUomEntries.FirstOrDefault(kv => kv.Value.IsActive && IsStockName(kv.Key)) is { Key: not null } bundle
                && bundle.Value.Conversion is decimal bc)
            {
                Add(new UomMessage("stock-bundle", UomMessageSeverity.Note,
                    $"This subd's {bundle.Key} is {FormatNumber(bc)} {CompanyStockUomName} (sold as a bundle), so it's priced as " +
                    $"{FormatNumber(bc)} × the {CompanyStockUomName} price. The company {CompanyStockUomName} itself is always 1.",
                    null, RefField));
            }
            else if (StockRowImplicit)
            {
                Add(new UomMessage("stock-implicit", UomMessageSeverity.Note,
                    $"{CompanyStockUomName} isn't one of this item's units. That's fine: the stock UOM is always 1 " +
                    $"and the sizes below are counted in {CompanyStockUomName}.", null, RefField));
            }

            if (!StockIsPiece && PieceEntryName is { } piece && workingUomEntries[piece].Conversion == 1m)
            {
                Add(new UomMessage("piece-same", UomMessageSeverity.Warning,
                    $"{piece} is the same conversion as {StockDisplayName}. If a {StockDisplayName} holds more than one piece, fix {piece}'s conversion.",
                    piece, nameof(UomRow.Conversion)));
            }

            if (!StockIsPiece && PiecesPerStock is null && rows.Count > 0)
            {
                Add(new UomMessage("no-piece", UomMessageSeverity.Note,
                    $"Add a {BaseUomName} unit to see the packaging (pieces per {StockDisplayName}).", null, RefField));
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
                return StockEntryName is { } stock
                    ? new UomMessage(key, UomMessageSeverity.Error, text, stock, nameof(UomRow.Conversion))
                    : new UomMessage(key, UomMessageSeverity.Error, text, null, RefField);
        }

        if (key.StartsWith("uomname_", StringComparison.Ordinal))
            return new UomMessage(key, UomMessageSeverity.Error, text, key["uomname_".Length..], nameof(UomRow.UnitLabel));
        if (key.StartsWith("conversion_", StringComparison.Ordinal))
            return new UomMessage(key, UomMessageSeverity.Error, text, key["conversion_".Length..], nameof(UomRow.Conversion));

        return new UomMessage(key, UomMessageSeverity.Error, text);
    }

    private static string CellKey(string row, string field) => $"{row.ToUpperInvariant()}|{field}";

    private string? GetCellClass(UomRow row, string column) =>
        cellStates.TryGetValue(CellKey(row.Name, column), out var severity)
            ? severity == UomMessageSeverity.Error ? "cell-error" : "cell-warn"
            : null;

    private Dictionary<string, string> BuildFinalErrors()
    {
        var errors = AddUomValidator.ValidateFinalUomEntries(workingUomEntries, conversionRequired: StockUomRequired);

        if (StockUomRequired)
        {
            var stock = StockEntry;
            // No row for the stock UOM is fine (it is always 1); a row that is there must be 1.
            if (stock is not null && stock.Conversion != 1m)
                errors["stockuom"] = $"The stock UOM ({StockEntryName}) must have conversion 1. Use “Rescale to {StockEntryName}”.";
        }

        foreach (var (name, entry) in workingUomEntries.Where(kv => kv.Value.IsActive))
            foreach (var kv in AddUomValidator.ValidateUomConversion(name, entry.Conversion, workingUomEntries, isStock: false))
                errors.TryAdd(kv.Key, kv.Value);

        foreach (var kv in validationErrors.Where(kv => kv.Key.StartsWith("uomname_", StringComparison.Ordinal)))
            errors.TryAdd(kv.Key, kv.Value);

        DropStockDuplicateErrors(errors);
        return errors;
    }

    /// <summary>
    /// "Same conversion" errors caused only by the stock UOM row (always 1) are dropped: a unit the same conversion as the
    /// stock UOM gets the "same size" warning instead, so adding the stock UOM row never blocks saving.
    /// </summary>
    private void DropStockDuplicateErrors(Dictionary<string, string> errors)
    {
        if (StockEntryName is not { } stock) return;

        var atOne = workingUomEntries
            .Where(kv => kv.Value.IsActive && kv.Value.Conversion == 1m)
            .Select(kv => kv.Key)
            .ToList();

        // Only the stock row plus one other unit share 1: not a real duplicate between saved units.
        if (atOne.Count(n => !string.Equals(n, stock, StringComparison.OrdinalIgnoreCase)) <= 1)
            foreach (var name in atOne)
                if (errors.TryGetValue($"conversion_{name}", out var msg) && msg.Contains("same conversion", StringComparison.OrdinalIgnoreCase))
                    errors.Remove($"conversion_{name}");
    }

    private void RefreshFinalErrors()
    {
        if (!hasTriedSave) return;

        var fresh = BuildFinalErrors();
        foreach (var kv in validationErrors.Where(kv => AddFormKeys.Contains(kv.Key)))
            fresh.TryAdd(kv.Key, kv.Value);

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
        if (first?.JumpRow is { } row && workingUomEntries.ContainsKey(row))
        {
            BeginEdit(row);                       // open the unit so it can be fixed right there
            await InvokeAsync(StateHasChanged);
        }
        if (first is not null) await JumpToMessageAsync(first);
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