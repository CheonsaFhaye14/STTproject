using System;
using System.Collections.Generic;

namespace STTproject.Data.Entities;

public partial class ItemsUom
{
    public int ItemsUomId { get; set; }

    public string UomName { get; set; } = null!;

    public decimal ConversionToBase { get; set; }

    public bool IsBaseUnit { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public decimal Price { get; set; }

    public int SubdItemId { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<ItemsUomPriceHistory> ItemsUomPriceHistories { get; set; } = new List<ItemsUomPriceHistory>();

    public virtual ICollection<SalesInvoiceItem> SalesInvoiceItems { get; set; } = new List<SalesInvoiceItem>();

    public virtual SubdItem SubdItem { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
