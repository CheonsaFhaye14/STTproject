using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using STTproject.Models;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Features.User.MapItem.Validators;
using STTproject.Features.User.MapItem.Services;

namespace STTproject.Features.User.MapItem.Components.Modals;

public partial class AddUom
{
    [Parameter] public string BaseUomName { get; set; } = "PC";    
    [Parameter] public HashSet<string> InUseUomNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [Parameter] public bool ShowAddUomModal { get; set; }
    [Parameter] public Dictionary<string, UomEntry> ExistingUomEntries { get; set; } = new();
    [Parameter] public string? DraftStorageKey { get; set; }
    [Parameter] public string? ItemCode { get; set; }
    [Parameter] public string? ItemName { get; set; }
    [Parameter] public EventCallback<Dictionary<string, UomEntry>> OnAdd { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    [Inject] private AddUomService AddUomService { get; set; } = default!;

    private string[] DefaultOptions => new[] { BaseUomName, "Case", "Box", "Pack" };
    private Dictionary<string, UomEntry> workingUomEntries = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> validationErrors = new();
    private string selectedUomOption = string.Empty;
    private string SelectedUomOption
    {
        get => selectedUomOption;
        set
        {
            if (selectedUomOption != value)
            {
                selectedUomOption = value;
                validationErrors.Remove("uom");
                if (selectedUomOption != "__custom")
                {
                    CustomUom = string.Empty;
                }

                if (IsBaseUom(GetSelectedUomName()))
                {
                    conversionInput = "1";
                }
            }
        }
    }

    private string customUom = string.Empty;
    private string CustomUom
    {
        get => customUom;
        set
        {
            if (customUom != value)
            {
                customUom = value;
                validationErrors.Remove("uom");
                validationErrors.Remove("conversion");
                if (IsBaseUom(GetSelectedUomName()))
                {
                    conversionInput = "1";
                }
            }
        }
    }
    private string conversionInput = string.Empty;
    private string ConversionBasedOnInput = string.Empty;
    private string ConversionInput
    {
        get => conversionInput;
        set
        {
            conversionInput = value;
            validationErrors.Remove("conversion");
        }
    }

    private string priceInput = string.Empty;
    private string PriceInput
    {
        get => priceInput;
        set
        {
            priceInput = value;
            validationErrors.Remove("price");
        }
    }

    private bool wasShown;
    private bool shouldFocusUomSelect;
    private bool showClearDraftConfirmModal;
    private string? lastEditedUom = null;
    private AddUomModalDraftState? loadedDraft;
    private IJSObjectReference? jsModule;
    private ElementReference uomSelectRef;
    private ElementReference conversionBasedOnInputRef;
    private ElementReference conversionInputRef;
    private ElementReference priceInputRef;
    private ElementReference addUomButtonRef;

    protected override async Task OnParametersSetAsync()
    {
        if (ShowAddUomModal && !wasShown)
        {
            loadedDraft = await LoadDraftAsync();

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
                priceInput = string.Empty;
                ConversionBasedOnInput = string.Empty;
                validationErrors.Clear();
            }

            showClearDraftConfirmModal = false;
            lastEditedUom = null;
            shouldFocusUomSelect = true;
            wasShown = true;
            await PersistDraftAsync();
        }
        else if (!ShowAddUomModal)
        {
            wasShown = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ShowAddUomModal && shouldFocusUomSelect)
        {
            shouldFocusUomSelect = false;
            await FocusUomSelectAsync();
        }

        await base.OnAfterRenderAsync(firstRender);
    }
    private bool HasBaseUnit(Dictionary<string, UomEntry> entries) =>
        entries.ContainsKey(BaseUomName) || entries.Values.Any(e => e.Conversion == 1);

    private string GetSelectedUomName()
    {
        if (selectedUomOption == "__custom")
            return (CustomUom ?? string.Empty).Trim();
        return selectedUomOption;
    }
        
    private async Task AddUomEntryAsync(bool autoCalc = false)
{
    var uomName = selectedUomOption == "__custom"
        ? (CustomUom ?? string.Empty).Trim()
        : selectedUomOption.Trim();

    int? enteredCount = string.IsNullOrWhiteSpace(conversionInput)
        ? null
        : int.Parse(conversionInput);

    var basisName = string.IsNullOrWhiteSpace(ConversionBasedOnInput) ? BaseUomName : ConversionBasedOnInput;

    int? pcConversion;
    if (IsBaseUom(uomName))
    {
        pcConversion = enteredCount;
        basisName = BaseUomName;
    }
    else if (!enteredCount.HasValue)
    {
        pcConversion = null;
    }
    else if (workingUomEntries.TryGetValue(basisName, out var basisEntry))
    {
        pcConversion = basisEntry.Conversion.HasValue
            ? enteredCount.Value * basisEntry.Conversion.Value
            : enteredCount.Value;
    }
    else
    {
        pcConversion = enteredCount.Value;
        basisName = BaseUomName;
    }

    validationErrors = AddUomValidator.ValidateUomEntry(uomName, conversionInput, pcConversion, priceInput, workingUomEntries, BaseUomName);
    
    if (validationErrors.Any())
    {
        return;
    }

    decimal? price = null;
    if (!string.IsNullOrWhiteSpace(priceInput))
    {
        price = decimal.Parse(priceInput);
    }

    var priorItemsUomId = workingUomEntries.TryGetValue(uomName, out var priorEntry)
            ? priorEntry.ItemsUomId
            : null;

    workingUomEntries[uomName] = new UomEntry
    {
        ItemsUomId = priorItemsUomId,
        Conversion = pcConversion,
        ConversionBasedOn = basisName,
        Price = price,
        IsActive = true,
        IsAutoCalculated = autoCalc || !price.HasValue
    };

    selectedUomOption = string.Empty;
    customUom = string.Empty;
    conversionInput = string.Empty;
    priceInput = string.Empty;

    await RecalculatePricesAsync(uomName);
    await PersistDraftAsync();
    shouldFocusUomSelect = true;
    await InvokeAsync(StateHasChanged);
    await FocusUomSelectAsync();
}

    private async Task FocusUomSelectAsync()
    {
        await Task.Yield();
        try
        {
            await uomSelectRef.FocusAsync();
        }
        catch
        {

        }
    }

    private sealed class UomRow
    {
        public required string Name { get; init; }
        public required UomEntry Entry { get; init; }
        public bool IsBase { get; init; }

        public string UnitLabel { get; set; } = string.Empty;
        public int? Conversion { get => Entry.Conversion; set => Entry.Conversion = value; }
        public decimal? Price { get => Entry.Price; set => Entry.Price = value; }
    }

    private List<UomRow> UomRows =>
        workingUomEntries
            .OrderBy(x => x.Value.Conversion ?? decimal.MaxValue)
            .Select(kv => new UomRow { Name = kv.Key, Entry = kv.Value, IsBase = kv.Value.Conversion == 1, UnitLabel = kv.Key })
            .ToList();

    private bool IsBaseUomEntry(string? uomName)
    {
        return !string.IsNullOrWhiteSpace(uomName)
            && workingUomEntries.TryGetValue(uomName, out var entry)
            && entry.Conversion == 1;
    }
    private static readonly List<string> UomColumns = new()
    {
        nameof(UomRow.UnitLabel), nameof(UomRow.Conversion), nameof(UomRow.Price)
    };

    private static readonly List<string> UomEditableColumns = new()
    {
        nameof(UomRow.UnitLabel), nameof(UomRow.Conversion), nameof(UomRow.Price)
    };

    private static readonly Dictionary<string, string> UomColumnLabels = new()
    {
        [nameof(UomRow.UnitLabel)] = "Unit of Measure",
        [nameof(UomRow.Conversion)] = "Conversion per (PC)",
        [nameof(UomRow.Price)] = "Price",
    };

    private static readonly Dictionary<string, string> UomColumnInputTypes = new()
    {
        [nameof(UomRow.Conversion)] = "number",
        [nameof(UomRow.Price)] = "number",
    };

    private string? GetUomDisplayValue(UomRow row, string column)
    {
        if (column == nameof(UomRow.UnitLabel))
        {
            return row.Name
                + (row.IsBase ? " (Base Unit)" : string.Empty)
                + (!row.Entry.IsActive ? " (Inactive)" : string.Empty);
        }

        return null; 
    }

    private async Task HandleUomCellChangedAsync((UomRow Row, string Column) args)
    {
        if (args.Column == nameof(UomRow.UnitLabel))
        {
            await HandleUomRenamedAsync(args.Row.Name, args.Row.UnitLabel);
        }
        else if (args.Column == nameof(UomRow.Conversion))
        {
            await HandleRowConversionChangedAsync(args.Row.Name);
        }
        else if (args.Column == nameof(UomRow.Price))
        {
            await HandlePriceInputChangedAsync(args.Row.Name);
        }
    }

    private async Task HandleUomRenamedAsync(string oldName, string? newNameRaw)
    {
        var newName = (newNameRaw ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!workingUomEntries.TryGetValue(oldName, out var entry))
        {
            return;
        }

        var renameErrors = AddUomValidator.ValidateUomRename(oldName, newName, workingUomEntries, BaseUomName);
        if (renameErrors.Any())
        {
            foreach (var kv in renameErrors)
            {
                validationErrors[kv.Key] = kv.Value;
            }
            await InvokeAsync(StateHasChanged);
            return;
        }

        validationErrors.Remove($"uomname_{oldName}");

        if (workingUomEntries.TryGetValue(newName, out var conflictingEntry) && !conflictingEntry.IsActive)
        {
            workingUomEntries.Remove(newName);
        }

        workingUomEntries.Remove(oldName);
        workingUomEntries[newName] = entry;

        foreach (var other in workingUomEntries.Values)
        {
            if (string.Equals(other.ConversionBasedOn, oldName, StringComparison.OrdinalIgnoreCase))
            {
                other.ConversionBasedOn = newName;
            }
        }
        if (string.Equals(ConversionBasedOnInput, oldName, StringComparison.OrdinalIgnoreCase))
        {
            ConversionBasedOnInput = newName;
        }

        // Carry any per-row errors over to the new key so they keep showing.
        if (validationErrors.TryGetValue($"conversion_{oldName}", out var convErr))
        {
            validationErrors.Remove($"conversion_{oldName}");
            validationErrors[$"conversion_{newName}"] = convErr;
        }
        if (validationErrors.TryGetValue($"price_{oldName}", out var priceErr))
        {
            validationErrors.Remove($"price_{oldName}");
            validationErrors[$"price_{newName}"] = priceErr;
        }

        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandlePriceInputChangedAsync(string uomName)
    {
        if (workingUomEntries.TryGetValue(uomName, out var entry))
        {
            entry.IsAutoCalculated = false;
            lastEditedUom = uomName;
        }

        await RecalculatePricesAsync(uomName);
        await PersistDraftAsync();
    }

    private async Task RecalculatePricesAsync(string? sourceUom = null)
    {
        AddUomService.RecalculatePrices(workingUomEntries, sourceUom);
        await InvokeAsync(StateHasChanged);
    }

    private async Task RemoveUomEntry(string uomName)
    {
        if (IsBaseUomEntry(uomName))
        {
            return;
        }

        if (!workingUomEntries.TryGetValue(uomName, out var entry))
        {
            return;
        }

        if (IsUomInUse(uomName))
        {
            entry.IsActive = false; 
        }
        else
        {
            workingUomEntries.Remove(uomName); 
        }

        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }
    private bool showReactivateConflictModal;
    private string reactivateConflictMessage = string.Empty;
    private async Task ReactivateUomEntry(string uomName)
    {
        if (!workingUomEntries.TryGetValue(uomName, out var entry))
        {
            return;
        }

        var conflict = workingUomEntries.FirstOrDefault(kv =>
            kv.Value.IsActive &&
            !string.Equals(kv.Key, uomName, StringComparison.OrdinalIgnoreCase) &&
            kv.Value.Conversion == entry.Conversion);

        if (!string.IsNullOrEmpty(conflict.Key))
        {
            reactivateConflictMessage =
                $"Cannot reactivate '{uomName}': conversion {entry.Conversion} is already used by '{conflict.Key}'. Update the conversion first.";
            showReactivateConflictModal = true;
            await InvokeAsync(StateHasChanged);
            return;
        }

        entry.IsActive = true;
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    private void DismissReactivateConflictModal()
    {
        showReactivateConflictModal = false;
        reactivateConflictMessage = string.Empty;
    }
    
    private async Task AddAsync()
    {
        var baseEntry = workingUomEntries.TryGetValue(BaseUomName, out var byName)
            ? byName
            : workingUomEntries.Values.FirstOrDefault(e => e.Conversion == 1);

        if (baseEntry is null)
        {
            workingUomEntries[BaseUomName] = new UomEntry { Conversion = null, Price = null };
            baseEntry = workingUomEntries[BaseUomName];
        }

        if (!baseEntry.Price.HasValue)
        {
            var sourceEntry = workingUomEntries.Values.FirstOrDefault(entry =>
                entry != baseEntry &&
                entry.Price.HasValue &&
                entry.Conversion.HasValue &&
                entry.Conversion.Value != 0);

            if (sourceEntry != null)
            {
                baseEntry.Price = (sourceEntry.Price!.Value / sourceEntry.Conversion!.Value) * 1;
                baseEntry.IsAutoCalculated = true;
            }
        }

        validationErrors = AddUomValidator.ValidateFinalUomEntries(workingUomEntries, BaseUomName);
        
        if (validationErrors.Any())
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        validationErrors.Clear();

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

    private string GetFieldError(string fieldKey)
    {
        return validationErrors.TryGetValue(fieldKey, out var message) ? message : string.Empty;
    }

    private async Task HandleUomSelectKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            if (string.IsNullOrWhiteSpace(selectedUomOption))
            {
                return;
            }

            if (selectedUomOption == "__custom" && string.IsNullOrWhiteSpace(CustomUom))
            {
                validationErrors["uom"] = "Custom unit of measure is required.";
                return;
            }
            if (IsBaseUom(GetSelectedUomName()))
            {
                await priceInputRef.FocusAsync();
            }
            else
            {
                await conversionInputRef.FocusAsync();
            }
            return;
        }

        if (e.Key == "Tab" && !e.ShiftKey)
        {
            if (selectedUomOption == "__custom" && string.IsNullOrWhiteSpace(CustomUom))
            {
                validationErrors["uom"] = "Custom unit of measure is required.";
                return;
            }
            if (IsBaseUom(GetSelectedUomName()))
            {
                await priceInputRef.FocusAsync();
            }
            else
            {
                await conversionInputRef.FocusAsync();
            }
        }
    }

    private async Task HandleConversionKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" || (e.Key == "Tab" && !e.ShiftKey))
        {
            if (IsBaseUom(GetSelectedUomName()))
            {
                conversionInput = "1";
            }

            // Conversion is optional now, so a blank value no longer blocks progression.
            await priceInputRef.FocusAsync();
        }
        else if (e.Key == "Tab" && e.ShiftKey)
        {
            await conversionBasedOnInputRef.FocusAsync();
        }
    }

    private async Task HandleConversionBasedOnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" || (e.Key == "Tab" && !e.ShiftKey))
        {
            if (IsBaseUom(GetSelectedUomName()))
            {
                conversionInput = "1";
            }

            await priceInputRef.FocusAsync();
        }
        else if (e.Key == "Tab" && e.ShiftKey)
        {
            await uomSelectRef.FocusAsync();
        }
    }
        
    private async Task HandlePriceKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" || (e.Key == "Tab" && !e.ShiftKey))
        {
            await addUomButtonRef.FocusAsync();
        }
        else if (e.Key == "Tab" && e.ShiftKey)
        {
            await conversionInputRef.FocusAsync();
        }
    }

    private async Task HandleAddUomButtonKeyDown(KeyboardEventArgs e)
    {
        if ((e.Key == "Tab" && !e.ShiftKey) || (e.Key == "Enter" && e.CtrlKey))
        {
            await uomSelectRef.FocusAsync();
        }
        else if (e.Key == "Tab" && e.ShiftKey)
        {
            await priceInputRef.FocusAsync();
        }
    }

    private void CloneExistingEntries()
    {
        workingUomEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ExistingUomEntries)
        {
            workingUomEntries[NormalizeBaseUomName(entry.Key)] = new UomEntry
            {
                ItemsUomId = entry.Value.ItemsUomId,
                Conversion = entry.Value.Conversion,
                Price = entry.Value.Price,
                IsAutoCalculated = false
            };
        }

        if (!HasBaseUnit(workingUomEntries))
        {
            workingUomEntries[BaseUomName] = new UomEntry { Conversion = null, Price = null };
        }
    }

    private void ApplyDraftState(AddUomModalDraftState draft)
    {
        workingUomEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in draft.WorkingUomEntries)
        {
            workingUomEntries[NormalizeBaseUomName(entry.Key)] = entry.Value;
        }

        if (!HasBaseUnit(workingUomEntries))
        {
            workingUomEntries[BaseUomName] = new UomEntry { Conversion = null, Price = null };
        }

        selectedUomOption = draft.SelectedUomOption;
        customUom = draft.CustomUom;
        conversionInput = draft.ConversionInput;
        priceInput = draft.PriceInput;
        validationErrors.Clear();
    }

    private async Task PersistDraftAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftStorageKey))
        {
            return;
        }

        jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");

        var draftState = new AddUomModalDraftState
        {
            WorkingUomEntries = new Dictionary<string, UomEntry>(workingUomEntries, StringComparer.OrdinalIgnoreCase),
            SelectedUomOption = selectedUomOption,
            CustomUom = customUom,
            ConversionInput = conversionInput,
            PriceInput = priceInput
        };

        await jsModule.InvokeVoidAsync("saveSalesInvoiceDraft", DraftStorageKey, JsonSerializer.Serialize(draftState));
    }

    private async Task<AddUomModalDraftState?> LoadDraftAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftStorageKey))
        {
            return null;
        }

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

        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AddUomModalDraftState>(draftJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ShowClearDraftConfirmModal()
    {
        showClearDraftConfirmModal = true;
    }

    private async Task ConfirmClearDraftAsync()
    {
        showClearDraftConfirmModal = false;

        if (!string.IsNullOrWhiteSpace(DraftStorageKey))
        {
            jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
            await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", DraftStorageKey);
        }

        workingUomEntries.Clear();
        workingUomEntries[BaseUomName] = new UomEntry { Conversion = 1, Price = null };
        selectedUomOption = string.Empty;
        customUom = string.Empty;
        conversionInput = string.Empty;
        priceInput = string.Empty;
        validationErrors.Clear();
        lastEditedUom = null;
        shouldFocusUomSelect = true;
        ConversionBasedOnInput = string.Empty;

        await InvokeAsync(StateHasChanged);
        await FocusUomSelectAsync();
    }

    private void CancelClearDraftConfirm()
    {
        showClearDraftConfirmModal = false;
    }

    private async Task ClearDraftAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftStorageKey))
        {
            return;
        }

        await ConfirmClearDraftAsync();
    }

    private bool IsBaseUom(string? uomName)
    {
        return string.Equals((uomName ?? string.Empty).Trim(), BaseUomName.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    private bool IsUomInUse(string? uomName) =>
        !string.IsNullOrWhiteSpace(uomName) && InUseUomNames.Contains(uomName.Trim());
    private string NormalizeBaseUomName(string? uomName)
    {
        return IsBaseUom(uomName) ? BaseUomName : (uomName ?? string.Empty).Trim();
    }
    private bool IsConversionNotSet = false;
    private async Task ClearConversionInputAsync()
    {
        ConversionInput = string.Empty;
        IsConversionNotSet = true;
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }
    private async Task SetConversionInput()
    {
        IsConversionNotSet = false;
        await InvokeAsync(StateHasChanged);
    }
private List<string> ConversionBasedOnOptions =>
    workingUomEntries.Keys
        .Where(k => k != GetSelectedUomName() && workingUomEntries[k]?.IsActive == true)
        .OrderBy(k => k)
        .ToList();
    private async Task HandleRowConversionChangedAsync(string uomName)
    {
        if (!workingUomEntries.TryGetValue(uomName, out var entry))
        {
            return;
        }

        // A manually-typed conversion is no longer an auto-derived one.  
        entry.IsAutoCalculated = entry.IsAutoCalculated && entry.Conversion is null;

        validationErrors.Remove($"conversion_{uomName}");
        var conversionErrors = AddUomValidator.ValidateUomConversion(uomName, entry.Conversion, workingUomEntries, BaseUomName);
        foreach (var kv in conversionErrors)
        {
            validationErrors[kv.Key] = kv.Value;
        }

        await RecalculatePricesAsync();
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }
    private string? GetRowError(int idx)
    {
        var rows = UomRows;
        if (idx < 0 || idx >= rows.Count)
        {
            return null;
        }

        var name = rows[idx].Name;
        return GetFieldError($"uomname_{name}") is { Length: > 0 } nameError ? nameError
            : GetFieldError($"conversion_{name}") is { Length: > 0 } convError ? convError
            : GetFieldError($"price_{name}");
    }
    private async Task OnConversionBasedOnChanged(string value)
    {
        ConversionBasedOnInput = value;
        await PersistDraftAsync();
    }

    private async Task ClearRowConversionAsync(string uomName)
    {
        if (IsBaseUomEntry(uomName))
        {
            return;
        }

        if (!workingUomEntries.TryGetValue(uomName, out var entry))
        {
            return;
        }

        entry.Conversion = null;

        // A price that was only ever derived from this conversion no longer means
        // anything once the conversion is cleared — drop it rather than leave a
        // stale calculated value on screen. A manually-typed price is left alone.
        if (entry.IsAutoCalculated)
        {
            entry.Price = null;
        }

        await RecalculatePricesAsync();
        await PersistDraftAsync();
        await InvokeAsync(StateHasChanged);
    }

    private string getLabelForConversionBased()
        {
            return $"How many {ConversionBasedOnInput} per {GetSelectedUomName() ?? "unit"}?";
        }
    private async Task SetRowConversionAsync(string uomName)
        {
            if (workingUomEntries.TryGetValue(uomName, out var entry))
            {
                entry.IsAutoCalculated = entry.IsAutoCalculated && entry.Conversion is null;
            }

            await RecalculatePricesAsync();
            await PersistDraftAsync();
            await InvokeAsync(StateHasChanged);
        }
    private string getModalSubtitle()
    {
        if (!string.IsNullOrWhiteSpace(ItemCode) && !string.IsNullOrWhiteSpace(ItemName))
        {
            return $"Item: {ItemCode} - {ItemName}";
        }
        else if (!string.IsNullOrWhiteSpace(ItemCode))
        {
            return $"Item: {ItemCode}";
        }
        else if (!string.IsNullOrWhiteSpace(ItemName))
        {
            return $"Item: {ItemName}";
        }
        else
        {
            return string.Empty;
        }
    }
    private async Task OnUomSelectChanged(string value)
    {
        SelectedUomOption = value; 
        await PersistDraftAsync();
    }
    
}