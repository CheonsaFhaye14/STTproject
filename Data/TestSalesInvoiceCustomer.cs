using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class TestSalesInvoiceCustomer
{
    public int SalesInvoiceCustomerId { get; set; }

    public int SalesInvoiceId { get; set; }

    public int CustomerId { get; set; }

    public int CustomerBranchId { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual TestSalesInvoice SalesInvoice { get; set; } = null!;

    public virtual ICollection<TestSalesInvoiceItem> TestSalesInvoiceItems { get; set; } = new List<TestSalesInvoiceItem>();
}
