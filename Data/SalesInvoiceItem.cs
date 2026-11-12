using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class SalesInvoiceItem
{
    public int SalesInvoiceItemId { get; set; }

    public int SalesInvoiceId { get; set; }

    public int SubdItemId { get; set; }

    public int Quantity { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public decimal Amount { get; set; }

    public int ItemsUomId { get; set; }

    public int? BaseQuantity { get; set; }

    public decimal? StockPriceUsed { get; set; }

    public int? CompanyItemPriceHistoryId { get; set; }

    public string? PriceSource { get; set; }

    public DateTime? PricedAt { get; set; }

    public decimal? BilledAmount { get; set; }

    public virtual CompanyItemPriceHistory? CompanyItemPriceHistory { get; set; }

    public virtual ItemsUom ItemsUom { get; set; } = null!;

    public virtual SalesInvoice SalesInvoice { get; set; } = null!;

    public virtual SubdItem SubdItem { get; set; } = null!;
}
