using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace STTproject.Models;

public partial class SubdItem
{
    public int SubdItemId { get; set; }

    public string SubdItemCode { get; set; } = null!;

    public string ItemName { get; set; } = null!;

    public int SubDistributorId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ItemMapping? ItemMapping { get; set; }

    public virtual ICollection<SalesInvoiceItem> SalesInvoiceItems { get; set; } = new List<SalesInvoiceItem>();

    public virtual SubDistributor SubDistributor { get; set; } = null!;

    public virtual SubdItemUom? SubdItemUom { get; set; }

    public virtual User? UpdatedByNavigation { get; set; }

    [NotMapped]
    public string UOM => SubdItemUom?.UomName ?? string.Empty;

    [NotMapped]
    public int QuantityPerPiece =>
        SubdItemUom is null
            ? 1
            : Math.Max(1, (int)Math.Round(SubdItemUom.ConversionToBase));

    [NotMapped]
    public int Price =>
        SubdItemUom is null
            ? 0
            : (int)Math.Round(SubdItemUom.Price);

    [NotMapped]
    public int PricePerPiece =>
        QuantityPerPiece <= 0
            ? Price
            : (int)Math.Round((decimal)Price / QuantityPerPiece);

    [NotMapped]
    public string UnitContent => "1";
}
