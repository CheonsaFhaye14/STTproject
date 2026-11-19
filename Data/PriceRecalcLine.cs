using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class PriceRecalcLine
{
    public int RunId { get; set; }

    public int SalesInvoiceItemId { get; set; }

    public int SalesInvoiceId { get; set; }

    public DateOnly SalesInvoiceDate { get; set; }

    public int CompanyItemId { get; set; }

    public decimal OldAmount { get; set; }

    public decimal? NewAmount { get; set; }

    public decimal? BaseQuantity { get; set; }

    public decimal? StockConvToBase { get; set; }

    public decimal? StockPrice { get; set; }

    public int? CompanyItemPriceHistoryId { get; set; }

    public string? Issue { get; set; }

    public string? PricingNote { get; set; }

    public virtual PriceRecalcRun Run { get; set; } = null!;
}
