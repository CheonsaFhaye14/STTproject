using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using STTproject.Data;
using STTproject.Features.SalesInvoice.Components.Modals;
using STTproject.Models;
using STTproject.Services;
using System.Text.Json;
namespace STTproject.Features.SalesInvoice.Components.Pages;

public partial class SalesInvoice
{
    private bool showAddItemsModal = false;
    private bool showEditItemsModal = false;
    private bool showCommitConfirmModal = false;
    private bool showAddItemsConfirmModal = false;
    private bool showEditItemsConfirmModal = false;
    private bool showClearConfirmModal = false;
    private int addItemsConfirmCount = 0;
    private string editItemsConfirmMessage = "";
    private AddInvoiceItems? addItemsModalRef;
    private EditInvoiceItems? editItemsModalRef;
    private IJSObjectReference? jsModule;
    private DotNetObjectReference<SalesInvoice>? objRef;

    async Task OnItemsChanged(List<InputItemModel> updatedItems)
    {
        items = updatedItems;
        await PersistDraftAsync();
    }

    int currentInvoiceId = 0;
    async Task SaveDraft()
    {
        if (!isSaved)
        {
            errorMessage = null;
            isSaved = true;
        }

        await PersistDraftAsync();
    }

    void EnableEdit()
    {
        isSaved = false;
        _ = PersistDraftAsync();
    }

    private static void AssignLineItemIds(List<InputItemModel> itemsToNumber)
    {
        for (var i = 0; i < itemsToNumber.Count; i++)
        {
            itemsToNumber[i].LineItemId = i + 1;
        }
    }

    void OpenAddItemsModal()
    {
        showEditItemsModal = false;
        showAddItemsModal = true;
        _ = PersistDraftAsync();
    }

    void CloseAddItemsModal()
    {
        showAddItemsModal = false;
        _ = PersistDraftAsync();
    }

    void OpenEditItemsModal()
    {
        if (!items.Any())
        {
            return;
        }

        // assign line item ids matching the table order so Edit modal shows the same numbers
        for (var i = 0; i < items.Count; i++)
        {
            items[i].LineItemId = i + 1;
        }

        showAddItemsModal = false;
        showEditItemsModal = true;
        _ = PersistDraftAsync();
    }

    void CloseEditItemsModal()
    {
        showEditItemsModal = false;
        _ = PersistDraftAsync();
    }

    Task OnModalItemsSaved(List<InputItemModel> savedItems)
    {
        if (savedItems?.Any() == true)
        {
            var nextLineItemId = items.Count > 0 ? items.Max(i => i.LineItemId) + 1 : 1;

            foreach (var savedItem in savedItems)
            {
                var existingItem = items.FirstOrDefault(item =>
                    item.SubdItemId == savedItem.SubdItemId &&
                    item.ItemsUomId == savedItem.ItemsUomId &&
                    item.ItemCode.Equals(savedItem.ItemCode, StringComparison.OrdinalIgnoreCase) &&
                    item.ItemName.Equals(savedItem.ItemName, StringComparison.OrdinalIgnoreCase));

                if (existingItem != null)
                {
                    existingItem.Quantity += savedItem.Quantity;
                    existingItem.Amount += savedItem.Amount;
                }
                else
                {
                    savedItem.LineItemId = nextLineItemId;
                    nextLineItemId++;
                    items.Add(savedItem);
                }
            }
        }

        showAddItemsModal = false;
        _ = PersistDraftAsync();
        return Task.CompletedTask;
    }

    Task OnEditedItemsSaved(List<InputItemModel> editedItems)
    {
        items = editedItems;
        showEditItemsModal = false;
        _ = PersistDraftAsync();
        return Task.CompletedTask;
    }

    async Task OnAddItemsBeforeSave(List<InputItemModel> itemsToAdd)
    {
        if (itemsToAdd?.Any() == true)
        {
            addItemsConfirmCount = itemsToAdd.Count;
            showAddItemsConfirmModal = true;
        }
        await Task.CompletedTask;
    }

    private async Task ConfirmAddItems()
    {
        showAddItemsConfirmModal = false;
        if (addItemsModalRef != null)
        {
            await addItemsModalRef.SaveItemsInternal();
        }
    }

    private void CancelAddItemsConfirm()
    {
        showAddItemsConfirmModal = false;
        addItemsConfirmCount = 0;
    }

    async Task OnEditItemsBeforeSave(List<InputItemModel> itemsToSave)
    {
        if (editItemsModalRef != null)
        {
            // Calculate deleted and modified items
            int deletedCount = editItemsModalRef.GetDeletedItemCount();
            int modifiedCount = editItemsModalRef.GetModifiedItemCount();

            // Build message based on changes
            if (deletedCount == 0 && modifiedCount == 0)
            {
                // No changes, don't show confirmation
                await Task.CompletedTask;
                return;
            }

            var messageParts = new List<string>();
            if (deletedCount > 0)
                messageParts.Add($"Removed {deletedCount} item{(deletedCount != 1 ? "s" : "")}");
            if (modifiedCount > 0)
                messageParts.Add($"Changed {modifiedCount} item{(modifiedCount != 1 ? "s" : "")}");

            editItemsConfirmMessage = string.Join(" and ", messageParts) + "?";
            showEditItemsConfirmModal = true;
        }
        await Task.CompletedTask;
    }

    private async Task ConfirmEditItems()
    {
        showEditItemsConfirmModal = false;
        if (editItemsModalRef != null)
        {
            var mergedItems = EditInvoiceItems.MergeMatchingItems(editItemsModalRef.EditableItems);
            await editItemsModalRef.SaveItemsInternal(mergedItems);
        }
    }

    private void CancelEditItemsConfirm()
    {
        showEditItemsConfirmModal = false;
        editItemsConfirmMessage = "";
    }

    private string GetDraftStorageKey()
    {
        var userId = userContext.UserId ?? 0;
        var subDistributorId = invoice.SubdistributorId != 0 ? invoice.SubdistributorId : SubDistributorId;
        var invoiceScope = currentInvoiceId != 0 ? $"invoice:{currentInvoiceId}" : "new";
        return $"salesinvoice-draft:{userId}:subd:{subDistributorId}:{invoiceScope}";
    }

    private async Task PersistDraftAsync()
    {
        if (jsModule is null)
        {
            return;
        }

        var draft = new SalesInvoiceDraftState
        {
            Invoice = invoice,
            Items = items,
            AddItemsDraft = addItemsModalRef?.CaptureDraftState(),
            IsSaved = isSaved,
            CurrentInvoiceId = currentInvoiceId
        };

        var draftJson = JsonSerializer.Serialize(draft);
        await jsModule.InvokeVoidAsync("saveSalesInvoiceDraft", GetDraftStorageKey(), draftJson);
    }

    private async Task RestoreDraftAsync()
    {
        if (jsModule is null)
        {
            return;
        }

        var draftJson = await jsModule.InvokeAsync<string?>("loadSalesInvoiceDraft", GetDraftStorageKey());
        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return;
        }

        var draft = JsonSerializer.Deserialize<SalesInvoiceDraftState>(draftJson);
        if (draft?.Invoice is null)
        {
            return;
        }

        invoice = draft.Invoice;
        items = draft.Items ?? new List<InputItemModel>();
        AssignLineItemIds(items);
        isSaved = draft.IsSaved;
        currentInvoiceId = draft.CurrentInvoiceId;
        StateHasChanged();

        if (addItemsModalRef is not null)
        {
            await addItemsModalRef.RestoreDraftStateAsync(draft.AddItemsDraft);
        }
    }

    private void ShowClearConfirmModal()
    {
        showClearConfirmModal = true;
    }

    private async Task ConfirmClearDraft()
    {
        showClearConfirmModal = false;

        if (jsModule is null)
        {
            return;
        }

        // Clear localStorage
        await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", GetDraftStorageKey());

        // Reset UI state
        invoice = new InputInvoiceModel
        {
            SubdistributorId = invoice.SubdistributorId
        };
        items = new List<InputItemModel>();
        isSaved = false;
        currentInvoiceId = 0;

        if (addItemsModalRef is not null)
        {
            await addItemsModalRef.RestoreDraftStateAsync(null);
        }

        StateHasChanged();
    }

    private void CancelClearConfirm()
    {
        showClearConfirmModal = false;
    }

    private async Task ClearDraftAsync()
    {
        if (jsModule is null)
        {
            return;
        }

        await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", GetDraftStorageKey());
    }

    private sealed class SalesInvoiceDraftState
    {
        public InputInvoiceModel? Invoice { get; set; }
        public List<InputItemModel> Items { get; set; } = new();
        public InvoiceItemsDraftState? AddItemsDraft { get; set; }
        public bool IsSaved { get; set; }
        public int CurrentInvoiceId { get; set; }
    }

    [JSInvokable]
    public Task OpenAddItemsModalFromShortcut()
    {
        if (isSaved)
        {
            if (showAddItemsModal)
            {
                showAddItemsModal = false;
            }
            else
            {
                showAddItemsModal = true;
                showEditItemsModal = false;
            }

            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task ToggleEditItemsModalFromShortcut()
    {
        if (isSaved)
        {
            if (!showEditItemsModal && !items.Any())
            {
                return Task.CompletedTask;
            }

            showEditItemsModal = !showEditItemsModal;
            if (showEditItemsModal)
            {
                showAddItemsModal = false;
            }

            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task SaveOpenModalFromShortcut()
    {
        if (!isSaved)
        {
            return;
        }

        if (showAddItemsModal && addItemsModalRef != null)
        {
            await addItemsModalRef.SaveFromShortcutAsync();
            return;
        }

        if (showEditItemsModal && editItemsModalRef != null)
        {
            await editItemsModalRef.SaveFromShortcutAsync();
            return;
        }

        ShowCommitInvoiceConfirm();
        StateHasChanged();
    }

    [JSInvokable]
    public Task EscapeActionFromShortcut()
    {
        if (showCommitConfirmModal)
        {
            showCommitConfirmModal = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        if (showAddItemsModal)
        {
            showAddItemsModal = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        if (showEditItemsModal)
        {
            showEditItemsModal = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        GoBackToHome();
        return Task.CompletedTask;
    }

    private void GoBackToHome()
    {
        Navigation.NavigateTo("/home");
    }

    private void ShowCommitInvoiceConfirm()
    {
        if (!items.Any())
        {
            errorMessage = "Add at least one item before committing the invoice.";
            showErrorModal = true;
            return;
        }

        errorMessage = null;
        showCommitConfirmModal = true;
    }

    private void CancelCommitInvoice()
    {
        showCommitConfirmModal = false;
    }

    private async Task ConfirmCommitInvoice()
    {
        showCommitConfirmModal = false;
        await CommitInvoice();
    }

    private void CloseErrorModal()
    {
        showErrorModal = false;
        errorMessage = null;
    }

    private async Task CommitInvoice()
    {
        if (!items.Any())
        {
            errorMessage = "Add at least one item before committing the invoice.";
            showErrorModal = true;
            return;
        }

        errorMessage = null;

        SaveInvoiceResult result;
        try
        {
            result = await salesInvoiceService.SaveInvoiceAsync(invoice, items, currentInvoiceId, userContext.UserId ?? 0);
        }
        catch (Exception)
        {
            errorMessage = "Unable to commit invoice due to a database error.";
            showErrorModal = true;
            return;
        }

        if (result.IsDuplicate)
        {
            errorMessage = "Duplicate sales invoice code!";
            showErrorModal = true;
            return;
        }

        if (!result.IsSaved)
        {
            errorMessage = result.ErrorMessage ?? "Unable to commit invoice.";
            showErrorModal = true;
            return;
        }

        ResetAfterSuccessfulCommit();
    }

    private void ResetAfterSuccessfulCommit()
    {
        var selectedSubdistributorId = invoice.SubdistributorId;

        invoice = new InputInvoiceModel
        {
            SubdistributorId = selectedSubdistributorId
        };

        items = new List<InputItemModel>();
        currentInvoiceId = 0;
        isSaved = false;
        showAddItemsModal = false;
        showEditItemsModal = false;
        showCommitConfirmModal = false;
        errorMessage = null;
        _ = ClearDraftAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            objRef = DotNetObjectReference.Create(this);
            try
            {
                jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
                await jsModule.InvokeVoidAsync("registerF3", objRef);
                await RestoreDraftAsync();
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (JSDisconnectedException)
            {
                return;
            }
            catch (JSException)
            {
                return;
            }
        }

        if (jsModule != null)
        {
            var activeModalSelector = showAddItemsModal
                ? "#add-items-modal-overlay .modal-box"
                : showEditItemsModal
                    ? "#edit-items-modal-overlay .modal-box"
                    : showCommitConfirmModal
                        ? "#commit-invoice-modal-overlay .modal-box"
                        : string.Empty;

            try
            {
                if (!string.IsNullOrWhiteSpace(activeModalSelector))
                {
                    await jsModule.InvokeVoidAsync("activateModalFocusTrap", activeModalSelector);
                }
                else
                {
                    await jsModule.InvokeVoidAsync("deactivateModalFocusTrap");
                }
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (JSDisconnectedException)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (jsModule != null)
        {
            try
            {
                await jsModule.InvokeVoidAsync("deactivateModalFocusTrap");
                await jsModule.InvokeVoidAsync("unregisterF3");
                await jsModule.DisposeAsync();
            }
            catch (TaskCanceledException)
            {
            }
            catch (JSDisconnectedException)
            {
            }
            finally
            {
                jsModule = null;
            }
        }

        objRef?.Dispose();
    }

    bool isSaved = false;
    string? errorMessage;
    bool showErrorModal = false;

    [Parameter]
    public int SubDistributorId { get; set; }

    [Parameter]
    public int? InvoiceId { get; set; }

    InputInvoiceModel invoice = new();
    List<InputItemModel> items = new();
    List<SubdItem> subdItems = new();
    List<ItemsUom> availableUoms = new();
    List<SubDistributor> subdList = new();
    List<Customer> customers = new();
    List<CustomerBranch> customerBranches = new();
    private readonly SemaphoreSlim onParametersSetLock = new(1, 1);

    protected override async Task OnParametersSetAsync()
    {
        // Use semaphore to ensure only one OnParametersSetAsync execution at a time
        // This prevents concurrent DbContext access when parameters change rapidly
        if (!await onParametersSetLock.WaitAsync(0))
        {
            // Another execution is already in progress, skip this one
            return;
        }

        try
        {
            await _OnParametersSetAsyncInternal();
        }
        finally
        {
            onParametersSetLock.Release();
        }
    }

    private async Task _OnParametersSetAsyncInternal()
    {
        if (!userContext.UserId.HasValue)
        {
            Navigation.NavigateTo("/");
            return;
        }

        var currentUserId = userContext.UserId.Value;
        subdList = await homeService.GetSubDistributorsAsync(currentUserId);

        if (!subdList.Any())
        {
            customers = new();
            customerBranches = new();
            subdItems = new();
            availableUoms = new();
            invoice.SubdistributorId = 0;
            return;
        }

        var selectedSubdId = SubDistributorId;

        if (InvoiceId.HasValue && InvoiceId.Value > 0)
        {
            var invoiceData = await salesInvoiceService.GetInvoiceByIdAsync(InvoiceId.Value);
            if (invoiceData.HasValue && invoiceData.Value.Invoice != null)
            {
                var loadedInvoice = invoiceData.Value.Invoice;
                invoice = loadedInvoice;
                items = invoiceData.Value.Items;
                AssignLineItemIds(items);
                currentInvoiceId = InvoiceId.Value;
                isSaved = true;
                selectedSubdId = invoice.SubdistributorId;
            }
        }

        if (!subdList.Any(s => s.SubDistributorId == selectedSubdId))
        {
            selectedSubdId = subdList.FirstOrDefault()?.SubDistributorId ?? 0;
        }

        var pageData = await salesInvoiceService.GetPageDataAsync(selectedSubdId);
        customers = pageData.Customers;
        customerBranches = pageData.CustomerBranches;
        subdItems = pageData.SubdItems;
        availableUoms = pageData.ItemUoms;

        if (!InvoiceId.HasValue || InvoiceId.Value == 0)
        {
            invoice.SubdistributorId = selectedSubdId;
            return;
        }

        var selectedCustomer = customers.FirstOrDefault(c => c.CustomerId == invoice.CustomerId);
        if (selectedCustomer != null)
        {
            invoice.CustomerCode = selectedCustomer.CustomerCode ?? string.Empty;
            invoice.CustomerName = selectedCustomer.CustomerName;
            invoice.CustomerType = selectedCustomer.CustomerType ?? string.Empty;
        }

        var selectedBranch = customerBranches.FirstOrDefault(cb => cb.CustomerBranchId == invoice.CustomerBranchId);
        if (selectedBranch != null)
        {
            invoice.CustomerAddress = string.Join(", ", new[]
            {
                selectedBranch.AddressLine,
                selectedBranch.City,
                selectedBranch.Province,
                selectedBranch.ZipCode.ToString()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    async Task UpdateSubdDisplay(SubDistributor s)
    {
        invoice.SubdistributorId = s.SubDistributorId;

        var pageData = await salesInvoiceService.GetPageDataAsync(s.SubDistributorId);
        subdItems = pageData.SubdItems;
        availableUoms = pageData.ItemUoms;
        await PersistDraftAsync();
    }

}

