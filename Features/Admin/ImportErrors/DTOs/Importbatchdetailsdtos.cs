namespace STTproject.Features.Admin.ImportErrors.DTOs
{
    /// <summary>Everything the import file details page shows: one ImportBatch plus the unique errors found in it.</summary>
    public class ImportBatchDetailsDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ImportType { get; set; } = string.Empty;
        public string BatchStatus { get; set; } = string.Empty;

        public int? SubDistributorId { get; set; }
        public string? SubDistributorName { get; set; }
        public string? UploadedByName { get; set; }

        /// <summary>Philippine time (UTC+8).</summary>
        public DateTime UploadedAtPht { get; set; }

        public int UniqueErrors { get; set; }

        /// <summary>Sum of ImportErrorBatch.Occurrences for this batch only.</summary>
        public int Occurrences { get; set; }

        /// <summary>Status lives on ImportError, so a pending error here is pending in every file it appeared in.</summary>
        public int PendingErrors { get; set; }

        /// <summary>Pending first, then most occurrences, then id.</summary>
        public List<ImportBatchErrorDto> Errors { get; set; } = new();
    }

    /// <summary>One unique ImportError as it appeared in this batch.</summary>
    public class ImportBatchErrorDto
    {
        public int ImportErrorId { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string? FieldName { get; set; }

        /// <summary>The problematic value (ImportErrorPresentation.ExtractProblemValue, falling back to the message).</summary>
        public string Value { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        /// <summary>Occurrences in this batch only.</summary>
        public int Occurrences { get; set; }
    }
}