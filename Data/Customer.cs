using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class Customer
{
    public int CustomerId { get; set; }

    public string CustomerCode { get; set; } = null!;

    public string CustomerName { get; set; } = null!;

    public string CustomerType { get; set; } = null!;

    public int SubDistributorId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual CustomerBranch? CustomerBranch { get; set; }

    public virtual ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();

    public virtual SubDistributor SubDistributor { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
