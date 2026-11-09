namespace STTproject.Features.Admin.Dashboard.DTOs
{
    // ---------------------------------------------------------------------
    // Existing DTOs (kept so the current sections keep compiling until the
    // new dashboard replaces them). Delete once CustPerSubd,
    // TotalMonthlyAnnualPrice and SubdItemPerSubd are removed.
    // ---------------------------------------------------------------------
    public class CustomerPerSubdDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int TotalCount => ActiveCount + InactiveCount;
    }

    public class TotalPricesPerSubdMonthlyAnnualDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public decimal TotalPrice { get; set; }
    }

    public class SubdItemPerSubdDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public int SubdItemCount { get; set; }
        public List<string> Principals { get; set; } = new();
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int TotalCount => ActiveCount + InactiveCount;
    }

    // ---------------------------------------------------------------------
    // Shared
    // ---------------------------------------------------------------------
    public class SubdLookupDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    // ---------------------------------------------------------------------
    // Sales card + panel
    // One row per sub distributor per month. The page loads this once and
    // does every year / month / sub distributor filter in memory, so the
    // filters respond instantly without another database call.
    // ---------------------------------------------------------------------
    public class SalesMonthDto
    {
        public int SubDistributorId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>Sum of Amount on OrderType = 'Invoice' lines.</summary>
        public decimal InvoiceAmount { get; set; }

        /// <summary>credit  total as a positive number (stored negative in the database).</summary>
        public decimal CreditAmount { get; set; }

        /// <summary>Number of SalesInvoice headers with OrderType = 'Invoice'.</summary>
        public int InvoiceCount { get; set; }

        /// <summary>Number of SalesInvoice headers with OrderType = 'Credit'.</summary>
        public int CreditCount { get; set; }

        /// <summary>All SalesInvoice headers (invoices + credit) — matches the Sales Invoices page total.</summary>
        public int TotalCount => InvoiceCount + CreditCount;

        public decimal NetAmount => InvoiceAmount - CreditAmount;
    }

    public class SalesDashboardDto
    {
        public List<SubdLookupDto> SubDistributors { get; set; } = new();
        public List<SalesMonthDto> Months { get; set; } = new();
    }

    // ---------------------------------------------------------------------
    // Pending import errors card + panel
    // ---------------------------------------------------------------------
    public class ErrorTypeCountDto
    {
        public string ErrorType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class PendingErrorBatchDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int? SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public string BatchStatus { get; set; } = string.Empty;

        /// <summary>How many batches share this batch's ImportFile (same file hash). 1 = uploaded once.</summary>
        public int TimesUploaded { get; set; } = 1;

        public int PendingCount { get; set; }

        /// <summary>Earliest CreatedAt of the pending errors counted under this batch.</summary>
        public DateTime OldestPendingAt { get; set; }

        public List<ErrorTypeCountDto> Types { get; set; } = new();
    }

    public class PendingErrorsSummaryDto
    {
        /// <summary>Distinct pending ImportError rows (each master error counted once).</summary>
        public int PendingCount { get; set; }

        /// <summary>Pending errors first found in the last 7 days.</summary>
        public int NewThisWeek { get; set; }

        public List<PendingErrorBatchDto> Batches { get; set; } = new();
    }

    // ---------------------------------------------------------------------
    // Price increases card + panel
    // ---------------------------------------------------------------------
    public class PendingPriceIncreaseDto
    {
        public int CompanyItemPriceHistoryId { get; set; }
        public int CompanyItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Principal { get; set; } = string.Empty;

        /// <summary>null = standard price; 'Regional' / 'National' for dual-priced items.</summary>
        public string? PriceListType { get; set; }

        public DateTime EffectivityDate { get; set; }

        /// <summary>Price before this schedule (CompanyItemPriceHistory.OldPrice).</summary>
        public decimal OldPrice { get; set; }

        public decimal IncreaseAmount { get; set; }
        public decimal NewPrice { get; set; }

        /// <summary>Active SubdItems of this company item (on this price list when it has one).</summary>
        public int SubdItemsAffected { get; set; }

        public bool IsOverdue { get; set; }
    }

    // ---------------------------------------------------------------------
    // Records card + panel
    // ---------------------------------------------------------------------
    public class SubdRecordDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int CustomersActive { get; set; }
        public int CustomersInactive { get; set; }
        public int SubdItemsActive { get; set; }
        public int SubdItemsInactive { get; set; }
        public int PrincipalCount { get; set; }

        public int CustomersTotal => CustomersActive + CustomersInactive;
        public int SubdItemsTotal => SubdItemsActive + SubdItemsInactive;
    }

    public class RecordsDto
    {
        public int CompanyItemsActive { get; set; }
        public int CompanyItemsInactive { get; set; }
        public List<SubdRecordDto> PerSubd { get; set; } = new();

        public int CustomersActive => PerSubd.Sum(x => x.CustomersActive);
        public int CustomersInactive => PerSubd.Sum(x => x.CustomersInactive);
        public int SubdItemsActive => PerSubd.Sum(x => x.SubdItemsActive);
        public int SubdItemsInactive => PerSubd.Sum(x => x.SubdItemsInactive);
        public int SubdsActive => PerSubd.Count(x => x.IsActive);
        public int SubdsInactive => PerSubd.Count(x => !x.IsActive);
    }
}