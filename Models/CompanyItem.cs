using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class CompanyItem
{
    public int CompanyItemId { get; set; }

    public string ItemCode { get; set; } = null!;

    public string ItemName { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual ICollection<SubdItem> SubdItems { get; set; } = new List<SubdItem>();
}
