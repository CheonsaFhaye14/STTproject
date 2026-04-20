using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class ItemMapping
{
    public int ItemMappingId { get; set; }

    public int SubdItemId { get; set; }

    public int CompanyItemId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public virtual CompanyItem CompanyItem { get; set; } = null!;

    public virtual User? CreatedByNavigation { get; set; }

    public virtual SubdItem SubdItem { get; set; } = null!;
}
