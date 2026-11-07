using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportErrorEvidence
{
    public int EvidenceId { get; set; }

    public int ImportErrorId { get; set; }

    public string EvidenceHash { get; set; } = null!;

    public string? DataJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ImportError ImportError { get; set; } = null!;

    public virtual ICollection<ImportErrorBatch> ImportErrorBatches { get; set; } = new List<ImportErrorBatch>();
}
