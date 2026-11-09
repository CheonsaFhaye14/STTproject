using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class SubdCustomer
{
    public int SubdCustomerId { get; set; }

    public int CustomerId { get; set; }

    public string SubdCustCode { get; set; } = null!;

    public string SubdCustName { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual User? UpdatedByNavigation { get; set; }
}
