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
    private const string BaseUomName = "PC";
    [Parameter] public HashSet<string> InUseUomNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [Parameter] public bool ShowAddUomModal { get; set; }
    [Parameter] public Dictionary<string, UomEntry> ExistingUomEntries { get; set; } = new();
    [Parameter] public string? DraftStorageKey { get; set; }
    [Parameter] public string? ItemCode { get; set; }
    [Parameter] public string? ItemName { get; set; }
    [Parameter] public EventCallback<Dictionary<string, UomEntry>> OnAdd { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    [Inject] private AddUomService AddUomService { get; set; } = default!;

    private readonly string[] defaultOptions = { BaseUomName, "Case", "Box", "Pack" };
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

                // If selected UOM is piece-like, auto-set conversion to 1
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
                // if custom UOM is piece-like, auto-set conversion to 1
                if (IsBaseUom(GetSelectedUomName()))
                {
                    conversionInput = "1";
                }
            }
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

        validationErrors = AddUomValidator.ValidateUomEntry(uomName, conversionInput, priceInput, workingUomEntries);

        if (validationErrors.Any())
        {
            return;
        }

        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(priceInput))
        {
            price = decimal.Parse(priceInput);
        }

        workingUomEntries[uomName] = new UomEntry
        {
            Conversion = int.Parse(conversionInput),
            Price = price,
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
            // Ignore focus errors when element is not ready
        }
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
        if (!IsBaseUom(uomName) && !IsUomInUse(uomName))
        {
            workingUomEntries.Remove(uomName);
            await PersistDraftAsync();
        }
    }

    private async Task AddAsync()
    {
        if (!workingUomEntries.TryGetValue(BaseUomName, out var baseEntry))
        {
            workingUomEntries[BaseUomName] = new UomEntry { Conversion = 1, Price = null };
            baseEntry = workingUomEntries[BaseUomName];
        }

        baseEntry.Conversion = 1;

        validationErrors = AddUomValidator.ValidateFinalUomEntries(workingUomEntries);

        if (validationErrors.Any())
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (!baseEntry.Price.HasValue)
        {
            var sourceEntry = workingUomEntries.Values.FirstOrDefault(entry => entry != baseEntry && entry.Price.HasValue);
            if (sourceEntry != null)
            {
                baseEntry.Price = (sourceEntry.Price!.Value / sourceEntry.Conversion) * 1;
                baseEntry.IsAutoCalculated = true;
            }
        }

        validationErrors.Clear();
        await PersistDraftAsync();

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
            // If piece-like UOM, skip conversion input (it's fixed to 1)
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
            // If piece-like UOM, conversion is fixed to 1 so skip validation and move to price
            if (IsBaseUom(GetSelectedUomName()))
            {
                conversionInput = "1";
                await priceInputRef.FocusAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(conversionInput))
            {
                validationErrors["conversion"] = "Conversion must be entered.";
                await InvokeAsync(StateHasChanged);
                return;
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
                Conversion = entry.Value.Conversion,
                Price = entry.Value.Price,
                IsAutoCalculated = false
            };
        }

        if (!workingUomEntries.ContainsKey(BaseUomName))
        {
            workingUomEntries[BaseUomName] = new UomEntry { Conversion = 1, Price = null };
        }
    }

    private void ApplyDraftState(AddUomModalDraftState draft)
    {
        workingUomEntries = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in draft.WorkingUomEntries)
        {
            workingUomEntries[NormalizeBaseUomName(entry.Key)] = entry.Value;
        }

        if (!workingUomEntries.ContainsKey(BaseUomName))
        {
            workingUomEntries[BaseUomName] = new UomEntry { Conversion = 1, Price = null };
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
        var draftJson = await jsModule.InvokeAsync<string?>("loadSalesInvoiceDraft", DraftStorageKey);
        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AddUomModalDraftState>(draftJson);
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

    private static bool IsBaseUom(string? uomName)
    {
        var normalized = (uomName ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "piece" or "pcs" or "pc";
    }
    private bool IsUomInUse(string? uomName) =>
        !string.IsNullOrWhiteSpace(uomName) && InUseUomNames.Contains(uomName.Trim());
    private static string NormalizeBaseUomName(string? uomName)
    {
        return IsBaseUom(uomName) ? BaseUomName : (uomName ?? string.Empty).Trim();
    }
}