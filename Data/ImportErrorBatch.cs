using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportErrorBatch
{
    public int ImportErrorBatchId { get; set; }

    public int ImportErrorId { get; set; }

    public int ImportBatchId { get; set; }

    public int Occurrences { get; set; }

    public string? DataJson { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public virtual ImportBatch ImportBatch { get; set; } = null!;

    public virtual ImportError ImportError { get; set; } = null!;
}
