using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class PriceRecalcRun
{
    public int RunId { get; set; }

    public int SubDistributorId { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public bool IsDryRun { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? AppliedAt { get; set; }

    public DateTime? UndoneAt { get; set; }

    public int? Lines { get; set; }

    public int? LinesChanged { get; set; }

    public int? LinesUnpriced { get; set; }

    public decimal? OldTotal { get; set; }

    public decimal? NewTotal { get; set; }

    public virtual ICollection<PriceRecalcLine> PriceRecalcLines { get; set; } = new List<PriceRecalcLine>();

    public virtual SubDistributor SubDistributor { get; set; } = null!;
}
