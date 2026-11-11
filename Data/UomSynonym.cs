using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class UomSynonym
{
    public int UomSynonymId { get; set; }

    public int UomSynonymGroupId { get; set; }

    public int? SubDistributorId { get; set; }

    public string SynonymText { get; set; } = null!;

    public int SortOrder { get; set; }

    public virtual ICollection<UomSynonymExclusion> UomSynonymExclusions { get; set; } = new List<UomSynonymExclusion>();

    public virtual UomSynonymGroup UomSynonymGroup { get; set; } = null!;
}
