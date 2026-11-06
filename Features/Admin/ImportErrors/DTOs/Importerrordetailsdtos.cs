namespace STTproject.Features.Admin.ImportErrors.DTOs
{
    /// <summary>Everything the error details page shows. One unique ImportError plus every batch it appeared in.</summary>
    public class ImportErrorDetailsDto
    {
        public int ImportErrorId { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string? FieldName { get; set; }
        public string Message { get; set; } = string.Empty;

        /// <summary>The problematic value (ImportErrorPresentation.ExtractProblemValue, falling back to the message).</summary>
        public string Value { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
        public string? ResolutionType { get; set; }
        public string? ResolvedByName { get; set; }

        /// <summary>Philippine time (UTC+8).</summary>
        public DateTime? ResolvedAtPht { get; set; }

        public int? SubDistributorId { get; set; }
        public string? SubDistributorName { get; set; }

        public int Occurrences { get; set; }
        public int AffectedInvoices { get; set; }

        /// <summary>The invoice numbers behind AffectedInvoices, from every batch's stored evidence.</summary>
        public List<string> AffectedInvoiceCodes { get; set; } = new();

        /// <summary>The batch whose file verified the fix. Null unless the error was resolved by Verify and Resolve.</summary>
        public int? VerifiedImportBatchId { get; set; }

        /// <summary>
        /// True when Entriel can check this error against a corrected file: a sales invoice row-level error with a fingerprint
        /// and complete (not truncated) evidence of which invoices were affected.
        /// </summary>
        public bool CanVerify { get; set; }
        public DateTime? FirstSeenAtPht { get; set; }
        public DateTime? LastSeenAtPht { get; set; }

        /// <summary>Newest first.</summary>
        public List<ImportErrorBatchDetailDto> Batches { get; set; } = new();

        /// <summary>Affected Excel rows across all batches, newest batch first, then sheet and row number.</summary>
        public List<ImportErrorAffectedRowDto> Rows { get; set; } = new();

        /// <summary>Set when this error is a recurrence: the same problem came back after an earlier error was closed. Null otherwise.</summary>
        public ImportErrorRecurrenceDto? Recurrence { get; set; }

        /// <summary>True when at least one batch hit ImportLogService.MaxAffectedRows, so Rows is not the full list.</summary>
        public bool RowsTruncated { get; set; }
    }

    /// <summary>The earlier error this one follows (ImportError.PreviousImportErrorId). The earlier record is never changed.</summary>
    public class ImportErrorRecurrenceDto
    {
        public int PreviousImportErrorId { get; set; }
        public string PreviousStatus { get; set; } = string.Empty;
        public string? PreviousResolutionType { get; set; }
        public string? PreviousResolvedByName { get; set; }

        /// <summary>Philippine time (UTC+8).</summary>
        public DateTime? PreviousResolvedAtPht { get; set; }

        /// <summary>Total occurrences the earlier error had across its batches.</summary>
        public int PreviousOccurrences { get; set; }
    }

    /// <summary>One ImportErrorBatch link: this error in one uploaded file.</summary>
    public class ImportErrorBatchDetailDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string BatchStatus { get; set; } = string.Empty;
        public string? UploadedByName { get; set; }
        public DateTime UploadedAtPht { get; set; }
        public int Occurrences { get; set; }
        public DateTime? FirstSeenAtPht { get; set; }
        public DateTime? LastSeenAtPht { get; set; }
    }

    public class ImportErrorAffectedRowDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Sheet { get; set; }
        public int? RowNumber { get; set; }
        public string? Invoice { get; set; }

        /// <summary>The source file's own headers and cell text in column order, or null when no snapshot was stored.</summary>
        public List<KeyValuePair<string, string>>? Data { get; set; }
    }

    /// <summary>One batch this error appeared in, with the import context a template is chosen by. Newest first.</summary>
    public sealed record FixContextDto(int ImportBatchId, int? SubDistributorId, string? SubDistributorName, string ImportType, DateTime UploadedAtPht);

    /// <summary>A later upload the admin can verify against.</summary>
    public sealed record VerificationCandidateDto(int ImportBatchId, string FileName, string BatchStatus, string? UploadedByName, DateTime UploadedAtPht);

    public enum VerificationOutcome
    {
        /// <summary>Every affected invoice passed: the error is now Resolved.</summary>
        Verified,
        /// <summary>The same problem is still reported in the file. The error stays Pending.</summary>
        StillInvalid,
        /// <summary>Some affected invoices are not in the file, so they could not be checked. The error stays Pending.</summary>
        RecordsNotFound,
        /// <summary>The check could not be run (unreadable file, wrong batch, unsupported error). The error stays Pending.</summary>
        Unverifiable,
        /// <summary>The error is no longer Pending, so nothing was changed.</summary>
        NotPending
    }

    public sealed record VerificationIssueDto(string? Invoice, string? Sheet, int? RowNumber, string Reason);

    public sealed class VerificationResultDto
    {
        public VerificationOutcome Outcome { get; set; }
        public string Message { get; set; } = string.Empty;
        public int InvoicesChecked { get; set; }
        public List<VerificationIssueDto> Issues { get; set; } = new();
    }

    /// <summary>An import template the admin can open to fix an error caused by the template.</summary>
    public sealed record FixTemplateOption(int ImportTemplateId, string TemplateName, bool IsGlobalDefault);

    /// <summary>A stored original file, ready to hand to the browser.</summary>
    public sealed record ImportOriginalFile(string FileName, string ContentType, byte[] Bytes);
}