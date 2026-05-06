using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using STTproject.Components.Helper;
using STTproject.Components.Shared;
using STTproject.Features.SalesInvoice.Validators;
using STTproject.Models;
using STTproject.Models.Tables;

namespace STTproject.Features.SalesInvoice.Components.Modals;

public partial class AddInvoiceItems
{
    private ElementReference SkuCodeInput;
    private GenericAutocomplete<SubdItem>? itemNameAutocomplete;
    private ElementReference UomSelect;
    private ElementReference QuantityInput;
    private IJSObjectReference? jsModule;
    private bool ShowValidationErrors;
    private bool skipNextSkuBlurValidation;
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
    [Parameter] public EventCallback OnDraftChanged { get; set; }

    private bool _wasModalShown;


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
        }

        if (ShowModal && !_wasModalShown)
        {
            _wasModalShown = true;
            try
            {
                await SkuCodeInput.FocusAsync();
            }
            catch { }
        }
        else if (!ShowModal && _wasModalShown)
        {
            _wasModalShown = false;
        }
    }

    private List<InputItemModel> modalItems = new();
    public InputItemModel NewItem { get; set; } = CreateNewItem();
    private SubdItem? CurrentSubdItem { get; set; }
    private ItemsUom? CurrentUom { get; set; }
    private string SelectedUOM { get; set; } = string.Empty;

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

    public Task RestoreDraftStateAsync(InvoiceItemsDraftState? draft)
    {
        if (draft is null)
        {
            return Task.CompletedTask;
        }

        NewItem = draft.NewItem is null ? CreateNewItem() : CloneItem(draft.NewItem);
        modalItems = draft.ModalItems.Select(CloneItem).ToList();
        CurrentSubdItem = AvailableItems.FirstOrDefault(i =>
            i.SubdItemId == NewItem.SubdItemId && i.SubDistributorId == SelectedSubdistributorId);
        CurrentUom = AvailableUoms.FirstOrDefault(u => u.ItemsUomId == NewItem.ItemsUomId);
        ShowValidationErrors = false;
        skipNextSkuBlurValidation = false;
        ValidationErrors.Clear();
        return Task.CompletedTask;
    }

    private void OnSkuChanged(string sku)
    {
        NewItem.ItemCode = sku;

        var selected = AvailableItems
            .FirstOrDefault(i => i.SubdItemCode.Equals(sku, StringComparison.OrdinalIgnoreCase) && i.SubDistributorId ==
SelectedSubdistributorId);

        if (selected != null)
        {
            CurrentSubdItem = selected;
            NewItem.SubdItemId = selected.SubdItemId;
            NewItem.ItemName = selected.ItemName;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
        }
        else
        {
            CurrentSubdItem = null;
            NewItem.SubdItemId = 0;
            NewItem.ItemName = string.Empty;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
        }

        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
    }

    private void OnItemNameChanged(ChangeEventArgs e)
    {
        var itemName = e.Value?.ToString();
        NewItem.ItemName = itemName ?? string.Empty;

        var selected = AvailableItems
            .FirstOrDefault(i => i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase) && i.SubDistributorId ==
SelectedSubdistributorId);

        if (selected != null)
        {
            NewItem.ItemCode = selected.SubdItemCode; // Auto-fill SKU
            CurrentSubdItem = selected;
            NewItem.SubdItemId = selected.SubdItemId;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
        }
        else
        {
            NewItem.ItemCode = string.Empty;
            CurrentSubdItem = null;
            NewItem.SubdItemId = 0;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
        }

        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
    }

    private Task HandleItemAutocompleteSelected(SubdItem? selected)
    {
        if (selected is null)
        {
            NewItem.ItemCode = string.Empty;
            CurrentSubdItem = null;
            NewItem.SubdItemId = 0;
            NewItem.ItemsUomId = 0;
            CurrentUom = null;
            RevalidateIfNeeded();
            _ = OnDraftChanged.InvokeAsync();
            return Task.CompletedTask;
        }

        NewItem.ItemName = selected.ItemName;
        NewItem.ItemCode = selected.SubdItemCode; // Auto-fill SKU
        CurrentSubdItem = selected;
        NewItem.SubdItemId = selected.SubdItemId;
        NewItem.ItemsUomId = 0;
        CurrentUom = null;
        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
        return Task.CompletedTask;
    }

    private void OnUomChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out int uomId))
        {
            NewItem.ItemsUomId = uomId;
            CurrentUom = AvailableUoms.FirstOrDefault(u => u.ItemsUomId == uomId);

            if (CurrentSubdItem != null && CurrentUom != null)
            {
                NewItem.Amount = CurrentUom.Price * NewItem.Quantity;
                NewItem.UomName = CurrentUom.UomName;
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
        RevalidateIfNeeded();
        _ = OnDraftChanged.InvokeAsync();
    }

    private async Task AddItemToQueue()
    {
        ShowValidationErrors = true;
        ValidateDraft();

        if (ValidationErrors.Any())
        {
            return;
        }

        var selectedUom = CurrentUom!;
        var lineTotalPrice = NewItem.Quantity * selectedUom.Price;

        var existingItem = modalItems.FirstOrDefault(item =>
            item.SubdItemId == NewItem.SubdItemId &&
            item.ItemsUomId == NewItem.ItemsUomId &&
            item.ItemCode.Equals(NewItem.ItemCode, StringComparison.OrdinalIgnoreCase) &&
            item.ItemName.Equals(NewItem.ItemName, StringComparison.OrdinalIgnoreCase));

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
                ItemName = NewItem.ItemName,
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
        if (!modalItems.Any())
        {
            return;
        }

        // Trigger confirmation before saving
        if (OnBeforeSave.HasDelegate)
        {
            await OnBeforeSave.InvokeAsync(modalItems.ToList());
        }
        else
        {
            await SaveItemsInternal();
        }
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
        _ = OnDraftChanged.InvokeAsync();
    }

    private void ResetNewItem()
    {
        NewItem = CreateNewItem();
        NewItem.Quantity = 1;
        CurrentSubdItem = null;
        CurrentUom = null;
        ShowValidationErrors = false;
        ValidationErrors.Clear();
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

    private async Task HandleItemNameKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            ShowValidationErrors = true;
            ValidateDraft();

            if (string.IsNullOrWhiteSpace(NewItem.ItemName))
            {
                if (itemNameAutocomplete != null)
                    await itemNameAutocomplete.OpenPopupAsync();
                return;
            }

            await Task.Delay(10);
            await UomSelect.FocusAsync();
        }
    }

    private async Task HandleItemNameConfirmed()
    {
        ShowValidationErrors = true;
        ValidateDraft();

        if (string.IsNullOrWhiteSpace(NewItem.ItemName))
        {
            return;
        }

        await Task.Delay(10);
        await UomSelect.FocusAsync();
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
        if (jsModule != null)
        {
            await jsModule.InvokeVoidAsync("openSelectDropdown", selectRef);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (jsModule != null)
        {
            try
            {
                await jsModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Circuit disconnected, JS cleanup not possible
            }
            jsModule = null;
        }
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

        ShowValidationErrors = true;
        ValidateDraft();
        return Task.CompletedTask;
    }

    private string GetNormalPriceDisplay()
    {
        return CurrentUom is null
            ? string.Empty
            : FormatHelper.FormatPrice(CurrentUom.Price);
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

    private string GetFieldError(string fieldKey)
    {
        return ValidationErrors.TryGetValue(fieldKey, out var message) ? message : string.Empty;
    }

}

