using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class SubdItemUom
{
    public int SubdItemUomId { get; set; }

    public int SubdItemId { get; set; }

    public string UomName { get; set; } = null!;

    public decimal ConversionToBase { get; set; }

    public decimal Price { get; set; }

    public bool IsBaseUnit { get; set; }

    public bool IsSellable { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual SubdItem SubdItem { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
