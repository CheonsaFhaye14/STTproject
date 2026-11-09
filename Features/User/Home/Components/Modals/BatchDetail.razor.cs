using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using STTproject.Services;

namespace STTproject.Features.User.Home.Components.Modals
{
    public partial class BatchDetail
    {
        private const string SortCreated = "created";
        private const string SortInvoiceDate = "invoiceDate";
        private const string SortCode = "code";

        private sealed record FilterChoice(string Value, string Text);

        private static readonly List<FilterChoice> SortOptions = new()
        {
            new(SortCreated, "Newest created"),
            new(SortInvoiceDate, "Invoice date (newest)"),
            new(SortCode, "Invoice code (A–Z)")
        };

        [Parameter]
        public bool Show { get; set; }

        [Parameter]
        public string BatchId { get; set; } = string.Empty;

        [Parameter]
        public string SubdName { get; set; } = string.Empty;

        [Parameter]
        public List<HomeSalesInvoiceBatchInvoiceRow> BatchInvoices { get; set; } = new();

        [Parameter]
        public EventCallback OnClose { get; set; }

        [Parameter]
        public EventCallback<int> OnViewInvoiceDetails { get; set; }

        private List<HomeSalesInvoiceBatchInvoiceRow> filteredInvoices = new();
        private List<FilterChoice> customerOptions = new();
        private List<HomeSalesInvoiceBatchInvoiceRow>? loadedInvoices;

        private string searchTerm = string.Empty;
        private string selectedOrderType = string.Empty;
        private string selectedCustomer = string.Empty;
        private string sortBy = SortCreated;
        private int filterResetKey;

        private bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(searchTerm)
            || !string.IsNullOrEmpty(selectedOrderType)
            || !string.IsNullOrEmpty(selectedCustomer);

        protected override void OnParametersSet()
        {
            // New batch opened (or modal closed) -> start with clean filters.
            if (!ReferenceEquals(BatchInvoices, loadedInvoices))
            {
                loadedInvoices = BatchInvoices;
                ResetFilterState();

                customerOptions = BatchInvoices
                    .Select(i => i.CustomerName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Select(name => new FilterChoice(name, name))
                    .ToList();
            }

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var term = searchTerm.Trim();
            IEnumerable<HomeSalesInvoiceBatchInvoiceRow> query = BatchInvoices;

            if (!string.IsNullOrEmpty(selectedOrderType))
            {
                query = query.Where(i => string.Equals(i.OrderType, selectedOrderType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(selectedCustomer))
            {
                query = query.Where(i => string.Equals(i.CustomerName, selectedCustomer, StringComparison.OrdinalIgnoreCase));
            }

            if (term.Length > 0)
            {
                query = query.Where(i =>
                    i.InvoiceCode.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || i.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            filteredInvoices = (sortBy switch
            {
                SortInvoiceDate => query.OrderByDescending(i => i.SalesInvoiceDate).ThenBy(i => i.InvoiceCode, StringComparer.OrdinalIgnoreCase),
                SortCode => query.OrderBy(i => i.InvoiceCode, StringComparer.OrdinalIgnoreCase),
                _ => query.OrderByDescending(i => i.CreatedDate).ThenByDescending(i => i.SalesInvoiceId)
            }).ToList();
        }

        private void ResetFilterState()
        {
            searchTerm = string.Empty;
            selectedOrderType = string.Empty;
            selectedCustomer = string.Empty;
            sortBy = SortCreated;
            filterResetKey++;
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

        private void OnCustomerSelected(string? value)
        {
            selectedCustomer = value ?? string.Empty;
            ApplyFilters();
        }

        private void OnSortSelected(string? value)
        {
            sortBy = string.IsNullOrEmpty(value) ? SortCreated : value;
            ApplyFilters();
        }

        private void ClearFilters()
        {
            var currentSort = sortBy;
            ResetFilterState();
            sortBy = currentSort; // clearing filters keeps the chosen sort
            ApplyFilters();
        }

        private Task CloseAsync() => OnClose.InvokeAsync();

        private async Task HandleKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Escape")
            {
                await OnClose.InvokeAsync();
            }
        }
    }
}