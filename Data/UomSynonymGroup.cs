using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class UomSynonymGroup
{
    public int UomSynonymGroupId { get; set; }

    public int? SubDistributorId { get; set; }

    public string GroupName { get; set; } = null!;

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public int Version { get; set; }

    public DateTime CreatedDate { get; set; }

    public int CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual SubDistributor? SubDistributor { get; set; }

    public virtual ICollection<UomSynonymExclusion> UomSynonymExclusions { get; set; } = new List<UomSynonymExclusion>();

    public virtual ICollection<UomSynonym> UomSynonyms { get; set; } = new List<UomSynonym>();
}
