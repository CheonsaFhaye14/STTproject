using System;
using System.Collections.Generic;

namespace STTproject.Models.Tables;

public partial class CustomerBranch
{
    public int CustomerBranchId { get; set; }

    public int CustomerId { get; set; }

    public string BranchName { get; set; } = null!;

    public string AddressLine { get; set; } = null!;

    public string City { get; set; } = null!;

    public string Province { get; set; } = null!;

    public int ZipCode { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();

    public virtual User? UpdatedByNavigation { get; set; }
}
