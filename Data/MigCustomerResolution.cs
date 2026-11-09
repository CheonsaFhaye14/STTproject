using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class MigCustomerResolution
{
    public int CustomerId { get; set; }

    public string Action { get; set; } = null!;

    public int? TargetCustomerId { get; set; }

    public string? Note { get; set; }
}
