using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class SalesInvoiceDeletionBatch
{
    public int DeletionBatchId { get; set; }

    public string DeletionType { get; set; } = null!;

    public int SubDistributorId { get; set; }

    public int? PeriodYear { get; set; }

    public int? PeriodMonth { get; set; }

    public int InvoiceCount { get; set; }

    public string Status { get; set; } = null!;

    public int DeletedBy { get; set; }

    public DateTime DeletedDate { get; set; }

    public int? RestoredBy { get; set; }

    public DateTime? RestoredDate { get; set; }

    public virtual ICollection<DeletedSalesInvoice> DeletedSalesInvoices { get; set; } = new List<DeletedSalesInvoice>();
}
