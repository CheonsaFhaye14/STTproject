using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using STTproject.Data;
using CustomerDataModel = STTproject.Data.Customer;
using STTproject.Features.User.SalesInvoice.Components.Modals;
using STTproject.Features.User.SalesInvoice.Services;
using STTproject.Models;
using STTproject.Services;
using System.Text.Json;
namespace STTproject.Features.User.SalesInvoice.Components.Pages;

public partial class SalesInvoice
{
    private bool showAddItemsModal = false;
    private bool showEditItemsModal = false;
    private bool showCommitConfirmModal = false;
    private bool showAddItemsConfirmModal = false;
    private bool showAddItemsErrorModal = false;
    private bool showImportSalesInvoiceModal = false;
    private bool showEditItemsConfirmModal = false;
    private bool showClearConfirmModal = false;
    private int addItemsConfirmCount = 0;
    private string editItemsConfirmMessage = "";
    private bool showImportConfirmModal = false;
    private bool showImportResultsModal = false;
    private DTOs.ImportSalesInvoiceResult? lastImportResult;
    private AddInvoiceItems? addItemsModalRef;
    private EditInvoiceItems? editItemsModalRef;
    private IJSObjectReference? jsModule;
    private DotNetObjectReference<SalesInvoice>? objRef;
    private bool isInitialDataReady;
    private bool hasAttemptedDraftRestore;

    private bool isLoading = false;

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

    async Task EnableEdit()
    {
        isSaved = false;
        await PersistDraftAsync();
    }

    private static void AssignLineItemIds(List<InputItemModel> itemsToNumber)
    {
        for (var i = 0; i < itemsToNumber.Count; i++)
        {
            itemsToNumber[i].LineItemId = i + 1;
        }
    }

    async Task OpenAddItemsModal()
    {
        showEditItemsModal = false;
        showAddItemsModal = true;
        await PersistDraftAsync();
    }

    private async Task OpenImportFilePicker()
    {
        if (invoice.SubdistributorId <= 0)
        {
            errorMessage = "Please select a subdistributor before importing.";
            showErrorModal = true;
            StateHasChanged();
            return;
        }

        showImportConfirmModal = true;
        StateHasChanged();
    }

    private async Task ConfirmImport()
    {
        showImportConfirmModal = false;
        if (jsModule is null)
        {
            return;
        }

        await jsModule.InvokeVoidAsync("clickElement", "#salesinvoice-import-file");
    }

    private void CancelImport()
    {
        showImportConfirmModal = false;
    }
    private async Task HandleImportSalesInvoiceCompletedAsync()
    {
        await Task.CompletedTask;
    }


    async Task CloseAddItemsModal()
    {
        showAddItemsModal = false;
        await PersistDraftAsync();
    }

    async Task OpenEditItemsModal()
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
        await PersistDraftAsync();
    }

    async Task CloseEditItemsModal()
    {
        showEditItemsModal = false;
        await PersistDraftAsync();
    }

    async Task OnModalItemsSaved(List<InputItemModel> savedItems)
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
        await PersistDraftAsync();
    }

    async Task OnEditedItemsSaved(List<InputItemModel> editedItems)
    {
        items = editedItems;
        showEditItemsModal = false;
        await PersistDraftAsync();
    }

    async Task OnAddItemsBeforeSave(List<InputItemModel> itemsToAdd)
    {
        if (itemsToAdd?.Any() == true)
        {
            addItemsConfirmCount = itemsToAdd.Count;
            showAddItemsConfirmModal = true;
            showAddItemsErrorModal = false;
            return;
        }

        showAddItemsConfirmModal = false;
        showAddItemsErrorModal = true;
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

    private void CloseAddItemsErrorModal()
    {
        showAddItemsErrorModal = false;
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
        var invoiceScope = currentInvoiceId != 0 ? $"invoice:{currentInvoiceId}" : "new";
        return $"salesinvoice-draft:{userId}:subd:{invoice.SubdistributorId}:{invoiceScope}";
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

        SalesInvoiceDraftState? draft;
        try
        {
            draft = JsonSerializer.Deserialize<SalesInvoiceDraftState>(draftJson);
        }
        catch (JsonException)
        {
            await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", GetDraftStorageKey());
            return;
        }

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

    private async Task TryRestoreDraftOnceAsync()
    {
        if (jsModule is null || !isInitialDataReady || hasAttemptedDraftRestore)
        {
            return;
        }

        hasAttemptedDraftRestore = true;
        await RestoreDraftAsync();
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
        if (showImportSalesInvoiceModal)
        {
            showImportSalesInvoiceModal = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        GoBackToHome();
        return Task.CompletedTask;
    }

    private void GoBackToHome()
    {
        Navigation.NavigateTo("/home", forceLoad: true);
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
        catch (Exception ex)
        {
            var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
            errorMessage = $"Unable to commit invoice due to a database error: {baseMsg}";
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

        await ResetAfterSuccessfulCommit();
    }

    private async Task ResetAfterSuccessfulCommit()
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
        await ClearDraftAsync();
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
                await TryRestoreDraftOnceAsync();
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

    // Download template notification
    bool showDownloadSuccess = false;
    string downloadSuccessMessage = string.Empty;

    InputInvoiceModel invoice = new();
    List<InputItemModel> items = new();
    List<SubdItem> subdItems = new();
    List<ItemsUom> availableUoms = new();
    List<SubDistributor> subdList = new();
    List<CustomerDataModel> customers = new();
    private readonly SemaphoreSlim onParametersSetLock = new(1, 1);

    [Parameter]
    public int SubDistributorId { get; set; }

    [Parameter]
    public int? InvoiceId { get; set; }

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
        try
        {
            if (!userContext.UserId.HasValue)
            {
                Navigation.NavigateTo("/");
                return;
            }

            // Reset all modal states when parameters change to ensure clean UI
            showAddItemsModal = false;
            showEditItemsModal = false;
            showCommitConfirmModal = false;
            showAddItemsConfirmModal = false;
            showEditItemsConfirmModal = false;
            showClearConfirmModal = false;
            showErrorModal = false;
            errorMessage = null;
            isSaved = false;
            isInitialDataReady = false;
            hasAttemptedDraftRestore = false;

            var currentUserId = userContext.UserId.Value;
            subdList = await homeService.GetSubDistributorsAsync(currentUserId);

            if (!subdList.Any())
            {
                customers = new();
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
            subdItems = pageData.SubdItems;
            availableUoms = pageData.ItemUoms;

            if (!InvoiceId.HasValue || InvoiceId.Value == 0)
            {
                invoice.SubdistributorId = selectedSubdId;
            }

            if (InvoiceId.HasValue && InvoiceId.Value > 0)
            {
                var selectedCustomer = customers.FirstOrDefault(c => c.CustomerId == invoice.CustomerId);
                if (selectedCustomer != null)
                {
                    invoice.CustomerCode = selectedCustomer.CustomerCode ?? string.Empty;
                    invoice.CustomerName = selectedCustomer.CustomerName;
                    invoice.CustomerType = selectedCustomer.CustomerType ?? string.Empty;
                    invoice.CustomerAddress = string.Join(", ", new[]
                    {
                        selectedCustomer.AddressLine,
                        selectedCustomer.City,
                        selectedCustomer.Province,
                        selectedCustomer.ZipCode?.ToString()
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                }
            }

            isInitialDataReady = true;
            await TryRestoreDraftOnceAsync();

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SalesInvoice._OnParametersSetAsyncInternal EXCEPTION: {ex.GetType().Name} - {ex.Message}");
            throw;
        }
    }

    private string GetComponentKey()
    {
        if (InvoiceId.HasValue && InvoiceId.Value > 0)
        {
            return $"invoice-{InvoiceId.Value}";
        }

        return $"subd-{SubDistributorId}";
    }

    async Task UpdateSubdDisplay(SubDistributor s)
    {
        invoice.SubdistributorId = s.SubDistributorId;

        var pageData = await salesInvoiceService.GetPageDataAsync(s.SubDistributorId);
        subdItems = pageData.SubdItems;
        availableUoms = pageData.ItemUoms;
        await PersistDraftAsync();
    }

    private bool isDownloadingTemplate = false;
    private async Task DownloadTemplate()
    {
        try
        {
            isDownloadingTemplate = true;
            await InvokeAsync(StateHasChanged);
            await Task.Yield();

            var customerRows = customers
                .Where(c => !string.IsNullOrWhiteSpace(c.CustomerCode) && c.SubDistributorId == invoice.SubdistributorId)
                .Select(c => (Code: c.CustomerCode!, Name: c.CustomerName ?? string.Empty))
                .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Code)
                .ToList();

            var skuRows = subdItems
                .Where(i => !string.IsNullOrWhiteSpace(i.SubdItemCode) && i.SubDistributorId == invoice.SubdistributorId)
                .Select(i => (Code: i.SubdItemCode!, Name: i.ItemName ?? string.Empty))  // adjust ItemName property if named differently
                .GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(i => i.Code)
                .ToList();

            var uoms = availableUoms
                .Select(u => u.UomName ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

            // Price keyed by SkuCode + UOM — needs the item code, not just SubdItemId, so join against subdItems.
            var priceRows = availableUoms
                .Join(subdItems,
                    uom => uom.SubdItemId,
                    item => item.SubdItemId,
                    (uom, item) => (SkuCode: item.SubdItemCode ?? string.Empty, Uom: uom.UomName, Price: uom.Price))
                .Where(p => !string.IsNullOrWhiteSpace(p.SkuCode) && !string.IsNullOrWhiteSpace(p.Uom))
                .ToList();

            var SubDistributorName = subdList.FirstOrDefault(s => s.SubDistributorId == invoice.SubdistributorId)?.SubdName ?? "UnknownSubDistributor";

            await downloadTemplateService.GenerateAndDownloadExcelAsync(customerRows, skuRows, uoms, priceRows, SubDistributorName);

            downloadSuccessMessage = "Template downloaded successfully.";
            showDownloadSuccess = true;
            StateHasChanged();
            await Task.Delay(3000);
            showDownloadSuccess = false;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
            errorMessage = $"Failed to generate template: {baseMsg}";
            showErrorModal = true;
        }
        finally
        {
            isDownloadingTemplate = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task HandleDownloadErrorReport()
    {
        if (lastImportResult == null) return;

        var bytes = downloadTemplateService.GenerateErrorReportExcel(lastImportResult);
        var fileName = $"SalesInvoice-Import-Errors-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";

        await JS.InvokeVoidAsync(
            "downloadFileFromBytes",
            fileName,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Convert.ToBase64String(bytes));
    }
}

