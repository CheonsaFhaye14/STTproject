using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class TestSalesInvoiceItem
{
    public int SalesInvoiceItemId { get; set; }

    public int SalesInvoiceCustomerId { get; set; }

    public int SubdItemId { get; set; }

    public int ItemsUomId { get; set; }

    public int Quantity { get; set; }

    public decimal Amount { get; set; }

    public string OrderType { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual TestSalesInvoiceCustomer SalesInvoiceCustomer { get; set; } = null!;
}
