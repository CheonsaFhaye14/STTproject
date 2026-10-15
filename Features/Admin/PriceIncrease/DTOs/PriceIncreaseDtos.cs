namespace STTproject.Features.Admin.PriceIncrease.DTOs
{
    public class PriceIncreaseTableListDto
    {
        public int GroupKey => CompanyItemId ?? -CompanyItemPriceHistoryId;
        public int CompanyItemPriceHistoryId { get; set; }
        public int? CompanyItemId { get; set; }
        public string? CompanyItemName { get; set; }
        public string? CompanyItemCode { get; set; }
        public decimal? StockPrice { get; set; }
        public decimal? PriceIncreaseAmount { get; set; }
        public DateTime? EffectivityDate { get; set; }
        public DateTime? AppliedDate { get; set; }
        public string? PriceListType { get; set; }
        public string? Status { get; set; }
        public int? CreatedBy { get; set; }
        public string? Principal { get; set; }
        public DateTime CreatedDate { get; set; }
    }
    public class GroupedPriceIncreaseDto
    {
        public int GroupKey { get; set; }
        public int? CompanyItemId { get; set; }
        public string? CompanyItemCode { get; set; }
        public string? CompanyItemName { get; set; }
        public string? Principal { get; set; }
        public DateTime CreatedDate { get; set; }

        public List<PriceIncreaseTableListDto> Schedules { get; set; } = new();
    }
    public class PriceIncreaseViewDto
    {
        public int? SubdItemId { get; set; }
        public string? SubdItemCode { get; set; }
        public string? SubdItemName { get; set; }
        public string? UomName { get; set; }
        public decimal? OldPrice { get; set; }
        public decimal? NewPrice { get; set; }
        public string? PriceListType { get; set; }
        public decimal? PriceIncreaseAmount { get; set; }
        public DateTime? AppliedDate { get; set; }
        public int? CreatedBy { get; set; }
        public int? ConversionToBase { get; set; }
    }

    public class AddPriceIncreaseDto
    {
        public int? CompanyItemId { get; set; }
        public decimal? PriceIncreaseAmount { get; set; }
        public DateTime? EffectivityDate { get; set; }
        public int? CreatedBy { get; set; }
        public string? PriceListType { get; set; }
        public List<PriceIncreaseLineDto> Lines { get; set; } = new();
        public string? Principal { get; set; }
        public List<int> SkippedUomIds { get; set; } = new();

        public Dictionary<int, decimal> ManualUomPrices { get; set; } = new();
    }
    public class CompanyItemDropdownItem
    {
        public int CompanyItemId { get; set; }
        public string CompanyItemCode { get; set; } = string.Empty;
        public string CompanyItemName { get; set; } = string.Empty;
        public decimal? StockPrice { get; set; }
        public string Principal { get; set; } = string.Empty;
        public decimal? StockPriceRegional { get; set; }
        public decimal? StockPriceNational { get; set; }
        public string? StockUomName { get; set; }
        public PricingStructure Structure => PricingStructureResolver.Resolve(StockPrice, StockPriceRegional, StockPriceNational);
    }
    public class PriceIncreaseLineDto
    {
        public string? PriceListType { get; set; }  
        public DateTime? EffectivityDate { get; set; }  
        public decimal Amount { get; set; }
    }

    public class CompanyItemUomPriceDto
    {
        public int? SubdItemId { get; set; }
        public string? SubdItemCode { get; set; }
        public string? SubdItemName { get; set; }
        public int ItemsUomId { get; set; }
        public bool IsStockUom { get; set; }
        public int? StockConversionToBase { get; set; } 
        public decimal? CurrentPrice { get; set; }
        public decimal? Increase { get; set; }
        public bool IsManual { get; set; }       
        public string? UomName { get; set; }
        public int? ConversionToBase { get; set; }
        public decimal? OldPrice { get; set; }
        public string? PriceListType { get; set; }
        public decimal? NewPrice { get; set; }

    }
}