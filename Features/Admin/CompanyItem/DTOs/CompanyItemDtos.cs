namespace STTproject.Features.Admin.CompanyItem.DTOs
{
    public class CompanyItemListDto
    {
        public int CompanyItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? Category { get; set; }
        public string? Principal { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }

    public class CompanyItemCreateDto
    {
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? Category { get; set; }
        public string? Principal { get; set; }
        public bool IsActive { get; set; }
        public int? CreatedBy { get; set; }
    }

    public class CompanyItemUpdateDto
    {
        public int CompanyItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? Category { get; set; }
        public string? Principal { get; set; }
        public bool IsActive { get; set; }
        public int? UpdatedBy { get; set; }
    }

    public class CompanyItemPriceHistoryDto
    {
        public int CompanyItemPriceHistoryId { get; set; }
        public int CompanyItemId { get; set; }
        public decimal NewPrice { get; set; }
        public DateTime EffectivityDate { get; set; }
        public DateTime CreatedDate { get; set; }      
        public int? CreatedBy { get; set; }
    }
}