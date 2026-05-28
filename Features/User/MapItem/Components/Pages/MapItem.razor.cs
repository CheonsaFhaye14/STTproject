using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using STTproject.Data;
using STTproject.Features.User.MapItem.Components.Sections;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Features.User.MapItem.Validators;
using STTproject.Models;
using STTproject.Services;
using STTproject.Shared.Helper;

namespace STTproject.Features.User.MapItem.Components.Pages
{
    public partial class MapItem
    {
        private const string BaseUomName = "PC";

        private bool showAddUomModal = false;
        private string? modalCompanyItemCode;
        private string? modalCompanyItemName;
        private int? selectedCompanyItemId;
        private string? selectedPrincipal;
        private string? itemCode;
        private string? itemName;
        private IJSObjectReference? jsModule;
        private IJSObjectReference? mapItemJsModule;
        private DotNetObjectReference<MapItem>? objRef;
        private bool principalEnterPrimed = false;
        private List<string> availableUoms = new();
        private Dictionary<string, UomEntry> uomEntries = new();
        private Dictionary<string, string> validationErrors = new();
        private int? editingSubdItemId;
        private bool showConfirmModal = false;
        private string modalTitle = string.Empty;
        private string modalMessage = string.Empty;
        private ConfirmActionKind pendingConfirmAction = ConfirmActionKind.None;
        private MapSubDistributorItemRow? pendingItem;
        private string? itemActionErrorMessage;
        private string? confirmedCompanyItemSummary;
        private bool showErrorModal = false;
        private bool showDownloadTemplateModal = false;
        private bool showImportConfirmModal = false;
        private bool showImportDetailsModal = false;
        private bool showClearConfirmModal = false;
        private ImportMapItemResult? lastImportResult;
        private HashSet<string> selectedImportItemGroupKeys = new(StringComparer.OrdinalIgnoreCase);
        private bool shouldHydrateClientStateAfterRender = false;
        private bool hasHydratedClientState = false;
        [Parameter] public EventCallback OnDraftChanged { get; set; }

        private async Task HandleDownloadTemplateSubmit((int SubdistributorId, string? Principal) filters)
        {
            showDownloadTemplateModal = false;
            try
            {
                var templateData = await mapItemService.GetTemplateDataAsync(filters.SubdistributorId, filters.Principal);
                await downloadTemplateService.GenerateAndDownloadExcelAsync(templateData);
            }
            catch (Exception ex)
            {
                itemActionErrorMessage = $"Failed to generate template: {ex.Message}";
                showErrorModal = true;
            }
        }

        private Task HandleDownloadTemplateCancel()
        {
            showDownloadTemplateModal = false;
            return Task.CompletedTask;
        }

        private async Task OpenImportFilePicker()
        {
            jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
            await jsModule.InvokeVoidAsync("clickElement", "#mapitem-import-file");
        }

        private void ShowImportConfirmModal()
        {
            showImportConfirmModal = true;
        }

        private async Task ConfirmImport()
        {
            showImportConfirmModal = false;
            await OpenImportFilePicker();
        }

        private Task CancelImport()
        {
            showImportConfirmModal = false;
            return Task.CompletedTask;
        }

        private async Task HandleImportFileSelected(InputFileChangeEventArgs e)
        {
            var file = e.File;
            if (file is null)
            {
                return;
            }

            var fileName = file.Name ?? string.Empty;
            if (!(fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)))
            {
                itemActionErrorMessage = "Please select a valid Excel file (.xls, .xlsx, .xlsm, .xlsb).";
                showErrorModal = true;
                StateHasChanged();
                return;
            }

            using var browserStream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024);
            using var ms = new MemoryStream();
            await browserStream.CopyToAsync(ms);
            ms.Position = 0;

            try
            {
                lastImportResult = await importMapItemService.ImportFromExcelAsync(ms, userContext.UserId ?? 0);
            }
            catch (Exception ex)
            {
                itemActionErrorMessage = $"Failed to open/import Excel file: {ex.Message}. Supported formats: .xlsx, .xlsm. For legacy .xls files please save as .xlsx and try again.";
                showErrorModal = true;
                StateHasChanged();
                return;
            }
            selectedImportItemGroupKeys = BuildDefaultSelectedImportGroupKeys(lastImportResult);

            showImportDetailsModal = true;

            StateHasChanged();
        }

        private async Task HandleCommitImportAsync()
        {
            if (lastImportResult is null || !userContext.UserId.HasValue)
            {
                return;
            }

            var selectedRows = lastImportResult.Rows
                .Where(row => row.IsSuccess && row.Issues.Count == 0)
                .Where(row => selectedImportItemGroupKeys.Contains(BuildImportItemGroupKey(row)))
                .ToList();

            if (selectedRows.Count == 0)
            {
                itemActionErrorMessage = "No valid mapping group selected for commit.";
                showErrorModal = true;
                StateHasChanged();
                return;
            }

            try
            {
                var committedGroups = await importMapItemService.CommitPreparedRowsAsync(selectedRows, userContext.UserId.Value);
                if (committedGroups <= 0)
                {
                    itemActionErrorMessage = "Commit did not save any item groups. Please re-check your selected rows.";
                    showErrorModal = true;
                    StateHasChanged();
                    return;
                }

                var committedSubdCode = selectedRows
                    .Select(row => row.SubDistributorCode)
                    .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code));
                if (!string.IsNullOrWhiteSpace(committedSubdCode))
                {
                    var matchedSubd = subdList.FirstOrDefault(subd =>
                        string.Equals(subd.SubdCode, committedSubdCode.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (matchedSubd != null && matchedSubd.SubDistributorId != selectedSubdId)
                    {
                        selectedSubdId = matchedSubd.SubDistributorId;
                        UpdateSelectedSubdLocation();
                        await PersistLastSelectedSubdAsync();
                    }
                }

                // Reset filters so newly committed rows are immediately visible in the table.
                selectedPrincipal = null;
                selectedCompanyItemIdForFilter = null;
                await LoadMapTablesAsync();

                showImportDetailsModal = false;
                lastImportResult = null;
                selectedImportItemGroupKeys.Clear();
            }
            catch (Exception ex)
            {
                itemActionErrorMessage = $"Commit failed: {ex.Message}";
                showErrorModal = true;
            }

            StateHasChanged();
        }

        private void CloseImportDetailsModal()
        {
            showImportDetailsModal = false;
            lastImportResult = null;
            selectedImportItemGroupKeys.Clear();
            StateHasChanged();
        }

        private static HashSet<string> BuildDefaultSelectedImportGroupKeys(ImportMapItemResult? result)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (result is null || result.Rows.Count == 0)
            {
                return selected;
            }

            var validGroups = result.Rows
                .GroupBy(BuildImportItemGroupKey)
                .Where(group => group.All(row => row.IsSuccess && row.Issues.Count == 0))
                .Select(group => group.Key);

            foreach (var key in validGroups)
            {
                selected.Add(key);
            }

            return selected;
        }

        private static string BuildImportItemGroupKey(ImportMapItemRowResult row)
        {
            static string NormalizePart(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
            return string.Join("|",
                NormalizePart(row.SubDistributorCode),
                NormalizePart(row.CompanyItemCode),
                NormalizePart(row.CompanyItemName));
        }

        private Task OpenAddUomModal()
        {
            if (selectedCompanyItemId.HasValue)
            {
                var ci = companyItemsForDropdown.FirstOrDefault(i => i.CompanyItemId == selectedCompanyItemId.Value);
                modalCompanyItemCode = ci?.ItemCode;
                modalCompanyItemName = ci?.ItemName;
            }
            else
            {
                modalCompanyItemCode = null;
                modalCompanyItemName = null;
            }

            showAddUomModal = true;
            _ = PersistDraftAsync();
            return Task.CompletedTask;
        }

        private Task CloseAddUomModalAsync()
        {
            showAddUomModal = false;
            _ = PersistDraftAsync();
            return Task.CompletedTask;
        }

        private async Task HandleAddUomModalAddAsync(Dictionary<string, UomEntry> updatedUomEntries)
        {
            uomEntries = updatedUomEntries
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

            availableUoms = uomEntries
                .Keys
                .OrderBy(uom => uom)
                .ToList();

            showAddUomModal = false;
            await PersistDraftAsync();
            StateHasChanged();
        }

        private string GetDraftStorageKey()
        {
            var userId = userContext.UserId ?? 0;
            return $"mapitem-draft:{userId}";
        }

        private string GetAddUomDraftStorageKey()
        {
            var userId = userContext.UserId ?? 0;
            var companyItemId = selectedCompanyItemId ?? 0;
            return $"mapitem-add-uom-draft:{userId}:{selectedSubdId}:{companyItemId}";
        }
        private string GetLastSelectedSubdStorageKey()
        {
            var userId = userContext.UserId ?? 0;
            return $"mapitem-last-selected-subd:{userId}";
        }

        // Draft/state DTOs moved to Features/MapItem/DTOs


        private readonly SemaphoreSlim mapTablesLoadLock = new(1, 1);
        private readonly SemaphoreSlim onParametersSetLock = new(1, 1);
        private List<SubDistributor> subdList = new();
        private ElementReference subdSelectRef;
        private int selectedSubdId;
        private string locationText = "---";
        private List<string> principals = new();
        private string? selectedDropdownPrincipal;
        private string? selectedCompanyItemDisplayName;
        private string selectedCompanyItemsFilterString = "All";
        private string selectedCompanyItemsCategoryString = "All";
        private CompanyItemFilterMode selectedCompanyItemsFilter = CompanyItemFilterMode.All;
        private List<MapCompanyItemViewRow> companyItemsAll = new();
        private List<MapCompanyItemViewRow> companyItems = new();
        private List<MapSubDistributorItemRow> subDistributorItems = new();
        private List<MapSubDistributorItemRow> filteredSubDistributorItems = new();
        private List<CompanyItemDropdownItem> companyItemsForDropdown = new();
        private int? selectedCompanyItemIdForFilter = null;
        private MapItemInputHeader? mapItemInputHeader;

        private enum ConfirmActionKind
        {
            None,
            Add,
            Update,
            Delete
        }

        private bool IsSubDistributorSelected => selectedSubdId != 0;
        private bool IsEditingItem => editingSubdItemId.HasValue;

        private IEnumerable<CompanyItemDropdownItem> FilteredCompanyItemsForDropdown =>
            string.IsNullOrWhiteSpace(selectedDropdownPrincipal)
                ? companyItemsForDropdown
                : companyItemsForDropdown.Where(i => i.Principal == selectedDropdownPrincipal);

        private IEnumerable<string> CompanyItemCategories =>
            companyItemsAll
                .Select(item => item.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category);

        private IEnumerable<string> PrincipalsDropdown =>
            companyItemsForDropdown
                .Select(i => i.Principal)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .OrderBy(p => p);

        private IEnumerable<string> DisplayPrincipalsDropdown =>
            string.IsNullOrWhiteSpace(selectedDropdownPrincipal) || PrincipalsDropdown.Contains(selectedDropdownPrincipal)
                ? PrincipalsDropdown
                : PrincipalsDropdown.Append(selectedDropdownPrincipal).OrderBy(p => p);

        private void OnPrincipalChanged()
        {
            if (selectedCompanyItemId.HasValue && !string.IsNullOrWhiteSpace(selectedDropdownPrincipal))
            {
                var currentSelectedItem = companyItemsForDropdown.FirstOrDefault(i => i.CompanyItemId == selectedCompanyItemId.Value);
                if (currentSelectedItem != null && currentSelectedItem.Principal != selectedDropdownPrincipal)
                {
                    ClearCompanyItemSelection();
                }
            }

            _ = PersistDraftAsync();
        }

        private Task ClearCompanyItemFilterAsync()
        {
            selectedCompanyItemIdForFilter = null;
            UpdateFilteredSubDistributorItems();
            return Task.CompletedTask;
        }

        private string? GetFieldError(string key)
            => validationErrors.TryGetValue(key, out var message) ? message : null;

        private void SetFieldError(string key, string message)
            => validationErrors[key] = message;

        private void ClearFieldError(string key)
            => validationErrors.Remove(key);

        private void ClearValidationErrors()
            => validationErrors.Clear();

        private void ClearItemActionError()
            => itemActionErrorMessage = null;

        private void ResetItemForm(bool persistDraft = true)
        {
            editingSubdItemId = null;
            itemCode = string.Empty;
            itemName = string.Empty;
            selectedCompanyItemId = null;
            selectedDropdownPrincipal = null;
            selectedCompanyItemDisplayName = null;
            availableUoms.Clear();
            uomEntries.Clear();
            ClearValidationErrors();
            ClearItemActionError();

            if (persistDraft)
            {
                _ = PersistDraftAsync();
            }
        }

        private void GoBackToHome()
        {
            Navigation.NavigateTo("/home", forceLoad: true);
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            if (mapItemJsModule is not null)
            {
                try
                {
                    await mapItemJsModule.InvokeVoidAsync("unregisterMapItemKeyHandler");
                    await mapItemJsModule.DisposeAsync();
                }
                catch
                {
                    // Best effort
                }
            }

            objRef?.Dispose();
        }

        [JSInvokable]
        public Task EscapeActionFromShortcut()
        {
            if (showAddUomModal)
            {
                showAddUomModal = false;
                StateHasChanged();
                return Task.CompletedTask;
            }

            if (showConfirmModal)
            {
                showConfirmModal = false;
                StateHasChanged();
                return Task.CompletedTask;
            }

            if (showClearConfirmModal)
            {
                showClearConfirmModal = false;
                StateHasChanged();
                return Task.CompletedTask;
            }

            if (showErrorModal)
            {
                showErrorModal = false;
                StateHasChanged();
                return Task.CompletedTask;
            }

            if (showDownloadTemplateModal)
            {
                showDownloadTemplateModal = false;
                StateHasChanged();
                return Task.CompletedTask;
            }

            if (showImportConfirmModal)
            {
                showImportConfirmModal = false;
                StateHasChanged();
                return Task.CompletedTask;
            }

            GoBackToHome();
            return Task.CompletedTask;
        }

        private bool IsAnyModalOpen =>
            showAddUomModal ||
            showConfirmModal ||
            showClearConfirmModal ||
            showErrorModal ||
            showDownloadTemplateModal ||
            showImportConfirmModal ||
            showImportDetailsModal;

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
                subdList = new();
                selectedSubdId = 0;
                locationText = "---";
                principals = new();
                selectedPrincipal = null;
                selectedCompanyItemsFilterString = "All";
                selectedCompanyItemsCategoryString = "All";
                selectedCompanyItemsFilter = CompanyItemFilterMode.All;
                companyItemsAll = new();
                companyItems = new();
                subDistributorItems = new();
                ResetItemForm();
                return;
            }

            subdList = await homeService.GetSubDistributorsAsync(userContext.UserId.Value);
            shouldHydrateClientStateAfterRender = true;

            if (selectedSubdId == 0)
            {
                if (hasHydratedClientState)
                {
                    await RestoreLastSelectedSubdAsync();
                }
            }

            if (!subdList.Any())
            {
                selectedSubdId = 0;
                locationText = "---";
                principals = new();
                selectedPrincipal = null;
                selectedCompanyItemsFilterString = "All";
                selectedCompanyItemsCategoryString = "All";
                selectedCompanyItemsFilter = CompanyItemFilterMode.All;
                companyItemsAll = new();
                companyItems = new();
                subDistributorItems = new();
                ResetItemForm();
                return;
            }

            if (selectedSubdId != 0 && !subdList.Any(s => s.SubDistributorId == selectedSubdId))
            {
                selectedSubdId = 0;
                selectedCompanyItemsFilterString = "All";
                selectedCompanyItemsCategoryString = "All";
                selectedCompanyItemsFilter = CompanyItemFilterMode.All;
                ResetItemForm();
            }

            UpdateSelectedSubdLocation();
            await LoadMapTablesAsync();

            if (hasHydratedClientState && selectedSubdId != 0)
            {
                await RestoreDraftStateOnlyAsync();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
                objRef = DotNetObjectReference.Create(this);
                try
                {
                    mapItemJsModule = await JS.InvokeAsync<IJSObjectReference>("import", "/js/mapitem.js");
                    await mapItemJsModule.InvokeVoidAsync("registerMapItemKeyHandler", objRef);
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
                await subdSelectRef.FocusAsync();

                if (shouldHydrateClientStateAfterRender && !hasHydratedClientState && userContext.UserId.HasValue)
                {
                    await HydrateClientStateAfterRenderAsync();
                    StateHasChanged();
                }
            }
        }

        private async Task HydrateClientStateAfterRenderAsync()
        {
            if (hasHydratedClientState)
            {
                return;
            }

            hasHydratedClientState = true;

            if (selectedSubdId == 0)
            {
                await RestoreLastSelectedSubdAsync();
            }

            if (selectedSubdId != 0)
            {
                UpdateSelectedSubdLocation();
                await LoadMapTablesAsync();
                await RestoreDraftStateOnlyAsync();
            }
            else
            {
                await LoadMapTablesAsync();
                await RestoreDraftStateOnlyAsync();
            }
        }

        private async Task HandleSubdKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" || (e.Key == "Tab" && !e.ShiftKey))
            {
                await Task.Delay(10);
                if (mapItemInputHeader is not null)
                {
                    await mapItemInputHeader.FocusItemCodeAsync();
                }
            }
        }

        private async Task HandleItemCodeKeyDown(KeyboardEventArgs e)
        {
            if (e.Key != "Enter" && e.Key != "Tab")
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(itemCode))
            {
                SetFieldError(MapItemValidation.Form.SkuCode.Key, MapItemValidation.Form.SkuCode.ErrorMessage);
                return;
            }

            ClearFieldError(MapItemValidation.Form.SkuCode.Key);
            await Task.Delay(10);
            if (mapItemInputHeader is not null)
            {
                await mapItemInputHeader.FocusItemNameAsync();
            }
        }

        private async Task HandleItemNameKeyDown(KeyboardEventArgs e)
        {
            if (e.Key != "Enter" && e.Key != "Tab")
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(itemName))
            {
                SetFieldError(MapItemValidation.Form.ItemName.Key, MapItemValidation.Form.ItemName.ErrorMessage);
                return;
            }

            ClearFieldError(MapItemValidation.Form.ItemName.Key);
            await Task.Delay(10);
            if (mapItemInputHeader is not null)
            {
                await mapItemInputHeader.FocusPrincipalAsync();
            }
        }

        private async Task HandlePrincipalKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                if (!principalEnterPrimed)
                {
                    principalEnterPrimed = true;
                    if (mapItemInputHeader is not null)
                    {
                        await mapItemInputHeader.OpenPrincipalDropdownAsync();
                    }
                    return;
                }

                principalEnterPrimed = false;
                await Task.Delay(10);
                if (mapItemInputHeader is not null)
                {
                    await mapItemInputHeader.FocusCompanyItemAsync();
                }
                return;
            }

            if (e.Key == "Tab" && !e.ShiftKey)
            {
                await Task.Delay(10);
                if (mapItemInputHeader is not null)
                {
                    await mapItemInputHeader.FocusCompanyItemAsync();
                }
            }
        }

        private async Task HandleCompanyItemKeyDown(KeyboardEventArgs e)
        {
            // Handled by Autocomplete component; if needed, forward first-enter to open popup
            if (e.Key == "Enter")
            {
                if (mapItemInputHeader is not null)
                {
                    await mapItemInputHeader.FocusCompanyItemAsync();
                    return;
                }
            }
        }

        private async Task OnSubdChangedAsync()
        {
            UpdateSelectedSubdLocation();
            await PersistLastSelectedSubdAsync();
            selectedPrincipal = null;
            ResetItemForm(persistDraft: false);
            // Load tables for the selected sub-distributor
            await LoadMapTablesAsync();
            // Then restore the draft state for this sub-distributor
            await RestoreDraftStateOnlyAsync();
        }

        private void UpdateSelectedSubdLocation()
        {
            var selected = subdList.FirstOrDefault(s => s.SubDistributorId == selectedSubdId);
            locationText = selected == null
                ? "---"
                : FormatHelper.FormatLocation(selected.CityMunicipality, selected.Province ?? string.Empty);
        }

        private async Task LoadMapTablesAsync()
        {
            // Acquire lock to ensure only one execution at a time, preventing DbContext threading issues
            await mapTablesLoadLock.WaitAsync();
            try
            {
                await _LoadMapTablesAsyncInternal();
            }
            finally
            {
                mapTablesLoadLock.Release();
            }
        }

        /// <summary>
        /// Internal method that performs the database load without lock acquisition.
        /// Only call this when you already hold the mapTablesLoadLock.
        /// </summary>
        private async Task _LoadMapTablesAsyncInternal()
        {
            if (!userContext.UserId.HasValue)
            {
                principals = new();
                companyItems = new();
                subDistributorItems = new();
                companyItemsForDropdown = new();
                return;
            }

            companyItemsForDropdown = await mapItemService.GetCompanyItemsForDropdownAsync(
                userContext.UserId.Value, selectedSubdId);

            principals = await mapItemService.GetMapItemPrincipalsAsync(userContext.UserId.Value, selectedSubdId);
            var companyRows = await mapItemService.GetMapCompanyItemsAsync(
                userContext.UserId.Value,
                selectedSubdId,
                selectedPrincipal,
                IsSubDistributorSelected ? selectedCompanyItemsFilter : CompanyItemFilterMode.All);

            subDistributorItems = selectedSubdId != 0
                ? await mapItemService.GetMapSubDistributorItemsAsync(
                    userContext.UserId.Value,
                    selectedSubdId,
                    selectedPrincipal)
                : new List<MapSubDistributorItemRow>();

            companyItemsAll = companyRows
                .GroupBy(x => new { x.CompanyItemId, x.CompanyItemCode, x.Description, x.Category })
                .Select(group => new MapCompanyItemViewRow
                {
                    CompanyItemId = group.Key.CompanyItemId,
                    CompanyItemCode = group.Key.CompanyItemCode,
                    Principal = group.Select(x => x.Principal).FirstOrDefault() ?? string.Empty,
                    ItemName = group.Key.Description,
                    Category = group.Key.Category,
                    EffectivityDate = group.Where(x => x.EffectivityDate.HasValue)
                        .Select(x => x.EffectivityDate)
                        .OrderByDescending(date => date)
                        .FirstOrDefault(),
                    PriceIncreasePercent = group.Where(x => x.PriceIncreasePercent.HasValue)
                        .Select(x => x.PriceIncreasePercent)
                        .FirstOrDefault(),
                    RecentAppliedDate = group.Where(x => x.RecentAppliedDate.HasValue)
                        .Select(x => x.RecentAppliedDate)
                        .OrderByDescending(d => d)
                        .FirstOrDefault(),
                    UomName = string.Join(", ", group
                        .Where(x => !string.IsNullOrWhiteSpace(x.UomName))
                        .Select(x => $"{x.UomName} - {x.Price:N2}"))
                    ,
                    IsMapped = subDistributorItems.Any(s => s.CompanyItemId == group.Key.CompanyItemId)
                })
                .OrderBy(x => x.CompanyItemCode)
                .ToList();

            ApplyCompanyItemFilters();
            UpdateFilteredSubDistributorItems();
        }

        private async Task FilterByPrincipalAsync(string? principal)
        {
            selectedPrincipal = principal;
            selectedCompanyItemIdForFilter = null;
            await LoadMapTablesAsync();
        }

        private void OnCompanyItemRowClicked(MapCompanyItemViewRow item)
        {
            if (selectedCompanyItemIdForFilter == item.CompanyItemId)
            {
                selectedCompanyItemIdForFilter = null;
                selectedPrincipal = null;
            }
            else
            {
                selectedCompanyItemIdForFilter = item.CompanyItemId;
                selectedPrincipal = string.IsNullOrWhiteSpace(item.Principal) ? null : item.Principal;
            }
            UpdateFilteredSubDistributorItems();
        }

        private void UpdateFilteredSubDistributorItems()
        {
            if (selectedCompanyItemIdForFilter.HasValue)
            {
                filteredSubDistributorItems = subDistributorItems
                    .Where(item => item.CompanyItemId == selectedCompanyItemIdForFilter.Value)
                    .ToList();
            }
            else
            {
                filteredSubDistributorItems = subDistributorItems;
            }
        }

        private async Task ValidateFormAsync()
        {
            validationErrors = await MapItemValidation.ValidateFormAsync(
                itemCode,
                itemName,
                selectedCompanyItemId,
                uomEntries.Any(),
                async () =>
                {
                    if (!IsSubDistributorSelected || string.IsNullOrWhiteSpace(itemCode))
                    {
                        return false;
                    }

                    return await mapItemService.SubdItemCodeExistsAsync(
                        selectedSubdId,
                        itemCode!,
                        editingSubdItemId);
                });
        }

        private Task HandleFormFieldBlur(FocusEventArgs _)
        {
            principalEnterPrimed = false;
            return ValidateFormAsync();
        }

        private Task HandlePrincipalBlur(FocusEventArgs _)
        {
            principalEnterPrimed = false;
            return Task.CompletedTask;
        }

        private async Task OnCompanyItemsFilterStringChanged()
        {
            if (Enum.TryParse<CompanyItemFilterMode>(selectedCompanyItemsFilterString, out var filterMode))
            {
                selectedCompanyItemsFilter = filterMode;
            }
            else
            {
                selectedCompanyItemsFilter = CompanyItemFilterMode.All;
                selectedCompanyItemsFilterString = "All";
            }

            selectedCompanyItemIdForFilter = null;
            await LoadMapTablesAsync();
            await PersistDraftAsync();
        }

        private Task OnCompanyItemsCategoryChanged()
        {
            if (!selectedCompanyItemsCategoryString.Equals("All", StringComparison.OrdinalIgnoreCase) &&
                !CompanyItemCategories.Contains(selectedCompanyItemsCategoryString, StringComparer.OrdinalIgnoreCase))
            {
                selectedCompanyItemsCategoryString = "All";
            }

            selectedCompanyItemIdForFilter = null;
            ApplyCompanyItemFilters();
            return PersistDraftAsync();
        }

        private async Task OnCompanyItemDataListChanged()
        {
            // legacy: not used when using Autocomplete
            await Task.CompletedTask;
        }

        private async Task OnAutocompleteSelected(CompanyItemDropdownItem? item)
        {
            if (item != null)
            {
                await SelectCompanyItem(item.CompanyItemId);
                ClearFieldError(MapItemValidation.Form.CompanyItem.Key);
            }
            else
            {
                ClearCompanyItemSelection();
            }

            await ValidateFormAsync();
            await PersistDraftAsync();
        }

        private async Task OnCompanyItemConfirmed()
        {
            await Task.Delay(10);
            if (mapItemInputHeader is not null)
            {
                await mapItemInputHeader.FocusAddUomButtonAsync();
            }
        }

        private async Task HandleMapCompanyItemRequestedAsync(int companyItemId)
        {
            await SelectCompanyItem(companyItemId);
            if (mapItemInputHeader is not null)
            {
                await mapItemInputHeader.FocusCompanyItemAsync();
            }
        }

        private Task HandleItemCodeChanged()
        {
            return PersistDraftAsync();
        }

        private Task HandleItemNameChanged()
        {
            return PersistDraftAsync();
        }

        private Task OnCompanyItemValueChanged(string? value)
        {
            selectedCompanyItemDisplayName = value;
            return PersistDraftAsync();
        }

        private async Task SelectCompanyItem(int companyItemId)
        {
            // Acquire lock to ensure sequential DbContext access
            await mapTablesLoadLock.WaitAsync();
            try
            {
                selectedCompanyItemId = companyItemId;
                var selectedItem = companyItemsForDropdown.FirstOrDefault(x => x.CompanyItemId == companyItemId);
                if (selectedItem != null)
                {
                    selectedDropdownPrincipal = selectedItem.Principal;
                    selectedCompanyItemDisplayName = selectedItem.ItemName;
                    // Load possible UOMs for this company item (used as dropdown/source)
                    availableUoms = await mapItemService.GetCompanyItemUomsAsync(companyItemId);

                    // If there's an add-uom draft for this subd+companyItem, don't overwrite uomEntries
                    jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
                    var addDraftJson = await jsModule.InvokeAsync<string?>("loadSalesInvoiceDraft", GetAddUomDraftStorageKey());

                    if (string.IsNullOrWhiteSpace(addDraftJson))
                    {
                        // No modal draft present. For a new item, show only PC as base unit by default.
                        if (!editingSubdItemId.HasValue)
                        {
                            uomEntries = new Dictionary<string, UomEntry>(StringComparer.OrdinalIgnoreCase)
                            {
                                [BaseUomName] = new UomEntry { Conversion = 1, Price = null }
                            };
                        }
                        else
                        {
                            // If editing an existing subd item, preserve existing available UOMs fetched from DB
                            uomEntries = availableUoms.ToDictionary(u => u, u => new UomEntry { Conversion = 1, Price = null });
                        }
                    }
                }

                ClearFieldError(MapItemValidation.Form.CompanyItem.Key);
                await PersistDraftAsync();
            }
            finally
            {
                mapTablesLoadLock.Release();
            }
        }

        private void ClearCompanyItemSelection()
        {
            selectedCompanyItemId = null;
            selectedDropdownPrincipal = null;
            selectedCompanyItemDisplayName = null;
            availableUoms = new();
            uomEntries = new();
            ClearFieldError(MapItemValidation.Form.CompanyItem.Key);
            _ = PersistDraftAsync();
        }

        private string GetSelectedCompanyItemName()
        {
            if (!selectedCompanyItemId.HasValue)
            {
                return selectedCompanyItemDisplayName ?? string.Empty;
            }

            var item = companyItemsForDropdown.FirstOrDefault(i => i.CompanyItemId == selectedCompanyItemId.Value);
            return item?.ItemName ?? selectedCompanyItemDisplayName ?? string.Empty;
        }

        private string GetUnitOfMeasureSummary()
        {
            if (!uomEntries.Any())
            {
                return "No unit of measure added";
            }

            return string.Join(", ", uomEntries
                .OrderBy(entry => entry.Value.Conversion)
                .Select(entry =>
                {
                    var conversionStr = entry.Value.Conversion == 1 ? "" : $" ({entry.Value.Conversion})";
                    var priceStr = entry.Value.Price.HasValue ? $" - {entry.Value.Price.Value:N2}" : "";
                    return $"{entry.Key}{conversionStr}{priceStr}";
                }));
        }

        private ItemsUom? BuildSelectedItemsUom()
        {
            // Prefer the base unit if it has a price
            if (uomEntries.TryGetValue(BaseUomName, out var baseEntry) && baseEntry.Price.HasValue)
            {
                return new ItemsUom
                {
                    UomName = BaseUomName,
                    ConversionToBase = 1,
                    Price = baseEntry.Price.Value,
                    IsBaseUnit = true,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    CreatedBy = userContext.UserId,
                    UpdatedBy = userContext.UserId
                };
            }

            // Prefer a manually-edited / explicitly set price (IsAutoCalculated == false)
            var manualEntry = uomEntries
                .Where(entry => entry.Value.Price.HasValue && entry.Value.IsAutoCalculated == false)
                .FirstOrDefault();

            if (manualEntry.Value != null && manualEntry.Value.Price.HasValue)
            {
                return new ItemsUom
                {
                    UomName = manualEntry.Key,
                    ConversionToBase = manualEntry.Value.Conversion,
                    Price = manualEntry.Value.Price.Value,
                    IsBaseUnit = IsBaseUom(manualEntry.Key),
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    CreatedBy = userContext.UserId,
                    UpdatedBy = userContext.UserId
                };
            }

            // Fallback: pick the first priced entry (non-base preferred)
            var selectedEntry = uomEntries
                .Where(entry => entry.Value.Price.HasValue)
                .OrderBy(entry => IsBaseUom(entry.Key))
                .FirstOrDefault();

            if (selectedEntry.Value == null || !selectedEntry.Value.Price.HasValue)
            {
                return null;
            }

            return new ItemsUom
            {
                UomName = selectedEntry.Key,
                ConversionToBase = selectedEntry.Value.Conversion,
                Price = selectedEntry.Value.Price.Value,
                IsBaseUnit = IsBaseUom(selectedEntry.Key),
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow,
                CreatedBy = userContext.UserId,
                UpdatedBy = userContext.UserId
            };
        }

        private async Task OpenDatalistAsync(ElementReference inputRef)
        {
            jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
            await jsModule.InvokeVoidAsync("openDatalist", inputRef);
        }



        private async Task SaveItemAsync()
        {
            await ValidateFormAsync();

            if (validationErrors.Any() || !userContext.UserId.HasValue || !selectedCompanyItemId.HasValue)
            {
                return;
            }

            // If there are no UOM entries or no priced UOMs, show an error on the confirmation modal when adding
            if (!uomEntries.Any() || !uomEntries.Any(x => x.Value.Price.HasValue))
            {
                if (!IsEditingItem)
                {
                    modalTitle = "Add Item";
                    modalMessage = string.Empty;
                    pendingConfirmAction = ConfirmActionKind.Add;
                    confirmedCompanyItemSummary = GetSelectedCompanyItemName();
                    itemActionErrorMessage = "At least one UOM with a price must be added before adding this item.";
                    showConfirmModal = true;
                    await PersistDraftAsync();
                    return;
                }

                return;
            }

            modalTitle = IsEditingItem ? "Update Item" : "Add Item";
            modalMessage = IsEditingItem
                ? $"Do you want to update '{itemCode}'?"
                : $"Do you want to add '{itemCode}'?";
            pendingConfirmAction = IsEditingItem ? ConfirmActionKind.Update : ConfirmActionKind.Add;
            confirmedCompanyItemSummary = GetSelectedCompanyItemName();
            itemActionErrorMessage = null;
            showConfirmModal = true;
            await PersistDraftAsync();
        }

        private async Task PersistItemAsync()
        {
            // Acquire lock to ensure sequential DbContext access for all database operations
            await mapTablesLoadLock.WaitAsync();
            try
            {
                if (!userContext.UserId.HasValue || !selectedCompanyItemId.HasValue)
                {
                    return;
                }

                var prices = string.Join(", ", uomEntries.Where(p => p.Value.Price.HasValue).Select(p => $"{p.Key} ({p.Value.Conversion}): {p.Value.Price:N2}"));
                Console.WriteLine($"{(IsEditingItem ? "Updated" : "Added")}: CompanyItemId={selectedCompanyItemId}, Principal={selectedDropdownPrincipal}, SKU={itemCode}, ItemName={itemName}, Prices={prices}");

                var item = new SubdItem
                {
                    SubdItemId = editingSubdItemId ?? 0,
                    SubdItemCode = itemCode ?? string.Empty,
                    ItemName = itemName ?? string.Empty,
                    SubDistributorId = selectedSubdId,
                    CompanyItemId = selectedCompanyItemId.Value,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = userContext.UserId.Value,
                    UpdatedBy = IsEditingItem ? userContext.UserId.Value : null,
                    UpdatedDate = IsEditingItem ? DateTime.UtcNow : null
                };

                if (IsEditingItem)
                {
                    var updateResult = await mapItemService.UpdateSubdItemAsync(item);
                    if (updateResult.IsUpdated)
                    {
                        // persist all UOMs separately
                        var saved = await mapItemService.SaveSubdItemUomPricesAsync(updateResult.IsUpdated ? (editingSubdItemId ?? 0) : 0,
    uomEntries, userContext.UserId ?? 0);
                        if (!saved)
                        {
                            itemActionErrorMessage = "Unable to save UOM prices.";
                            showErrorModal = true;
                        }

                        ResetItemForm();
                        await _LoadMapTablesAsyncInternal();
                        await ClearDraftAsync();
                    }
                    else if (!string.IsNullOrWhiteSpace(updateResult.ErrorMessage))
                    {
                        itemActionErrorMessage = updateResult.ErrorMessage;
                        showErrorModal = true;
                    }

                    return;
                }

                var success = await mapItemService.AddSubdItemAsync(item);

                if (success)
                {
                    // item.SubdItemId is set by EF on save; use it directly to persist UOM rows
                    if (item.SubdItemId > 0)
                    {
                        var saved = await mapItemService.SaveSubdItemUomPricesAsync(item.SubdItemId, uomEntries, userContext.UserId ?? 0);
                        if (!saved)
                        {
                            itemActionErrorMessage = "Unable to save UOM prices.";
                            showErrorModal = true;
                        }
                    }

                    ResetItemForm();
                    await _LoadMapTablesAsyncInternal();
                    if (mapItemInputHeader is not null)
                    {
                        await mapItemInputHeader.FocusItemCodeAsync();
                    }
                    await ClearDraftAsync();
                }
            }
            finally
            {
                mapTablesLoadLock.Release();
            }
        }

        private async Task BeginEditItem(MapSubDistributorItemRow item)
        {
            editingSubdItemId = item.SubdItemId;
            itemCode = item.SubItemCode;
            itemName = item.Description;
            selectedCompanyItemId = item.CompanyItemId;
            selectedCompanyItemDisplayName = item.CompanyItemName;
            selectedDropdownPrincipal = item.Principal;

            var existingUoms = await mapItemService.GetSubdItemUomsAsync(item.SubdItemId);
            if (existingUoms is not null && existingUoms.Any())
            {
                availableUoms = new List<string>(existingUoms.Select(u => NormalizeBaseUomName(u.UomName)).Append(BaseUomName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u));

                uomEntries = new Dictionary<string, UomEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var u in availableUoms)
                {
                    var matched = existingUoms.FirstOrDefault(e => string.Equals(NormalizeBaseUomName(e.UomName), u, StringComparison.OrdinalIgnoreCase));
                    if (IsBaseUom(u))
                    {
                        var price = matched != null && matched.ConversionToBase != 0 ? matched.Price / matched.ConversionToBase :
    matched?.Price;
                        uomEntries[u] = new UomEntry { Conversion = 1, Price = price };
                    }
                    else if (matched != null)
                    {
                        uomEntries[u] = new UomEntry { Conversion = matched.ConversionToBase, Price = matched.Price };
                    }
                    else
                    {
                        uomEntries[u] = new UomEntry { Conversion = 1, Price = null };
                    }
                }
            }
            else
            {
                availableUoms = new List<string>();
                uomEntries = new Dictionary<string, UomEntry>(StringComparer.OrdinalIgnoreCase);
            }

            ClearValidationErrors();
            await PersistDraftAsync();
        }

        private void DeleteItemAsync(MapSubDistributorItemRow item)
        {
            pendingItem = item;
            modalTitle = "Delete Item";
            modalMessage = $"Are you sure you want to delete '{item.SubItemCode}'? This action cannot be undone.";
            pendingConfirmAction = ConfirmActionKind.Delete;
            itemActionErrorMessage = null;
            showConfirmModal = true;
        }

        private Task CancelEditAsync()
        {
            ResetItemForm();
            _ = ClearDraftAsync();
            return Task.CompletedTask;
        }

        private void ShowClearConfirmModal()
        {
            showClearConfirmModal = true;
        }

        private async Task ConfirmClearDraft()
        {
            showClearConfirmModal = false;

            ResetItemForm(persistDraft: false);
            selectedPrincipal = null;
            selectedCompanyItemsFilterString = "All";
            selectedCompanyItemsCategoryString = "All";
            selectedCompanyItemsFilter = CompanyItemFilterMode.All;

            StateHasChanged();

            if (userContext.UserId is null)
            {
                return;
            }

            jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/salesinvoice.js");
            await jsModule.InvokeVoidAsync("clearSalesInvoiceDraft", GetDraftStorageKey());

            await LoadMapTablesAsync();
        }

        private void CancelClearConfirm()
        {
            showClearConfirmModal = false;
        }

        private async Task ClearAllDraftsAsync()
        {
            ShowClearConfirmModal();
            await Task.CompletedTask;
        }

        private async Task ConfirmAction()
        {
            if (pendingConfirmAction == ConfirmActionKind.Add || pendingConfirmAction == ConfirmActionKind.Update)
            {
                // PersistItemAsync handles its own lock
                await PersistItemAsync();
            }
            else if (pendingConfirmAction == ConfirmActionKind.Delete && pendingItem is not null)
            {
                // Acquire lock for Delete operation and subsequent LoadMapTables
                await mapTablesLoadLock.WaitAsync();
                try
                {
                    var deleteResult = await mapItemService.DeleteSubdItemAsync(pendingItem.SubdItemId);
                    if (deleteResult.IsDeleted)
                    {
                        if (editingSubdItemId == pendingItem.SubdItemId)
                        {
                            ResetItemForm();
                        }

                        await _LoadMapTablesAsyncInternal();
                    }
                    else if (!string.IsNullOrWhiteSpace(deleteResult.ErrorMessage))
                    {
                        itemActionErrorMessage = deleteResult.ErrorMessage;
                        showErrorModal = true;
                    }
                }
                finally
                {
                    mapTablesLoadLock.Release();
                }
            }
            CancelConfirmation();
        }

        private void CancelConfirmation()
        {
            showConfirmModal = false;
            pendingItem = null;
            pendingConfirmAction = ConfirmActionKind.None;
            modalTitle = string.Empty;
            modalMessage = string.Empty;
            confirmedCompanyItemSummary = null;
        }

        private async Task PersistDraftAsync()
        {
            if (userContext.UserId is null)
            {
                return;
            }

            var draftStore = await draftService.LoadDraftStoreAsync(GetDraftStorageKey());
            draftStore.Drafts[selectedSubdId.ToString()] = new MapItemDraftState
            {
                SelectedSubdId = selectedSubdId,
                ShowAddUomModal = showAddUomModal,
                ItemCode = itemCode,
                ItemName = itemName,
                SelectedPrincipal = selectedPrincipal,
                SelectedDropdownPrincipal = selectedDropdownPrincipal,
                SelectedCompanyItemId = selectedCompanyItemId,
                SelectedCompanyItemDisplayName = selectedCompanyItemDisplayName,
                EditingSubdItemId = editingSubdItemId,
                SelectedCompanyItemsFilterString = selectedCompanyItemsFilterString,
                SelectedCompanyItemsCategoryString = selectedCompanyItemsCategoryString,
                UomEntries = new Dictionary<string, UomEntry>(uomEntries, StringComparer.OrdinalIgnoreCase)
            };

            await draftService.SaveDraftStoreAsync(GetDraftStorageKey(), draftStore);
        }

        private async Task RestoreDraftAsync()
        {
            if (userContext.UserId is null)
            {
                return;
            }

            var draftStore = await draftService.LoadDraftStoreAsync(GetDraftStorageKey());
            if (!draftStore.Drafts.TryGetValue(selectedSubdId.ToString(), out _))
            {
                return;
            }

            // Restore state and reload tables
            await RestoreDraftStateOnlyAsync();
            await LoadMapTablesAsync();
        }

        private async Task RestoreDraftStateOnlyAsync()
        {
            if (userContext.UserId is null)
            {
                return;
            }

            var draftStore = await draftService.LoadDraftStoreAsync(GetDraftStorageKey());
            if (!draftStore.Drafts.TryGetValue(selectedSubdId.ToString(), out var draft))
            {
                return;
            }

            itemCode = draft.ItemCode;
            itemName = draft.ItemName;
            showAddUomModal = false;
            selectedPrincipal = draft.SelectedPrincipal;
            selectedDropdownPrincipal = draft.SelectedDropdownPrincipal;
            selectedCompanyItemId = draft.SelectedCompanyItemId;
            selectedCompanyItemDisplayName = draft.SelectedCompanyItemDisplayName;
            editingSubdItemId = draft.EditingSubdItemId;
            selectedCompanyItemsFilterString = draft.SelectedCompanyItemsFilterString ?? "All";
            selectedCompanyItemsCategoryString = draft.SelectedCompanyItemsCategoryString ?? "All";
            uomEntries = new Dictionary<string, UomEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in draft.UomEntries)
            {
                uomEntries[NormalizeBaseUomName(entry.Key)] = entry.Value;
            }
            availableUoms = uomEntries.Keys.OrderBy(x => x).ToList();

            if (!Enum.TryParse(selectedCompanyItemsFilterString, out CompanyItemFilterMode restoredFilter))
            {
                restoredFilter = CompanyItemFilterMode.All;
            }

            selectedCompanyItemsFilter = restoredFilter;
            UpdateSelectedSubdLocation();

            if (!selectedCompanyItemsCategoryString.Equals("All", StringComparison.OrdinalIgnoreCase) &&
                !CompanyItemCategories.Contains(selectedCompanyItemsCategoryString, StringComparer.OrdinalIgnoreCase))
            {
                selectedCompanyItemsCategoryString = "All";
            }

            ApplyCompanyItemFilters();

            if (selectedCompanyItemId.HasValue)
            {
                await SelectCompanyItem(selectedCompanyItemId.Value);
            }

            StateHasChanged();
        }

        private async Task ClearDraftAsync()
        {
            if (userContext.UserId is null)
            {
                return;
            }

            var draftStore = await draftService.LoadDraftStoreAsync(GetDraftStorageKey());
            if (draftStore.Drafts.Remove(selectedSubdId.ToString()) && draftStore.Drafts.Count > 0)
            {
                await draftService.SaveDraftStoreAsync(GetDraftStorageKey(), draftStore);
                return;
            }

            await draftService.ClearDraftStoreAsync(GetDraftStorageKey());
        }

        private async Task PersistLastSelectedSubdAsync()
        {
            if (userContext.UserId is null)
            {
                return;
            }

            var selectionState = new MapItemSelectionState
            {
                SelectedSubdId = selectedSubdId
            };

            await draftService.SaveSelectionStateAsync(GetLastSelectedSubdStorageKey(), selectionState);
        }

        private async Task RestoreLastSelectedSubdAsync()
        {
            if (userContext.UserId is null || subdList.Count == 0)
            {
                return;
            }

            var selectionState = await draftService.LoadSelectionStateAsync(GetLastSelectedSubdStorageKey());
            if (selectionState is null)
            {
                return;
            }

            if (selectionState.SelectedSubdId != 0 && !subdList.Any(subd => subd.SubDistributorId == selectionState.SelectedSubdId))
            {
                return;
            }

            selectedSubdId = selectionState.SelectedSubdId;
        }

        private void ApplyCompanyItemFilters()
        {
            companyItems = companyItemsAll
                .Where(item => selectedCompanyItemsCategoryString.Equals("All", StringComparison.OrdinalIgnoreCase)
                    || item.Category.Equals(selectedCompanyItemsCategoryString, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.CompanyItemCode)
                .ToList();
            UpdateFilteredSubDistributorItems();
        }

        private void GoToCompanyItems()
        {
            Navigation.NavigateTo("/companyitems");
        }

        private void CloseErrorModal()
        {
            showErrorModal = false;
            itemActionErrorMessage = null;
        }

        private static bool IsBaseUom(string? uomName)
        {
            var normalized = (uomName ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is "piece" or "pcs" or "pc";
        }

        private static string NormalizeBaseUomName(string? uomName)
        {
            return IsBaseUom(uomName) ? BaseUomName : (uomName ?? string.Empty).Trim();
        }

    }
}
