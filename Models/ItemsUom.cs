using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class ItemsUom
{
    public int ItemsUomId { get; set; }

    public int CompanyItemId { get; set; }

    public string UomName { get; set; } = null!;

    public decimal ConversionToBase { get; set; }

    public bool IsBaseUnit { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public decimal? Price { get; set; }

    public virtual CompanyItem CompanyItem { get; set; } = null!;

    public virtual User? CreatedByNavigation { get; set; }

    public virtual User? UpdatedByNavigation { get; set; }
}
