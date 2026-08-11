namespace STTproject.Features.Admin.PriceIncrease.DTOs
{
    public class PriceIncreaseTableListDto
    {
        public int CompanyItemPriceHistoryId { get; set; }
        public int? CompanyItemId { get; set; }
        public string? CompanyItemName { get; set; }
        public string? CompanyItemCode { get; set; }
        public decimal? StockPrice { get; set; }
        public decimal? PriceIncreaseAmount { get; set; }
        public DateTime? EffectivityDate { get; set; }
        public DateTime? AppliedDate { get; set; }
        public string? Status { get; set; }
        public int? CreatedBy { get; set; }
        public string? Principal { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class PriceIncreaseViewDto //Inside PriceIncreaseListDto
    {
        public int? SubdItemId { get; set; }
        public string? SubdItemCode { get; set; }
        public string? SubdItemName { get; set; }
        public string? UomName { get; set; }
        public decimal? OldPrice { get; set; }
        public decimal? NewPrice { get; set; }
        public decimal? PriceIncreaseAmount { get; set; }
        public DateTime? AppliedDate { get; set; }
        public int? CreatedBy { get; set; }
    }

    public class AddPriceIncreaseDto
    {
        public int? CompanyItemId { get; set; }
        public decimal? PriceIncreaseAmount { get; set; }
        public DateTime? EffectivityDate { get; set; }
        public int? CreatedBy { get; set; }
        public string? Principal { get; set; }
    }
    public class CompanyItemDropdownItem
    {
        public int CompanyItemId { get; set; }
        public string CompanyItemCode { get; set; } = string.Empty;
        public string CompanyItemName { get; set; } = string.Empty;
        public decimal? StockPrice { get; set; }
        public string Principal { get; set; } = string.Empty;
    }
    public class CompanyItemUomPriceDto
    {
        public int? SubdItemId { get; set; }
        public string? SubdItemCode { get; set; }
        public string? SubdItemName { get; set; }
        public int ItemsUomId { get; set; }
        public string? UomName { get; set; }
        public decimal? ConversionToBase { get; set; }
        public decimal? OldPrice { get; set; }
        public decimal? NewPrice { get; set; }

    }
}