using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using STTproject.Features.Pricing;
using STTproject.Features.User.SalesInvoice.Validators;
using STTproject.Models;
using STTproject.Data;

namespace STTproject.Features.User.SalesInvoice.Components.Modals;

public partial class EditInvoiceItems
{
    [Parameter] public bool ShowModal { get; set; }
    [Parameter] public EventCallback<bool> ShowModalChanged { get; set; }
    [Parameter] public EventCallback OnCancelModal { get; set; }
    [Parameter] public EventCallback<List<InputItemModel>> OnSaveEditedItems { get; set; }
    [Parameter] public EventCallback<List<InputItemModel>> OnBeforeSave { get; set; }
    [Parameter] public List<InputItemModel> Items { get; set; } = new();
    [Parameter] public List<SubdItem> AvailableItems { get; set; } = new();
    [Parameter] public List<ItemsUom> AvailableUoms { get; set; } = new();
    [Parameter] public DateOnly InvoiceDate { get; set; }
    [Parameter] public IReadOnlyDictionary<int, UomPriceInfo> UomPrices { get; set; } = new Dictionary<int, UomPriceInfo>();
    [Parameter] public bool IsCredit { get; set; }

    private ElementReference itemNameSelectRef;
    private ElementReference uomSelectRef;
    private ElementReference quantityInputRef;
    private ElementReference cancelButtonRef;
    private ElementReference saveButtonRef;
    private IJSObjectReference? jsModule;
    private Dictionary<string, string> ValidationErrors { get; set; } = new();
    private bool uomEnterPrimed;

    private bool wasShown;
    private bool focusItemNameOnRender;
    private List<InputItemModel> editableItems = new();
    private int selectedItemIndex;

    // Public property to expose editableItems for confirmation
    public List<InputItemModel> EditableItems => editableItems;

    private InputItemModel? SelectedItem =>
        selectedItemIndex >= 0 && selectedItemIndex < editableItems.Count
            ? editableItems[selectedItemIndex]
            : null;

    protected override void OnParametersSet()
    {
        if (ShowModal && !wasShown)
        {
            editableItems = Items.Select(CloneItem).ToList();
            foreach (var item in editableItems.Where(i => i.Quantity <= 0))
            {
                item.Quantity = 1;
            }
            selectedItemIndex = -1;
            ValidationErrors.Clear();
            wasShown = true;
            focusItemNameOnRender = true;
            uomEnterPrimed = false;
        }
        else if (!ShowModal)
        {
            wasShown = false;
            focusItemNameOnRender = false;
            uomEnterPrimed = false;
        }

        if (ShowModal && editableItems.Any())
        {
            RecalculateAmounts();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
        }

        if (ShowModal && focusItemNameOnRender && editableItems.Any())
        {
            focusItemNameOnRender = false;
            await itemNameSelectRef.FocusAsync();
        }
    }

    // ── Pricing ──────────────────────────────────────────────────────────────

    private IEnumerable<ItemsUom> GetSellableUoms(InputItemModel item)
    {
        // Active UOMs of this item, plus the line's current one so it still displays if it was deactivated
        return AvailableUoms
            .Where(u => u.SubdItemId == item.SubdItemId && (u.IsActive || u.ItemsUomId == item.ItemsUomId))
            .OrderBy(u => u.ConversionToBase);
    }

    private string GetUomOptionLabel(ItemsUom uom)
    {
        if (!uom.IsActive) return $"{uom.UomName} (inactive)";
        var priced = UomPrices.TryGetValue(uom.ItemsUomId, out var p) && p.IsPriced;
        return priced ? uom.UomName : $"{uom.UomName} (no price)";
    }

    private void RepriceLine(InputItemModel item)
    {
        if (item.Quantity > 0
            && UomPrices.TryGetValue(item.ItemsUomId, out var p)
            && p.SubdItemId == item.SubdItemId
            && p.IsPriced)
        {
            item.Amount = p.AmountFor(item.Quantity, IsCredit);
            item.PriceIssue = null;
            return;
        }

        item.Amount = 0m;

        if (item.Quantity <= 0)
        {
            item.PriceIssue = null;
            return;
        }

        UomPrices.TryGetValue(item.ItemsUomId, out var info);
        item.PriceIssue = info?.Issue ?? "This unit of measure is not available for this item.";
    }

    private void RecalculateAmounts()
    {
        foreach (var item in editableItems)
        {
            RepriceLine(item);
        }
    }

    // ── Field handlers ───────────────────────────────────────────────────────

    private string GetItemNameOptionLabel(InputItemModel item)
    {
        var uomName = AvailableUoms.FirstOrDefault(u => u.ItemsUomId == item.ItemsUomId)?.UomName ?? item.UomName;
        return $"[{item.LineItemId}] {item.ItemName} ({uomName})";
    }

    private void OnSelectedItemChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var index) && index >= 0 && index < editableItems.Count)
        {
            selectedItemIndex = index;
            uomEnterPrimed = false;
            ValidateDraft();
        }
    }

    private Task OnUomChanged(ChangeEventArgs e)
    {
        var item = SelectedItem;
        if (item is null || !int.TryParse(e.Value?.ToString(), out var uomId))
        {
            return Task.CompletedTask;
        }

        item.ItemsUomId = uomId;
        item.UomName = AvailableUoms.FirstOrDefault(u => u.ItemsUomId == uomId)?.UomName ?? string.Empty;
        RepriceLine(item);

        uomEnterPrimed = false;
        ValidateDraft();
        return Task.CompletedTask;
    }

    private Task OnQuantityInput(ChangeEventArgs e)
    {
        var item = SelectedItem;
        if (item is null)
        {
            return Task.CompletedTask;
        }

        item.Quantity = int.TryParse(e.Value?.ToString(), out var quantity) ? quantity : 0;
        RepriceLine(item);
        ValidateDraft();
        return Task.CompletedTask;
    }

    private void RemoveSelectedItem()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        editableItems.Remove(item);
        ValidationErrors.Clear();

        if (!editableItems.Any())
        {
            selectedItemIndex = -1;
            StateHasChanged();
            return;
        }

        if (selectedItemIndex >= editableItems.Count)
        {
            selectedItemIndex = editableItems.Count - 1;
        }
    }

    // ── Keyboard flow ────────────────────────────────────────────────────────

    private async Task HandleItemNameKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            if (selectedItemIndex < 0)
            {
                ValidationErrors[SalesInvoiceValidation.EditItem.ItemName.Key] = SalesInvoiceValidation.EditItem.ItemName.ErrorMessage;
                await OpenSelectDropdownAsync(itemNameSelectRef);
                return;
            }

            ValidateDraft();

            await Task.Delay(10);
            if (SelectedItem != null)
            {
                try { await uomSelectRef.FocusAsync(); } catch { /* ignore focus errors */ }
            }
            else
            {
                try { await cancelButtonRef.FocusAsync(); } catch { /* ignore focus errors */ }
            }
        }
    }

    private async Task HandleUomKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            var item = SelectedItem;
            if (item == null)
            {
                return;
            }

            if (!uomEnterPrimed)
            {
                uomEnterPrimed = true;
                await OpenSelectDropdownAsync(uomSelectRef);
                return;
            }

            uomEnterPrimed = false;

            if (item.ItemsUomId == 0)
            {
                ValidationErrors[SalesInvoiceValidation.EditItem.Uom.Key] = SalesInvoiceValidation.EditItem.Uom.ErrorMessage;
                await OpenSelectDropdownAsync(uomSelectRef);
                return;
            }

            ValidateDraft();

            await Task.Delay(10);
            await quantityInputRef.FocusAsync();
        }
    }

    private async Task HandleQuantityKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            var item = SelectedItem;
            if (item is null)
            {
                return;
            }

            if (item.Quantity <= 0)
            {
                ValidationErrors[SalesInvoiceValidation.EditItem.Quantity.Key] = SalesInvoiceValidation.EditItem.Quantity.ErrorMessage;
                return;
            }

            ValidateDraft();

            await Task.Delay(10);
            await saveButtonRef.FocusAsync();
        }
    }

    private Task HandleItemNameBlur(FocusEventArgs _)
    {
        if (selectedItemIndex < 0)
        {
            ValidationErrors[SalesInvoiceValidation.EditItem.ItemName.Key] = SalesInvoiceValidation.EditItem.ItemName.ErrorMessage;
            return Task.CompletedTask;
        }

        ValidateDraft();
        return Task.CompletedTask;
    }

    private Task HandleUomBlur(FocusEventArgs _)
    {
        uomEnterPrimed = false;

        if (SelectedItem is null)
        {
            return Task.CompletedTask;
        }

        ValidateDraft();
        return Task.CompletedTask;
    }

    private Task HandleQuantityBlur(FocusEventArgs _)
    {
        if (SelectedItem is null)
        {
            return Task.CompletedTask;
        }

        ValidateDraft();
        return Task.CompletedTask;
    }

    private async Task HandleRemoveKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await Task.Delay(10);
            try { await cancelButtonRef.FocusAsync(); } catch { /* ignore focus errors */ }
        }
    }

    private async Task HandleCancelKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await Task.Delay(10);
            try { await saveButtonRef.FocusAsync(); } catch { /* ignore focus errors */ }
        }
    }

    private async Task HandleSaveKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SaveModal();
        }
    }

    private async Task OpenSelectDropdownAsync(ElementReference selectRef)
    {
        if (jsModule != null)
        {
            await jsModule.InvokeVoidAsync("openSelectDropdown", selectRef);
        }
    }

    // ── Save / close ─────────────────────────────────────────────────────────

    private async Task SaveModal()
    {
        if (editableItems.Any())
        {
            ValidateDraft();

            if (ValidationErrors.Any())
            {
                return;
            }

            var unpriced = editableItems.FirstOrDefault(i => i.PriceIssue is not null);
            if (unpriced is not null)
            {
                selectedItemIndex = editableItems.IndexOf(unpriced);
                ValidationErrors[SalesInvoiceValidation.EditItem.Uom.Key] = $"No price: {unpriced.PriceIssue}";
                return;
            }
        }

        var mergedItems = MergeMatchingItems(editableItems);

        if (OnBeforeSave.HasDelegate)
        {
            await OnBeforeSave.InvokeAsync(mergedItems);
        }
        else
        {
            await SaveItemsInternal(mergedItems);
        }
    }

    public async Task SaveFromShortcutAsync()
    {
        await SaveModal();
    }

    public async Task SaveItemsInternal(List<InputItemModel> mergedItems)
    {
        if (OnSaveEditedItems.HasDelegate)
        {
            await OnSaveEditedItems.InvokeAsync(mergedItems);
        }

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
            LineItemId = source.LineItemId,
            PriceIssue = source.PriceIssue
        };
    }

    // Amounts are summed here, but the page re-prices every line after saving,
    // so a merged line is always priced once from its total quantity.
    public static List<InputItemModel> MergeMatchingItems(IEnumerable<InputItemModel> source)
    {
        return source
            .Where(item => item.Quantity > 0)
            .GroupBy(item => new
            {
                item.SubdItemId,
                item.ItemsUomId,
                ItemCode = item.ItemCode.ToUpperInvariant(),
                ItemName = item.ItemName.ToUpperInvariant()
            })
            .Select(group =>
            {
                var first = group.First();
                return new InputItemModel
                {
                    ItemCode = first.ItemCode,
                    SubdItemId = first.SubdItemId,
                    ItemName = first.ItemName,
                    ItemsUomId = first.ItemsUomId,
                    UomName = first.UomName,
                    Quantity = group.Sum(x => x.Quantity),
                    Amount = group.Sum(x => x.Amount),
                    LineItemId = first.LineItemId
                };
            })
            .ToList();
    }

    public int GetDeletedItemCount()
    {
        return Items.Count(item =>
            !editableItems.Any(e =>
                e.SubdItemId == item.SubdItemId &&
                e.ItemCode.Equals(item.ItemCode, StringComparison.OrdinalIgnoreCase)));
    }

    public int GetModifiedItemCount()
    {
        return editableItems.Count(item =>
        {
            var originalItem = Items.FirstOrDefault(i =>
                i.SubdItemId == item.SubdItemId &&
                i.ItemCode.Equals(item.ItemCode, StringComparison.OrdinalIgnoreCase));
            return originalItem != null &&
                   (item.Quantity != originalItem.Quantity || item.ItemsUomId != originalItem.ItemsUomId);
        });
    }

    private void ValidateDraft()
    {
        ValidationErrors = SalesInvoiceValidation.ValidateEditItemDraft(SelectedItem, SelectedItem != null);
    }

    private string GetFieldError(string fieldKey)
    {
        return ValidationErrors.TryGetValue(fieldKey, out var message) ? message : string.Empty;
    }
}