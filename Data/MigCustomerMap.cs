using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class MigCustomerMap
{
    public int CustomerId { get; set; }

    public int SubDistributorId { get; set; }

    public string CustomerCode { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public string? Scode { get; set; }

    public string? Sname { get; set; }

    public int? KeepId { get; set; }
}
