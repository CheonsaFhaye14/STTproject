using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportBatch
{
    public int ImportBatchId { get; set; }

    public string ImportType { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public int? SubDistributorId { get; set; }

    public string? Principal { get; set; }

    public int? TotalRows { get; set; }

    public string BatchStatus { get; set; } = null!;

    public int UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; }

    public virtual ICollection<ImportError> ImportErrors { get; set; } = new List<ImportError>();

    public virtual SubDistributor? SubDistributor { get; set; }

    public virtual User UploadedByNavigation { get; set; } = null!;
}
