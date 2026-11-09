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
        /// Only for an error resolved by a recheck (ResolutionType "Verified by recheck"): the ORIGINAL batches whose stored files were rechecked
        /// with the current template and master data, newest first. They are the error's own batch links, which never change once it is Resolved
        /// (a recurrence creates a new error). Empty otherwise. Not the same as VerifiedImportBatchId, which is a corrected file that was re-imported.
        /// </summary>
        public List<int> RecheckedImportBatchIds { get; set; } = new();

        /// <summary>
        /// The way the admin chose in the Fix window the last time (reimport, template or mapping), kept in ImportError.DataJson as FixChoice.
        /// It is what the admin chose to do, not proof of what they changed. Null when none was recorded.
        /// </summary>
        public string? FixChoice { get; set; }
        public DateTime? FixChoiceAtPht { get; set; }

        /// <summary>
        /// True when Entriel can check this error against a corrected file: a sales invoice row-level error with a fingerprint
        /// and complete (not truncated) evidence of which invoices were affected.
        /// </summary>
        public bool CanVerify { get; set; }
        public DateTime? FirstSeenAtPht { get; set; }
        public DateTime? LastSeenAtPht { get; set; }

        /// <summary>Newest first.</summary>
        public List<ImportErrorBatchDetailDto> Batches { get; set; } = new();

        /// <summary>
        /// The same batches as <see cref="Batches"/>, newest first, with batches that uploaded the identical file (same ImportFileId)
        /// grouped together. A batch with no shared file (uploaded before deduplication) is a group of one. Every batch is still its own
        /// upload attempt: grouping changes how they are shown, never what is stored.
        /// </summary>
        public List<ImportErrorUploadGroupDto> UploadGroups { get; set; } = new();

        /// <summary>Affected Excel rows across all batches, newest batch first, then sheet and row number. One entry per batch per row.</summary>
        public List<ImportErrorAffectedRowDto> Rows { get; set; } = new();

        /// <summary>
        /// The same rows as <see cref="Rows"/>, one entry per logical Excel row (sheet and row number) however many uploads reported it.
        /// Each keeps the snapshot it had in every upload. Nothing stored is merged away: this is only how the page lists it.
        /// </summary>
        public List<ImportErrorLogicalRowDto> LogicalRows { get; set; } = new();

        /// <summary>The error's own facts shown in the Overview for types that need more than one value (customer code and name, item code, name and unit).</summary>
        public List<ImportErrorFactDto> Facts { get; set; } = new();

        /// <summary>One of <see cref="ImportErrorCorrectionState"/>. See <see cref="CorrectionNote"/> for what it is based on.</summary>
        public string CorrectionState { get; set; } = ImportErrorCorrectionState.StillReported;
        public string CorrectionNote { get; set; } = string.Empty;

        /// <summary>The upload that confirmed the correction. Only set when <see cref="CorrectionState"/> is Corrected by a re-import.</summary>
        public int? CorrectionBatchId { get; set; }

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

        /// <summary>The shared file this batch uploaded. Null for a batch with no shared file.</summary>
        public int? ImportFileId { get; set; }

        /// <summary>The Excel row(s) the importer reported for this error in this attempt, for example "Sales · 165". Null when none was stored.</summary>
        public string? ReportedRowsText { get; set; }
    }

    /// <summary>Upload attempts of one identical file, newest first.</summary>
    public class ImportErrorUploadGroupDto
    {
        public int? ImportFileId { get; set; }

        /// <summary>The newest attempt's file name. Each attempt keeps its own name in <see cref="Batches"/>.</summary>
        public string FileName { get; set; } = string.Empty;

        public List<ImportErrorBatchDetailDto> Batches { get; set; } = new();

        public int AttemptCount => Batches.Count;
        public int Occurrences => Batches.Sum(b => b.Occurrences);
        public DateTime FirstUploadedAtPht => Batches.Min(b => b.UploadedAtPht);
        public DateTime LastUploadedAtPht => Batches.Max(b => b.UploadedAtPht);
    }

    public class ImportErrorAffectedRowDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? Sheet { get; set; }
        public int? RowNumber { get; set; }
        public string? Invoice { get; set; }

        /// <summary>
        /// True for a row the importer reported. False for a row listed only because it belongs to the same invoice as a reported row.
        /// Evidence stored before this existed has no such mark, and its rows count as reported.
        /// </summary>
        public bool IsReportedRow { get; set; } = true;

        /// <summary>The source file's own headers and cell text in column order, or null when no snapshot was stored.</summary>
        public List<KeyValuePair<string, string>>? Data { get; set; }

        /// <summary>The template field this error is about when it is one field (for example "SalesManName"). Null when none was stored.</summary>
        public string? ProblemField { get; set; }

        /// <summary>That field's value in this row. Null together with <see cref="HasProblemValue"/> false when none was stored; blank means the cell was empty.</summary>
        public string? ProblemValue { get; set; }
        public bool HasProblemValue { get; set; }

        /// <summary>Structured facts stored with a reported row (CustomerCode, CustomerName, ItemCode, ItemName, UploadedUom). Null for older evidence.</summary>
        public Dictionary<string, string?>? Details { get; set; }
    }

    /// <summary>One Excel row (sheet + row number) of an error, with the snapshot each upload kept of it. Newest upload first.</summary>
    public class ImportErrorLogicalRowDto
    {
        public string? Sheet { get; set; }
        public int? RowNumber { get; set; }
        public string? Invoice { get; set; }

        /// <summary>True when at least one upload reported this row. False when it is only ever listed because it belongs to a reported invoice.</summary>
        public bool IsReportedRow { get; set; }

        /// <summary>From the newest snapshot that has one.</summary>
        public string? ProblemField { get; set; }
        public string? ProblemValue { get; set; }
        public bool HasProblemValue { get; set; }
        public List<KeyValuePair<string, string>>? Data { get; set; }

        /// <summary>Compact facts from the newest snapshot that has them (RowCustomer, RowItemCode, RowItem, RowQty, RowAmount, RowSalesman). Null for older evidence.</summary>
        public Dictionary<string, string?>? Details { get; set; }

        /// <summary>What the newest recheck read for this row's field with the template of that moment. Null when no recheck stored it.</summary>
        public string? CheckedValue { get; set; }
        public bool HasCheckedValue { get; set; }
        public DateTime? CheckedAtPht { get; set; }

        /// <summary>True when the uploads disagree about what the row contained.</summary>
        public bool SnapshotsDiffer { get; set; }

        public List<ImportErrorRowSnapshotDto> Snapshots { get; set; } = new();
        public int ReportedInUploads => Snapshots.Count(s => s.IsReportedRow);
    }

    /// <summary>What one upload kept of one row.</summary>
    public class ImportErrorRowSnapshotDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime UploadedAtPht { get; set; }
        public bool IsReportedRow { get; set; }
        public string? ProblemValue { get; set; }
        public bool HasProblemValue { get; set; }
        public List<KeyValuePair<string, string>>? Data { get; set; }
    }

    /// <summary>A labeled value for the Overview.</summary>
    public sealed record ImportErrorFactDto(string Label, string Value);

    /// <summary>Whether an error has been fixed, judged only from evidence Entriel already holds.</summary>
    public static class ImportErrorCorrectionState
    {
        /// <summary>Reported in the most recent checked upload for this subdistributor and import type.</summary>
        public const string StillReported = "Still reported";

        /// <summary>A later validation confirmed it: a corrected file that passed the verification, or a recheck with current settings.</summary>
        public const string Corrected = "Corrected";

        /// <summary>Reported earlier, and no later validation confirms either way. Not being listed in a later upload is not proof.</summary>
        public const string PreviouslyReported = "Previously reported";
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

    /// <summary>What an uploaded workbook contains, read from the stored original file: every sheet with its first rows.</summary>
    public sealed class ImportSheetPreviewDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        /// <summary>Set when the file could not be found or opened; Sheets is then empty.</summary>
        public string? Problem { get; set; }
        public List<ImportSheetDto> Sheets { get; set; } = new();
    }

    public sealed class ImportSheetDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        /// <summary>Last used row and column in the sheet.</summary>
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        /// <summary>How many columns the preview rows show (the sheet may have more).</summary>
        public int ShownColumns { get; set; }
        /// <summary>The first non-empty rows, with their Excel row numbers.</summary>
        public List<ImportSheetRowDto> Rows { get; set; } = new();
    }

    public sealed class ImportSheetRowDto
    {
        public int RowNumber { get; set; }
        public List<string> Cells { get; set; } = new();
    }
}