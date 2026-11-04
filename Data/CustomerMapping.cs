using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class CustomerMapping
{
    public int CustomerMappingId { get; set; }

    public int SubDistributorId { get; set; }

    public string? SubdCustCode { get; set; }

    public string SubdCustName { get; set; } = null!;

    public int CustomerId { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual SubDistributor SubDistributor { get; set; } = null!;
}
