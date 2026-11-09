using System.Globalization;
using Microsoft.AspNetCore.Components;
using STTproject.Data;
using STTproject.Services;

namespace STTproject.Features.User.Home.Components.Sections
{
    public partial class RecentActivityTable
    {
        private const string BatchView = "batch";
        private const string FlatView = "flat";

        private const string SortNewest = "newest";
        private const string SortOldest = "oldest";
        private const string SortSubd = "subd";

        private sealed record FilterChoice(string Value, string Text);

        private static readonly List<FilterChoice> MonthOptions = Enumerable.Range(1, 12)
            .Select(m => new FilterChoice(m.ToString(), CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)))
            .ToList();

        private static readonly List<FilterChoice> SortOptions = new()
        {
            new(SortNewest, "Newest first"),
            new(SortOldest, "Oldest first"),
            new(SortSubd, "Subdistributor (A–Z)")
        };

        [Parameter]
        public int UserId { get; set; }

        [Parameter]
        public List<SubDistributor> SubdList { get; set; } = new();

        // Data
        private List<HomeSalesInvoiceBatchRow> batchRows = new();
        private List<HomeSalesInvoiceFlatRow> flatRows = new();
        private List<HomeSalesInvoiceBatchRow> filteredBatchRows = new();
        private List<HomeSalesInvoiceFlatRow> filteredFlatRows = new();
        private List<FilterChoice> yearOptions = new();
        private int loadedUserId;

        private string currentViewMode = BatchView;
        private bool IsBatchView => currentViewMode == BatchView;

        // Filters
        private string searchTerm = string.Empty;
        private string selectedOrderType = string.Empty;
        private int? selectedSubDistributorId;
        private int? selectedMonth;
        private int? selectedYear;
        private string sortBy = SortNewest;
        private int filterResetKey;

        private bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(searchTerm)
            || (!IsBatchView && !string.IsNullOrEmpty(selectedOrderType))
            || selectedSubDistributorId.HasValue
            || selectedMonth.HasValue
            || selectedYear.HasValue;

        // Batch View Modal
        private bool showBatchDetailsModal;
        private string selectedBatchId = string.Empty;
        private string selectedBatchSubdName = string.Empty;
        private List<HomeSalesInvoiceBatchInvoiceRow> selectedBatchInvoices = new();

        // Flat View Modal
        private bool showInvoiceDetailsModal;
        private string selectedInvoiceCode = string.Empty;
        private HomeSalesInvoiceDetailRow? selectedInvoiceDetails;

        // Loading
        private bool isLoadingData = true;       // first load: "Loading…" row in the table
        private bool isBusy;                     // button actions: full-screen LoadingOverlay
        private string busyTitle = "Loading…";
        private string? busySubtitle;

        protected override async Task OnParametersSetAsync()
        {
            // Only load when the user changes, not on every parent re-render.
            if (UserId <= 0 || UserId == loadedUserId)
            {
                return;
            }

            loadedUserId = UserId;
            isLoadingData = true;
            try
            {
                await ReloadRecentActivityAsync();
            }
            finally
            {
                isLoadingData = false;
            }
        }

        /// <summary>
        /// Shows the LoadingOverlay while <paramref name="work"/> runs.
        /// Ignores extra clicks while something is already loading, and always
        /// hides the overlay again even if the work throws.
        /// </summary>
        private async Task RunWithLoadingAsync(string title, string? subtitle, Func<Task> work)
        {
            if (isBusy)
            {
                return;
            }

            isBusy = true;
            busyTitle = title;
            busySubtitle = subtitle;
            StateHasChanged();
            await Task.Yield(); // let the overlay render before the slow call starts

            try
            {
                await work();
            }
            finally
            {
                isBusy = false;
            }
        }

        /// <summary>
        /// Full page navigation: keep the overlay up until the browser leaves the page.
        /// </summary>
        private async Task NavigateWithLoadingAsync(string url, string title)
        {
            if (isBusy)
            {
                return;
            }

            isBusy = true;
            busyTitle = title;
            busySubtitle = "Please wait…";
            StateHasChanged();
            await Task.Yield();

            Navigation.NavigateTo(url, forceLoad: true);
        }

        private async Task ReloadRecentActivityAsync()
        {
            var batchTask = homeService.GetSalesInvoiceBatchRowsAsync(UserId);
            var flatTask = homeService.GetSalesInvoiceFlatRowsAsync(UserId);
            await Task.WhenAll(batchTask, flatTask);

            batchRows = batchTask.Result;
            flatRows = flatTask.Result;

            yearOptions = flatRows
                .Select(r => r.SalesInvoiceDate.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .Select(y => new FilterChoice(y.ToString(), y.ToString()))
                .ToList();

            ApplyFilters();
        }

        // ---------- Filtering ----------

        private void ApplyFilters()
        {
            var term = searchTerm.Trim();

            // Batch view
            IEnumerable<HomeSalesInvoiceBatchRow> batches = batchRows;

            if (selectedSubDistributorId is int batchSubdId)
            {
                batches = batches.Where(b => b.SubDistributorId == batchSubdId);
            }

            if (selectedMonth.HasValue || selectedYear.HasValue)
            {
                // A batch matches if at least one of its invoices falls in the period.
                batches = batches.Where(b => b.InvoiceDates.Any(MatchesPeriod));
            }

            if (term.Length > 0)
            {
                batches = batches.Where(b =>
                    Contains(b.BatchId, term)
                    || Contains(b.SubdName, term)
                    || Contains(b.SubdCode, term)
                    || b.InvoiceCodes.Any(code => Contains(code, term))
                    || b.CustomerNames.Any(name => Contains(name, term)));
            }

            filteredBatchRows = (sortBy switch
            {
                SortSubd => batches.OrderBy(b => b.SubdName, StringComparer.OrdinalIgnoreCase).ThenByDescending(b => b.CreatedDate),
                SortOldest => batches.OrderBy(b => b.CreatedDate).ThenBy(b => b.FirstSalesInvoiceId),
                _ => batches.OrderByDescending(b => b.CreatedDate).ThenByDescending(b => b.LastSalesInvoiceId)
            }).ToList();

            // Invoice list view
            IEnumerable<HomeSalesInvoiceFlatRow> invoices = flatRows;

            if (selectedSubDistributorId is int flatSubdId)
            {
                invoices = invoices.Where(r => r.SubDistributorId == flatSubdId);
            }

            if (selectedMonth.HasValue || selectedYear.HasValue)
            {
                invoices = invoices.Where(r => MatchesPeriod(r.SalesInvoiceDate));
            }

            if (!string.IsNullOrEmpty(selectedOrderType))
            {
                invoices = invoices.Where(r => string.Equals(r.OrderType, selectedOrderType, StringComparison.OrdinalIgnoreCase));
            }

            if (term.Length > 0)
            {
                invoices = invoices.Where(r =>
                    Contains(r.InvoiceCode, term)
                    || Contains(r.CustomerName, term)
                    || Contains(r.SubdName, term));
            }

            filteredFlatRows = (sortBy switch
            {
                SortSubd => invoices.OrderBy(r => r.SubdName, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.CreatedDate),
                SortOldest => invoices.OrderBy(r => r.CreatedDate).ThenBy(r => r.SalesInvoiceId),
                _ => invoices.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.SalesInvoiceId)
            }).ToList();
        }

        private bool MatchesPeriod(DateOnly date) =>
            (!selectedYear.HasValue || date.Year == selectedYear.Value)
            && (!selectedMonth.HasValue || date.Month == selectedMonth.Value);

        private static bool Contains(string? value, string term) =>
            value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

        private void SetViewMode(string mode)
        {
            currentViewMode = mode;
        }

        private void OnSearchChanged(string? value)
        {
            searchTerm = value ?? string.Empty;
            ApplyFilters();
        }

        private void OnOrderTypeChanged(string value)
        {
            selectedOrderType = value ?? string.Empty;
            ApplyFilters();
        }

        private void OnSubdSelected(SubDistributor? subd)
        {
            selectedSubDistributorId = subd?.SubDistributorId;
            ApplyFilters();
        }

        private void OnMonthSelected(string? value)
        {
            selectedMonth = int.TryParse(value, out var month) ? month : null;
            ApplyFilters();
        }

        private void OnYearSelected(string? value)
        {
            selectedYear = int.TryParse(value, out var year) ? year : null;
            ApplyFilters();
        }

        private void OnSortSelected(string? value)
        {
            sortBy = string.IsNullOrEmpty(value) ? SortNewest : value;
            ApplyFilters();
        }

        private void ClearFilters()
        {
            searchTerm = string.Empty;
            selectedOrderType = string.Empty;
            selectedSubDistributorId = null;
            selectedMonth = null;
            selectedYear = null;
            filterResetKey++; // re-creates the Select/AutoComplete components so they visually reset
            ApplyFilters();
        }

        // ---------- Batch View Modal ----------

        Task OpenBatchDetailsAsync(HomeSalesInvoiceBatchRow row) =>
            RunWithLoadingAsync("Loading batch details…", row.BatchId, async () =>
            {
                var invoices = await homeService.GetBatchInvoiceSummariesAsync(
                    UserId,
                    row.SubDistributorId,
                    row.BatchCreatedDate,
                    row.FirstSalesInvoiceId,
                    row.LastSalesInvoiceId);

                selectedBatchId = row.BatchId;
                selectedBatchSubdName = row.SubdName;
                selectedBatchInvoices = invoices;
                showBatchDetailsModal = true;
            });

        void CloseBatchDetails()
        {
            showBatchDetailsModal = false;
            selectedBatchId = string.Empty;
            selectedBatchSubdName = string.Empty;
            selectedBatchInvoices = new();
        }

        // ---------- Invoice Detail Modal ----------

        async Task OpenInvoiceDetailsAsync(HomeSalesInvoiceFlatRow row)
        {
            await OpenInvoiceDetailsByIdAsync(row.SalesInvoiceId, row.InvoiceCode);
        }

        Task OpenInvoiceDetailsByIdAsync(int salesInvoiceId, string? invoiceCode = null) =>
            RunWithLoadingAsync("Loading invoice details…", invoiceCode, async () =>
            {
                var details = await homeService.GetInvoiceDetailByIdAsync(UserId, salesInvoiceId);

                selectedInvoiceDetails = details;
                selectedInvoiceCode = !string.IsNullOrWhiteSpace(invoiceCode)
                    ? invoiceCode
                    : details?.InvoiceNumber ?? string.Empty;
                showInvoiceDetailsModal = true;
            });

        void CloseInvoiceDetails()
        {
            showInvoiceDetailsModal = false;
            selectedInvoiceCode = string.Empty;
            selectedInvoiceDetails = null;
        }

        Task EditInvoiceDetails()
        {
            if (selectedInvoiceDetails == null)
            {
                return Task.CompletedTask;
            }

            return NavigateWithLoadingAsync(
                $"/salesinvoice/edit/{selectedInvoiceDetails.SalesInvoiceId}",
                "Opening invoice for editing…");
        }
    }
}