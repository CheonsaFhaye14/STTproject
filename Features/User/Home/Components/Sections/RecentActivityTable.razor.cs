using Microsoft.AspNetCore.Components;
using STTproject.Data;
using STTproject.Services;

namespace STTproject.Features.User.Home.Components.Sections
{
    public partial class RecentActivityTable
    {
        [Parameter]
        public int UserId { get; set; }

        [Parameter]
        public List<SubDistributor> SubdList { get; set; } = new();

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
            if (UserId <= 0)
            {
                return;
            }

            batchRows = await homeService.GetSalesInvoiceBatchRowsAsync(UserId);
            flatRows = await homeService.GetSalesInvoiceFlatRowsAsync(UserId);
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
                UserId,
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

            selectedInvoiceDetails = await homeService.GetInvoiceDetailByIdAsync(
                UserId,
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
            selectedInvoiceCode = string.Empty;
            selectedInvoiceDetails = null;
        }

        async Task EditInvoiceDetails()
        {
            if (selectedInvoiceDetails != null)
            {
                Navigation.NavigateTo($"/salesinvoice/edit/{selectedInvoiceDetails.SalesInvoiceId}", forceLoad: true);
            }
        }
        async Task ReloadRecentActivityAsync()
        {
            batchRows = await homeService.GetSalesInvoiceBatchRowsAsync(UserId);
            flatRows = await homeService.GetSalesInvoiceFlatRowsAsync(UserId);
        }
    }
}
