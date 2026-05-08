using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ItemsUomPriceHistory
{
    public int ItemsUomPriceHistoryId { get; set; }

    public int ItemsUomId { get; set; }

    public int CompanyItemId { get; set; }

    public decimal OldPrice { get; set; }

    public decimal NewPrice { get; set; }

    public decimal? PriceIncreasePercent { get; set; }

    public DateTime EffectivityDate { get; set; }

    public DateTime AppliedDate { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public virtual CompanyItem CompanyItem { get; set; } = null!;

    public virtual ItemsUom ItemsUom { get; set; } = null!;
}
