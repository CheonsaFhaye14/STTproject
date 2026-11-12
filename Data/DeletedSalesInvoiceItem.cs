using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class DeletedSalesInvoiceItem
{
    public int DeletedSalesInvoiceItemId { get; set; }

    public int DeletedSalesInvoiceId { get; set; }

    public int OriginalSalesInvoiceItemId { get; set; }

    public int SubdItemId { get; set; }

    public int ItemsUomId { get; set; }

    public int Quantity { get; set; }

    public decimal Amount { get; set; }

    public DateTime? CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? UpdatedBy { get; set; }

    public int? BaseQuantity { get; set; }

    public decimal? StockPriceUsed { get; set; }

    public int? CompanyItemPriceHistoryId { get; set; }

    public string? PriceSource { get; set; }

    public DateTime? PricedAt { get; set; }

    public decimal? BilledAmount { get; set; }

    public virtual DeletedSalesInvoice DeletedSalesInvoice { get; set; } = null!;
}
