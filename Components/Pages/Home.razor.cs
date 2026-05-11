using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using STTproject.Data;
using STTproject.Services;

namespace STTproject.Components.Pages
{
    public partial class Home
    {
        private int userid;
        private string UserFullName = string.Empty;

        private List<SubDistributor> subdList = new();
        private List<HomeSalesInvoiceBatchRow> batchRows = new();
        private List<HomeSalesInvoiceFlatRow> flatRows = new();
        private string currentViewMode = "batch"; // "batch" or "flat"
        private string selectedSubdNameFilter = string.Empty;
        private DateOnly? selectedDateFilter;
        private string sortByFilter = "date";

        // Batch View Modal
        private bool showBatchDetailsModal;
        private string selectedBatchId = string.Empty;
        private List<HomeSalesInvoiceBatchInvoiceRow> selectedBatchInvoices = new();
        private int selectedBatchSubDistributorId;
        private DateOnly selectedBatchCreatedDate;
        private int selectedBatchFirstInvoiceId;
        private int selectedBatchLastInvoiceId;

        // Flat View Modal
        private bool showInvoiceDetailsModal;
        private string selectedInvoiceCode = string.Empty;
        private HomeSalesInvoiceDetailRow? selectedInvoiceDetails;
        private bool showDeleteInvoiceConfirmModal;
        private string? deleteInvoiceErrorMessage;
        private bool showErrorModal = false;

        private IEnumerable<HomeSalesInvoiceBatchRow> FilteredBatchRows
        {
            get
            {
                var query = batchRows.AsEnumerable();

                // Filter by SubDistributor
                if (!string.IsNullOrWhiteSpace(selectedSubdNameFilter))
                {
                    query = query.Where(r => r.SubdName.Contains(selectedSubdNameFilter, StringComparison.OrdinalIgnoreCase));
                }

                // Filter by Date
                if (selectedDateFilter.HasValue)
                {
                    query = query.Where(r => r.BatchCreatedDate == selectedDateFilter.Value);
                }

                // Sort
                query = sortByFilter switch
                {
                    "subd" => query.OrderBy(r => r.SubdCode).ThenByDescending(r => r.CreatedDate),
                    _ => query.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.LastSalesInvoiceId)
                };

                return query;
            }
        }

        private IEnumerable<HomeSalesInvoiceFlatRow> FilteredFlatRows
        {
            get
            {
                var query = flatRows.AsEnumerable();

                // Filter by SubDistributor
                if (!string.IsNullOrWhiteSpace(selectedSubdNameFilter))
                {
                    query = query.Where(r => r.SubdName.Contains(selectedSubdNameFilter, StringComparison.OrdinalIgnoreCase));
                }

                // Filter by Date
                if (selectedDateFilter.HasValue)
                {
                    query = query.Where(r => r.SalesInvoiceDate == selectedDateFilter.Value);
                }

                // Sort
                query = sortByFilter switch
                {
                    "subd" => query.OrderBy(r => r.SubdName).ThenByDescending(r => r.SalesInvoiceDate),
                    _ => query.OrderByDescending(r => r.SalesInvoiceDate)
                };

                return query;
            }
        }

        protected override async Task OnParametersSetAsync()
        {
            if (!userContext.UserId.HasValue)
            {
                Navigation.NavigateTo("/");
                return;
            }

            userid = userContext.UserId.Value;
            UserFullName = string.Empty;

            var user = await homeService.GetUserAsync(userid);
            if (user != null)
            {
                UserFullName = user.FullName;
            }

            subdList = await homeService.GetSubDistributorsAsync(userid);
            batchRows = await homeService.GetSalesInvoiceBatchRowsAsync(userid);
            flatRows = await homeService.GetSalesInvoiceFlatRowsAsync(userid);
        }

        void InputSalesInvoice(int subDistributorId)
        {
            Navigation.NavigateTo($"/salesinvoice/{subDistributorId}");
        }

        void GoToMapItems()
        {
            Navigation.NavigateTo("/mapitem");
        }

        void SetBatchMode()
        {
            currentViewMode = "batch";
        }

        void SetFlatMode()
        {
            currentViewMode = "flat";
        }

        void ToggleViewMode()
        {
            currentViewMode = currentViewMode == "batch" ? "flat" : "batch";
        }

        // Batch View Methods
        async Task OpenBatchDetailsAsync(HomeSalesInvoiceBatchRow row)
        {
            selectedBatchId = row.BatchId;
            selectedBatchSubDistributorId = row.SubDistributorId;
            selectedBatchCreatedDate = row.BatchCreatedDate;
            selectedBatchFirstInvoiceId = row.FirstSalesInvoiceId;
            selectedBatchLastInvoiceId = row.LastSalesInvoiceId;
            selectedBatchInvoices = await homeService.GetBatchInvoiceSummariesAsync(
                userid,
                row.SubDistributorId,
                row.BatchCreatedDate,
                row.FirstSalesInvoiceId,
                row.LastSalesInvoiceId);

            showBatchDetailsModal = true;
        }

        void CloseBatchDetails()
        {
            showBatchDetailsModal = false;
            selectedBatchId = string.Empty;
            selectedBatchInvoices = new();
            selectedBatchSubDistributorId = 0;
            selectedBatchCreatedDate = default;
            selectedBatchFirstInvoiceId = 0;
            selectedBatchLastInvoiceId = 0;
        }

        // Flat View Methods
        async Task OpenInvoiceDetailsAsync(HomeSalesInvoiceFlatRow row)
        {
            await OpenInvoiceDetailsByIdAsync(row.SalesInvoiceId, row.InvoiceCode);
        }

        async Task OpenInvoiceDetailsByIdAsync(int salesInvoiceId, string? invoiceCode = null)
        {
            selectedInvoiceCode = invoiceCode ?? string.Empty;
            deleteInvoiceErrorMessage = null;
            showDeleteInvoiceConfirmModal = false;

            selectedInvoiceDetails = await homeService.GetInvoiceDetailByIdAsync(
                userid,
                salesInvoiceId);

            if (selectedInvoiceDetails != null && string.IsNullOrWhiteSpace(selectedInvoiceCode))
            {
                selectedInvoiceCode = selectedInvoiceDetails.InvoiceNumber;
            }

            showInvoiceDetailsModal = true;
        }

        void CloseInvoiceDetails()
        {
            showInvoiceDetailsModal = false;
            showDeleteInvoiceConfirmModal = false;
            selectedInvoiceCode = string.Empty;
            selectedInvoiceDetails = null;
            deleteInvoiceErrorMessage = null;
        }

        void EditInvoiceDetails()
        {
            if (selectedInvoiceDetails != null)
            {
                Navigation.NavigateTo($"/salesinvoice/edit/{selectedInvoiceDetails.SalesInvoiceId}");
            }
        }

        void ShowDeleteInvoiceConfirm()
        {
            if (selectedInvoiceDetails == null)
            {
                return;
            }

            deleteInvoiceErrorMessage = null;
            showDeleteInvoiceConfirmModal = true;
        }

        void CancelDeleteInvoice()
        {
            showDeleteInvoiceConfirmModal = false;
        }

        async Task ConfirmDeleteInvoiceAsync()
        {
            if (selectedInvoiceDetails == null)
            {
                return;
            }

            try
            {
                var deleted = await homeService.DeleteInvoiceByIdAsync(userid, selectedInvoiceDetails.SalesInvoiceId);
                if (!deleted)
                {
                    deleteInvoiceErrorMessage = "Unable to delete invoice.";
                    showErrorModal = true;
                    return;
                }

                await ReloadRecentActivityAsync();

                if (showBatchDetailsModal && selectedBatchSubDistributorId > 0)
                {
                    selectedBatchInvoices = await homeService.GetBatchInvoiceSummariesAsync(
                        userid,
                        selectedBatchSubDistributorId,
                        selectedBatchCreatedDate,
                        selectedBatchFirstInvoiceId,
                        selectedBatchLastInvoiceId);
                }

                showDeleteInvoiceConfirmModal = false;
                CloseInvoiceDetails();
            }
            catch
            {
                deleteInvoiceErrorMessage = "Unable to delete invoice due to a database error.";
                showErrorModal = true;
            }
        }

        private void CloseErrorModal()
        {
            showErrorModal = false;
            deleteInvoiceErrorMessage = null;
        }

        private void HandlePageKeyDown(KeyboardEventArgs e)
        {
            if (e.Key != "Escape")
            {
                return;
            }

            if (showInvoiceDetailsModal)
            {
                CloseInvoiceDetails();
                return;
            }

            if (showBatchDetailsModal)
            {
                CloseBatchDetails();
            }
        }

        async Task ReloadRecentActivityAsync()
        {
            batchRows = await homeService.GetSalesInvoiceBatchRowsAsync(userid);
            flatRows = await homeService.GetSalesInvoiceFlatRowsAsync(userid);
        }

    }
}
