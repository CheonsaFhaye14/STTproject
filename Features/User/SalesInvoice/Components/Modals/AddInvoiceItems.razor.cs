using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using STTproject.Shared.Components;
using STTproject.Features.User.SalesInvoice.Validators;
using STTproject.Models;
using STTproject.Data;
using STTproject.Shared.Helper;

namespace STTproject.Features.User.SalesInvoice.Components.Modals;

public partial class AddInvoiceItems
{
    private ElementReference PrincipalSelect;

    private ElementReference SkuCodeInput;

    private GenericAutocomplete<SubdItem>? itemNameAutocomplete;

    private ElementReference UomSelect;

    private ElementReference QuantityInput;

    private IJSObjectReference? jsModule;
    [Inject] public IJSRuntime JSRuntime { get; set; } = default!;

    private bool ShowValidationErrors;

    private bool skipNextSkuBlurValidation;
    private string? SaveErrorMessage;
    private Dictionary<string, string> ValidationErrors { get; set; } = new();

    [Parameter] public bool ShowModal { get; set; }
    [Parameter] public EventCallback<bool> ShowModalChanged { get; set; }
    [Parameter] public EventCallback OnCancelModal { get; set; }
    [Parameter] public EventCallback<List<InputItemModel>> OnSaveModalItems { get; set; }
    [Parameter] public EventCallback<List<InputItemModel>> OnBeforeSave { get; set; }

    [Parameter] public List<InputItemModel> Items { get; set; } = new();
    [Parameter] public EventCallback<List<InputItemModel>> ItemsChanged { get; set; }
    [Parameter] public List<SubdItem> AvailableItems { get; set; } = new();
    [Parameter] public List<ItemsUom> AvailableUoms { get; set; } = new();
    [Parameter] public int SelectedSubdistributorId { get; set; }
    [Parameter] public bool HeaderIsSaved { get; set; } = false;
    [Parameter] public DateOnly InvoiceDate { get; set; }
    [Parameter] public EventCallback OnDraftChanged { get; set; }

    private string _selectedPrincipal = string.Empty;
    private string _selectedCategory = string.Empty;

    private string SelectedPrincipal
    {
        get => _selectedPrincipal;
        set
        {
            var newVal = value?.Trim() ?? string.Empty;
            if (string.Equals(_selectedPrincipal, newVal, StringComparison.OrdinalIgnoreCase))
                return;
            _selectedPrincipal = newVal;

            // Clear category when principal changes so user can pick appropriate category.
            _selectedCategory = string.Empty;

            // Reset draft fields (but keep principal/category selection via backing fields)
            ResetNewItem();

            _ = OnDraftChanged.InvokeAsync();
        }
    }

    private string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            var newVal = value?.Trim() ?? string.Empty;
            if (string.Equals(_selectedCategory, newVal, StringComparison.OrdinalIgnoreCase))
                return;
            _selectedCategory = newVal;

            ResetNewItem();
            _ = OnDraftChanged.InvokeAsync();
        }
    }

    private DateTime lastPrincipalEnterTime = DateTime.MinValue;

    private List<SubdItem> FilteredAvailableItems => AvailableItems
        .Where(i => i.SubDistributorId == SelectedSubdistributorId)
        .Where(i => string.IsNullOrWhiteSpace(SelectedPrincipal)
            || string.Equals(i.CompanyItem?.Principal?.Trim(), SelectedPrincipal?.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(i => string.IsNullOrWhiteSpace(SelectedCategory)
            || string.Equals(i.CompanyItem?.Category?.Trim(), SelectedCategory?.Trim(), StringComparison.OrdinalIgnoreCase))
        .ToList();

    private List<string> AvailablePrincipals => AvailableItems
        .Where(i => i.SubDistributorId == SelectedSubdistributorId)
        .Select(i => i.CompanyItem?.Principal)
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => p!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(p => p)
        .ToList();

    private List<string> AvailableCategories => AvailableItems
        .Where(i => i.SubDistributorId == SelectedSubdistributorId)
        .Where(i => string.IsNullOrWhiteSpace(SelectedPrincipal) || string.Equals(i.CompanyItem?.Principal?.Trim(), SelectedPrincipal?.Trim(), StringComparison.OrdinalIgnoreCase))
        .Select(i => i.CompanyItem?.Category)
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .Select(c => c!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c)
        .ToList();


    private List<InputItemModel> modalItems = new();
    public InputItemModel NewItem { get; set; } = CreateNewItem();
    private SubdItem? CurrentSubdItem { get; set; }
    private ItemsUom? CurrentUom { get; set; }
    private decimal? CurrentUnitPrice { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrWhiteSpace(SelectedPrincipal)
            && !AvailablePrincipals.Any(p => string.Equals(p, SelectedPrincipal?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPrincipal = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(SelectedCategory)
            && !AvailableCategories.Any(c => string.Equals(c, SelectedCategory?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            SelectedCategory = string.Empty;
        }

        await UpdateCurrentUnitPriceAsync();
    }
    // Change handlers removed — logic handled in SelectedPrincipal/SelectedCategory setters

    private async Task HandlePrincipalKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            var timeSinceLastEnter = (DateTime.Now - lastPrincipalEnterTime).TotalMilliseconds;

            if (timeSinceLastEnter < 500)
            {
                await Task.Delay(10);
                await SkuCodeInput.FocusAsync();
                lastPrincipalEnterTime = DateTime.MinValue;
            }
            else
            {
                lastPrincipalEnterTime = DateTime.Now;
            }
        }
    }

    private string NewSku
    {
        get => NewItem.ItemCode;
        set => OnSkuChanged(value);
    }

    public InvoiceItemsDraftState CaptureDraftState()
    {
        return new InvoiceItemsDraftState
        {
            NewItem = CloneItem(NewItem),
            ModalItems = modalItems.Select(CloneItem).ToList()
        };
    }

    public async Task RestoreDraftStateAsync(InvoiceItemsDraftState? draft)
    {
        if (draft is null)
        {
            return;
        }

        NewItem = draft.NewItem is null ? CreateNewItem() : CloneItem(draft.NewItem);
        modalItems = draft.ModalItems.Select(CloneItem).ToList();
        CurrentSubdItem = AvailableItems.FirstOrDefault(i =>
            i.SubdItemId == NewItem.SubdItemId && i.SubDistributorId == SelectedSubdistributorId);
        CurrentUom = AvailableUoms.FirstOrDefault(u => u.ItemsUomId == NewItem.ItemsUomId);
        if (CurrentUom != null)
        {
            CurrentUnitPrice = await salesInvoiceService.ResolveUomPriceAsync(CurrentUom.ItemsUomId, InvoiceDate);
            NewItem.Amount = CurrentUnitPrice.Value * NewItem.Quantity;
        }
        ShowValidationErrors = false;
        skipNextSkuBlurValidation = false;
        ValidationErrors.Clear();
    }

    private void OnSkuChanged(string sku)
    {
        NewItem.ItemCode = sku;

        var selected = FilteredAvailableItems
            .FirstOrDefault(i => i.SubdItemCode.Equals(sku, StringComparison.OrdinalIgnoreCase) && i.SubDistributorId ==
SelectedSubdistributorId);

        if (selected != null)
        {
            SelectedPrincipal = selected.CompanyItem?.Principal?.Trim() ?? string.Empty;
            SelectedCategory = selected.CompanyItem?.Category?.Trim() ?? string.Empty;
            CurrentSubdItem = selected;
            NewItem.SubdItemId = selected.SubdItemId;
            NewItem.ItemName = GetItemDisplayLabel(selected);
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
            CurrentUnitPrice = null;
        }
        else
        {
            SelectedPrincipal = string.Empty;
            CurrentSubdItem = null;
            NewItem.SubdItemId = 0;
            NewItem.ItemName = string.Empty;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
            CurrentUnitPrice = null;
        }

        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
    }

    private Task HandleItemAutocompleteSelected(SubdItem? selected)
    {
        if (selected is null)
        {
            SelectedPrincipal = string.Empty;
            NewItem.ItemCode = string.Empty;
            CurrentSubdItem = null;
            NewItem.SubdItemId = 0;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
            CurrentUnitPrice = null;
            RevalidateIfNeeded();
            _ = OnDraftChanged.InvokeAsync();
            return Task.CompletedTask;
        }
        SelectedPrincipal = selected.CompanyItem?.Principal?.Trim() ?? string.Empty;
        SelectedCategory = selected.CompanyItem?.Category?.Trim() ?? string.Empty;
        NewItem.ItemName = GetItemDisplayLabel(selected);
        NewItem.ItemCode = selected.SubdItemCode; // Auto-fill SKU
        CurrentSubdItem = selected;
        NewItem.SubdItemId = selected.SubdItemId;
        NewItem.ItemsUomId = 0;
        CurrentUom = null;
        CurrentUnitPrice = null;
        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
        return Task.CompletedTask;
    }

    private Task HandleItemNameValueChanged(string? value)
    {
        NewItem.ItemName = value ?? string.Empty;

        if (!TryResolveSelectedItemByName())
        {
            SelectedPrincipal = string.Empty;
            NewItem.ItemCode = string.Empty;
            CurrentSubdItem = null;
            NewItem.SubdItemId = 0;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
            CurrentUnitPrice = null;
        }

        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
        return Task.CompletedTask;
    }

    private async Task OnUomChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out int uomId))
        {
            NewItem.ItemsUomId = uomId;
            CurrentUom = AvailableUoms.FirstOrDefault(u => u.ItemsUomId == uomId);

            if (CurrentSubdItem != null && CurrentUom != null)
            {
                CurrentUnitPrice = await salesInvoiceService.ResolveUomPriceAsync(CurrentUom.ItemsUomId, InvoiceDate);
                NewItem.Amount = CurrentUnitPrice.Value * NewItem.Quantity;
                NewItem.UomName = CurrentUom.UomName;
            }
            else
            {
                CurrentUnitPrice = null;
            }
        }

        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
    }

    private void OnQuantityInput(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var quantity))
        {
            NewItem.Quantity = 0;
            RevalidateIfNeeded();
            _ = OnDraftChanged.InvokeAsync();
            return;
        }

        NewItem.Quantity = quantity;
        if (CurrentUom != null && CurrentUnitPrice.HasValue)
        {
            NewItem.Amount = CurrentUnitPrice.Value * NewItem.Quantity;
        }
        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
    }

    private async Task AddItemToQueue()
    {
        SaveErrorMessage = null;
        ShowValidationErrors = true;
        ValidateDraft();

        if (ValidationErrors.Any())
        {
            return;
        }

        var selectedUom = CurrentUom!;
        CurrentUnitPrice ??= await salesInvoiceService.ResolveUomPriceAsync(selectedUom.ItemsUomId, InvoiceDate);
        var lineTotalPrice = NewItem.Quantity * CurrentUnitPrice.Value;

        var resolvedItemName = CurrentSubdItem?.ItemName ?? NewItem.ItemName;

        var existingItem = modalItems.FirstOrDefault(item =>
            item.SubdItemId == NewItem.SubdItemId &&
            item.ItemsUomId == NewItem.ItemsUomId &&
            item.ItemCode.Equals(NewItem.ItemCode, StringComparison.OrdinalIgnoreCase) &&
            item.ItemName.Equals(resolvedItemName, StringComparison.OrdinalIgnoreCase));

        if (existingItem != null)
        {
            existingItem.Quantity += NewItem.Quantity;
            existingItem.Amount += lineTotalPrice;
        }
        else
        {
            modalItems.Add(new InputItemModel
            {
                ItemCode = NewItem.ItemCode,
                ItemName = resolvedItemName,
                SubdItemId = NewItem.SubdItemId,
                ItemsUomId = NewItem.ItemsUomId,
                Quantity = NewItem.Quantity,
                Amount = lineTotalPrice // Amount based on rules
            });
        }

        ResetNewItem();
        await OnDraftChanged.InvokeAsync();
    }

    private async Task SaveModal()
    {
        SaveErrorMessage = null;

        // Trigger confirmation before saving
        if (OnBeforeSave.HasDelegate)
        {
            await OnBeforeSave.InvokeAsync(modalItems.ToList());
            return;
        }

        if (!modalItems.Any())
        {
            SaveErrorMessage = "Please add at least one item before saving.";
            return;
        }

        await SaveItemsInternal();
    }

    public async Task SaveFromShortcutAsync()
    {
        await SaveModal();
    }

    public async Task SaveItemsInternal()
    {
        if (OnSaveModalItems.HasDelegate)
        {
            await OnSaveModalItems.InvokeAsync(modalItems.ToList());
        }

        modalItems.Clear();
        await OnDraftChanged.InvokeAsync();
        await CloseModal();
    }

    private async Task CancelModal()
    {
        if (OnCancelModal.HasDelegate)
        {
            await OnCancelModal.InvokeAsync();
        }

        await CloseModal();
    }

    private async Task CloseModal()
    {
        if (ShowModalChanged.HasDelegate)
        {
            await ShowModalChanged.InvokeAsync(false);
        }
    }

    private void RemoveQueuedItem(InputItemModel item)
    {
        modalItems.Remove(item);
        if (!modalItems.Any())
        {
            SaveErrorMessage = null;
        }
        _ = OnDraftChanged.InvokeAsync();
    }

    private void ResetNewItem()
    {
        NewItem = CreateNewItem();
        NewItem.Quantity = 1;
        CurrentSubdItem = null;
        CurrentUom = null;
        CurrentUnitPrice = null;
        // Do not clear SelectedPrincipal/SelectedCategory here; selection should persist.
        ShowValidationErrors = false;
        ValidationErrors.Clear();
        SaveErrorMessage = null;
    }

    private static InputItemModel CreateNewItem()
    {
        return new InputItemModel
        {
            Quantity = 1
        };
    }

    private static InputItemModel CloneItem(InputItemModel source)
    {
        return new InputItemModel
        {
            ItemCode = source.ItemCode,
            SubdItemId = source.SubdItemId,
            ItemName = source.ItemName,
            ItemsUomId = source.ItemsUomId,
            UomName = source.UomName,
            Quantity = source.Quantity,
            Amount = source.Amount,
            LineItemId = source.LineItemId
        };
    }

    private async Task HandleSkuKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            if (string.IsNullOrWhiteSpace(NewItem.ItemCode))
            {
                ValidationErrors.Remove(SalesInvoiceValidation.AddItem.Sku.Key);
                ValidationErrors.Remove(SalesInvoiceValidation.AddItem.ItemName.Key);
                skipNextSkuBlurValidation = true;

                await Task.Delay(10);
                if (itemNameAutocomplete != null)
                    await itemNameAutocomplete.GetInputRef().FocusAsync();
                return;
            }

            ShowValidationErrors = true;
            ValidateDraft();
            await Task.Delay(10); // buffer slightly to allow bindings
            if (CurrentSubdItem != null)
            {
                await UomSelect.FocusAsync();
            }
            else
            {
                if (itemNameAutocomplete != null)
                    await itemNameAutocomplete.GetInputRef().FocusAsync();
            }
        }
    }

    private async Task HandleItemNameConfirmed()
    {
        TryResolveSelectedItemByName();
        ShowValidationErrors = true;
        ValidateDraft();

        if (string.IsNullOrWhiteSpace(NewItem.ItemName))
        {
            return;
        }

        await Task.Delay(10);
        await UomSelect.FocusAsync();
    }

    private bool TryResolveSelectedItemByName()
    {
        if (string.IsNullOrWhiteSpace(NewItem.ItemName))
        {
            return false;
        }

        var normalizedItemName = NewItem.ItemName;
        var principalStart = normalizedItemName.LastIndexOf(" (", StringComparison.Ordinal);
        if (principalStart > 0 && normalizedItemName.EndsWith(")", StringComparison.Ordinal))
        {
            normalizedItemName = normalizedItemName[..principalStart];
        }

        var selected = FilteredAvailableItems.FirstOrDefault(i =>
            i.ItemName.Equals(normalizedItemName, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
        {
            return false;
        }

        SelectedPrincipal = selected.CompanyItem?.Principal?.Trim() ?? string.Empty;
        SelectedCategory = selected.CompanyItem?.Category?.Trim() ?? string.Empty;
        CurrentSubdItem = selected;
        NewItem.SubdItemId = selected.SubdItemId;
        NewItem.ItemCode = selected.SubdItemCode;
        NewItem.ItemName = GetItemDisplayLabel(selected);
        NewItem.ItemsUomId = 0;
        CurrentUom = null;
        CurrentUnitPrice = null;
        return true;
    }

    private static string GetItemDisplayLabel(SubdItem item)
    {
        var principal = item.CompanyItem?.Principal;
        return string.IsNullOrWhiteSpace(principal)
            ? item.ItemName
            : $"{item.ItemName} ({principal})";
    }

    private async Task HandleUomKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            ShowValidationErrors = true;
            ValidateDraft();

            if (NewItem.ItemsUomId == 0)
            {
                await OpenSelectDropdownAsync(UomSelect);
                return;
            }

            await Task.Delay(10);
            await QuantityInput.FocusAsync();
        }
    }

    private async Task OpenSelectDropdownAsync(ElementReference selectRef)
    {
        jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
        await jsModule.InvokeVoidAsync("openSelectDropdown", selectRef);
    }

    private async Task HandleQuantityKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            ShowValidationErrors = true;
            ValidateDraft();
            await AddItemToQueue();
            await Task.Delay(10);
            await SkuCodeInput.FocusAsync();
        }
    }

    private Task HandleDraftFieldBlur(FocusEventArgs _)
    {
        if (!HasAnyDraftInput())
        {
            ShowValidationErrors = false;
            ValidationErrors.Clear();
            return Task.CompletedTask;
        }

        ShowValidationErrors = true;
        ValidateDraft();
        return Task.CompletedTask;
    }

    private Task HandleSkuBlur(FocusEventArgs _)
    {
        if (skipNextSkuBlurValidation)
        {
            skipNextSkuBlurValidation = false;
            return Task.CompletedTask;
        }

        if (!HasAnyDraftInput())
        {
            ShowValidationErrors = false;
            ValidationErrors.Clear();
            return Task.CompletedTask;
        }

        ShowValidationErrors = true;
        ValidateDraft();
        return Task.CompletedTask;
    }

    private string GetNormalPriceDisplay()
    {
        return CurrentUom is null
            ? string.Empty
            : FormatHelper.FormatPrice(CurrentUnitPrice ?? CurrentUom.Price);
    }

    private async Task UpdateCurrentUnitPriceAsync()
    {
        if (CurrentUom is null)
        {
            CurrentUnitPrice = null;
            return;
        }

        CurrentUnitPrice = await salesInvoiceService.ResolveUomPriceAsync(CurrentUom.ItemsUomId, InvoiceDate);

        if (NewItem.ItemsUomId == CurrentUom.ItemsUomId && NewItem.Quantity > 0)
        {
            NewItem.Amount = CurrentUnitPrice.Value * NewItem.Quantity;
        }
    }

    private void ValidateDraft()
    {
        ValidationErrors = SalesInvoiceValidation.ValidateAddItemDraft(NewItem, CurrentSubdItem, CurrentUom);
    }

    private void RevalidateIfNeeded()
    {
        if (ShowValidationErrors)
        {
            ValidateDraft();
        }
    }

    private bool HasAnyDraftInput()
    {
        return !string.IsNullOrWhiteSpace(NewItem.ItemCode)
            || !string.IsNullOrWhiteSpace(NewItem.ItemName)
            || NewItem.ItemsUomId != 0
            || NewItem.SubdItemId != 0
            || NewItem.Quantity != 1;
    }

    private string GetFieldError(string fieldKey)
    {
        return ValidationErrors.TryGetValue(fieldKey, out var message) ? message : string.Empty;
    }

}

