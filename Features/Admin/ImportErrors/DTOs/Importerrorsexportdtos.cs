namespace STTproject.Features.Admin.ImportErrors.DTOs
{
    public enum ImportErrorExportContent
    {
        /// <summary>Only the error report (XLSX or CSV).</summary>
        ErrorReport,
        /// <summary>Only the original uploaded Excel files.</summary>
        OriginalFiles,
        /// <summary>The report plus the original files, in one ZIP.</summary>
        Both
    }

    public enum ImportErrorExportFormat { Xlsx, Csv }

    /// <summary>Which original files go into the export.</summary>
    public enum ImportErrorExportFileScope
    {
        /// <summary>Every batch the selected errors appeared in, each batch once.</summary>
        RelatedToErrors,
        /// <summary>Every uploaded original file, including batches without errors.</summary>
        All,
        /// <summary>The batches in <see cref="ImportErrorExportRequest.SelectedBatchIds"/>.</summary>
        Selected
    }

    /// <summary>Filters for the file picker. They never restrict which errors are exported (and the reverse).</summary>
    public class ImportFileFilter
    {
        public string? Search { get; set; }
        public int? SubDistributorId { get; set; }
        public int? UploadedBy { get; set; }
        public string? ImportType { get; set; }

        /// <summary>Calendar dates in Philippine time (UTC+8), both inclusive.</summary>
        public DateTime? UploadedFrom { get; set; }
        public DateTime? UploadedTo { get; set; }
    }

    public class ImportErrorExportRequest
    {
        public ImportErrorExportContent Content { get; set; } = ImportErrorExportContent.ErrorReport;

        /// <summary>True = every import error, ignoring the page filters.</summary>
        public bool AllErrors { get; set; }

        /// <summary>The filter the admin had on screen (ignored when AllErrors is true).</summary>
        public ImportErrorFilter? Filter { get; set; }

        /// <summary>True when the filter came from the "By Import File" view (it filters files, not errors).</summary>
        public bool IsImportBatchView { get; set; }

        /// <summary>XLSX = one workbook. CSV = one CSV file per sheet, packaged in a ZIP.</summary>
        public ImportErrorExportFormat Format { get; set; } = ImportErrorExportFormat.Xlsx;

        // Report sections. Error Details is always included.
        public bool IncludeSummary { get; set; } = true;
        public bool IncludeImportHistory { get; set; } = true;

        /// <summary>Original Excel row data, sheet name, row number and problematic values. Optional, off by default.</summary>
        public bool IncludeAffectedRows { get; set; }

        /// <summary>Original files to include (ignored when Content is ErrorReport).</summary>
        public ImportErrorExportFileScope FileScope { get; set; } = ImportErrorExportFileScope.RelatedToErrors;

        /// <summary>Filter the Selected files were picked under. The server re-checks the selection against it.</summary>
        public ImportFileFilter? FileFilter { get; set; }

        /// <summary>ImportBatchIds picked by the admin (FileScope = Selected).</summary>
        public List<int> SelectedBatchIds { get; set; } = new();
    }

    public class ImportErrorExportResult
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";

        public int ErrorCount { get; set; }
        public int OriginalFileCount { get; set; }

        /// <summary>Original files that were requested but could not be read. The export still succeeds without them.</summary>
        public List<string> MissingFiles { get; set; } = new();
    }

    public class ImportFileListItemDto
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ImportType { get; set; } = string.Empty;
        public string BatchStatus { get; set; } = string.Empty;
        public string? SubDistributorName { get; set; }
        public string? UploadedByName { get; set; }

        /// <summary>Philippine time (UTC+8).</summary>
        public DateTime UploadedAtPht { get; set; }
    }

    public class ImportFilePage
    {
        public List<ImportFileListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
    }

    public class ImportFileOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ImportFileFilterOptions
    {
        public List<ImportFileOption> SubDistributors { get; set; } = new();
        public List<ImportFileOption> Uploaders { get; set; } = new();
        public List<string> ImportTypes { get; set; } = new();
    }

    /// <summary>Thrown with a message that is safe to show to the admin as-is.</summary>
    public class ImportErrorExportException : Exception
    {
        public ImportErrorExportException(string message) : base(message) { }
    }
}