using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class SubdItem
{
    public int SubdItemId { get; set; }

    public string SubdItemCode { get; set; } = null!;

    public string ItemName { get; set; } = null!;

    public int SubDistributorId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int CompanyItemId { get; set; }

    public virtual CompanyItem CompanyItem { get; set; } = null!;

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<ItemsUom> ItemsUoms { get; set; } = new List<ItemsUom>();

    public virtual ICollection<SalesInvoiceItem> SalesInvoiceItems { get; set; } = new List<SalesInvoiceItem>();

    public virtual SubDistributor SubDistributor { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
