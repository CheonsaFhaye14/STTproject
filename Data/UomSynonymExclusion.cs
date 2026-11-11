using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class UomSynonymExclusion
{
    public int UomSynonymExclusionId { get; set; }

    public int SubDistributorId { get; set; }

    public int UomSynonymGroupId { get; set; }

    public int? UomSynonymId { get; set; }

    public DateTime CreatedDate { get; set; }

    public int CreatedBy { get; set; }

    public virtual SubDistributor SubDistributor { get; set; } = null!;

    public virtual UomSynonym? UomSynonym { get; set; }

    public virtual UomSynonymGroup UomSynonymGroup { get; set; } = null!;
}
