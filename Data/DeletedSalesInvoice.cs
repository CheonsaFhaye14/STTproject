using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class DeletedSalesInvoice
{
    public int DeletedSalesInvoiceId { get; set; }

    public int DeletionBatchId { get; set; }

    public int OriginalSalesInvoiceId { get; set; }

    public string SalesInvoiceCode { get; set; } = null!;

    public DateOnly SalesInvoiceDate { get; set; }

    public int CustomerId { get; set; }

    public int SubDistributorId { get; set; }

    public string OrderType { get; set; } = null!;

    public string? SalesMan { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual ICollection<DeletedSalesInvoiceItem> DeletedSalesInvoiceItems { get; set; } = new List<DeletedSalesInvoiceItem>();

    public virtual SalesInvoiceDeletionBatch DeletionBatch { get; set; } = null!;
}
