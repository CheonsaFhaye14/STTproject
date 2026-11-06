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

    public string? Fingerprint { get; set; }

    public int? PreviousImportErrorId { get; set; }

    public int? VerifiedImportBatchId { get; set; }

    public virtual ImportBatch ImportBatch { get; set; } = null!;

    public virtual ICollection<ImportErrorBatch> ImportErrorBatches { get; set; } = new List<ImportErrorBatch>();

    public virtual ICollection<ImportError> InversePreviousImportError { get; set; } = new List<ImportError>();

    public virtual ImportError? PreviousImportError { get; set; }

    public virtual User? ResolvedByNavigation { get; set; }
}
