using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class VwSubdCustomerKey
{
    public int SubDistributorId { get; set; }

    public string SubdCustCode { get; set; } = null!;

    public string SubdCustName { get; set; } = null!;

    public int SubdCustomerId { get; set; }

    public int CustomerId { get; set; }
}
