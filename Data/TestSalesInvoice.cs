using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class TestSalesInvoice
{
    public int SalesInvoiceId { get; set; }

    public string SalesInvoiceCode { get; set; } = null!;

    public DateOnly SalesInvoiceDate { get; set; }

    public int SubDistributorId { get; set; }

    public string SalesMan { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual ICollection<TestSalesInvoiceCustomer> TestSalesInvoiceCustomers { get; set; } = new List<TestSalesInvoiceCustomer>();
}
