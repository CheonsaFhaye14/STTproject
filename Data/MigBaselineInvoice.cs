using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class MigBaselineInvoice
{
    public int SubDistributorId { get; set; }

    public int? Invoices { get; set; }

    public int? Items { get; set; }

    public decimal? Amount { get; set; }
}
