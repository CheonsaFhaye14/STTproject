using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportFile
{
    public int ImportFileId { get; set; }

    public string ImportType { get; set; } = null!;

    public int? SubDistributorId { get; set; }

    public int? ImportTemplateId { get; set; }

    public int? ImportTemplateVersion { get; set; }

    public string OriginalFileName { get; set; } = null!;

    public string StoredPath { get; set; } = null!;

    public string Sha256 { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string Status { get; set; } = null!;

    public int? TotalRows { get; set; }

    public int? RecordCount { get; set; }

    public int? ErrorCount { get; set; }

    public int? WarningCount { get; set; }

    public int? UploadedBy { get; set; }

    public DateTime UploadedDate { get; set; }

    public virtual ICollection<ImportFileSheet> ImportFileSheets { get; set; } = new List<ImportFileSheet>();

    public virtual ImportTemplate? ImportTemplate { get; set; }

    public virtual SubDistributor? SubDistributor { get; set; }
}
