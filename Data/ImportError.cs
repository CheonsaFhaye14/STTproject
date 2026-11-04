using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportError
{
    public int ImportErrorId { get; set; }

    public int ImportBatchId { get; set; }

    public string ErrorType { get; set; } = null!;

    public string ErrorMessage { get; set; } = null!;

    public string? FieldName { get; set; }

    public string Status { get; set; } = null!;

    public int? SourceRowNumber { get; set; }

    public string? DataJson { get; set; }

    public string? ResolutionType { get; set; }

    public int? ResolvedBy { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ImportBatch ImportBatch { get; set; } = null!;

    public virtual User? ResolvedByNavigation { get; set; }
}
