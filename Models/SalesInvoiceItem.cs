using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class SalesInvoiceItem
{
    public int SalesInvoiceItemId { get; set; }

    public int SalesInvoiceId { get; set; }

    public int SubdItemId { get; set; }

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual SalesInvoice SalesInvoice { get; set; } = null!;

    public virtual SubdItem SubdItem { get; set; } = null!;
}
