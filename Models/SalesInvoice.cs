using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class SalesInvoice
{
    public int SalesInvoiceId { get; set; }

    public string SalesInvoiceCode { get; set; } = null!;

    public DateOnly SalesInvoiceDate { get; set; }

    public int CustomerId { get; set; }

    public int CustomerBranchId { get; set; }

    public int SubDistributorId { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public string OrderType { get; set; } = null!;

    public DateOnly OrderDate { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual CustomerBranch CustomerBranch { get; set; } = null!;

    public virtual ICollection<SalesInvoiceItem> SalesInvoiceItems { get; set; } = new List<SalesInvoiceItem>();

    public virtual SubDistributor SubDistributor { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
