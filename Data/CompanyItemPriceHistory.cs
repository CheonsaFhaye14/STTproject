using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class CompanyItemPriceHistory
{
    public int CompanyItemPriceHistoryId { get; set; }

    public int CompanyItemId { get; set; }

    public decimal OldPrice { get; set; }

    public decimal NewPrice { get; set; }

    public decimal PriceIncreaseAmount { get; set; }

    public DateTime EffectivityDate { get; set; }

    public DateTime? AppliedDate { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public virtual CompanyItem CompanyItem { get; set; } = null!;

    public virtual ICollection<ItemsUomPriceHistory> ItemsUomPriceHistories { get; set; } = new List<ItemsUomPriceHistory>();
}
