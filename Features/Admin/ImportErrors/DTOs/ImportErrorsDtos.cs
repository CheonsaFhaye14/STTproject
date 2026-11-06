namespace STTproject.Features.Admin.ImportErrors.DTOs
{
    /// <summary>One row of the "All Errors" view: one unique ImportError.</summary>
    public class ImportErrorListDto
    {
        public int ImportErrorId { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string? FieldName { get; set; }

        /// <summary>The problematic value from the Excel file (see ImportErrorPresentation.ExtractProblemValue).</summary>
        public string Value { get; set; } = string.Empty;

        public int? SubDistributorId { get; set; }
        public string? SubDistributorName { get; set; }

        /// <summary>Sum of ImportErrorBatch.Occurrences across every batch this error appeared in.</summary>
        public int Occurrences { get; set; }
        public DateTime? LastSeenAt { get; set; }

        public string Status { get; set; } = string.Empty;
        public string? ResolutionType { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    /// <summary>One row of the "By Import File" view: one ImportBatch that has errors.</summary>
    public class ImportBatchListDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int? SubDistributorId { get; set; }
        public string? SubDistributorName { get; set; }
        public string BatchStatus { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }

        /// <summary>Distinct ImportErrors linked to this batch (ImportErrorBatch rows).</summary>
        public int UniqueErrors { get; set; }
        public int Occurrences { get; set; }

        /// <summary>How many of those unique errors are still Pending. Status lives on ImportError, so it is shared across files.</summary>
        public int PendingErrors { get; set; }
    }

    public class ImportErrorStatusCounts
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Ignored { get; set; }
        public int Resolved { get; set; }
    }

    public class ImportErrorFilter
    {
        public string? Search { get; set; }

        /// <summary>All Errors view: "", "pending", "ignored" or "resolved". By Import File view: the ImportBatch.BatchStatus (case-insensitive).</summary>
        public string? Status { get; set; }

        /// <summary>An ImportError.ErrorType value, for example "SkuNotFound". Ignored in the By Import File view.</summary>
        public string? ErrorType { get; set; }

        public int? SubDistributorId { get; set; }

        /// <summary>
        /// All Errors view only: keep errors last seen (ImportErrorBatch.LastSeenAt) on these Philippine-time calendar
        /// dates, both inclusive. The Export window sets these; the list page leaves them empty.
        /// </summary>
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }

        public string SortColumn { get; set; } = string.Empty;
        public bool SortAscending { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}