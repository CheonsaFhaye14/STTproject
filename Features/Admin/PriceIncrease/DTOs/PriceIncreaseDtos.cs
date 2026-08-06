namespace STTproject.Features.Admin.PriceIncrease.DTOs
{
    public class PriceIncreaseListDto
    {
        public int? CompanyItemId { get; set; }
        public string? CompanyItemName { get; set; }
        public string? CompanyItemCode { get; set; }
        public string? Principal { get; set; }
        public decimal? PriceIncreasePercentage { get; set; }
        public string? EffectivityDate { get; set; }
        public string? Status { get; set; }
    }

    public class PriceIncreaseCreateDto
    {
        public int? CompanyItemId { get; set; }
        public string? Principal { get; set; }
        public string? CompanyItemCode { get; set; }
        public string? CompanyItemName { get; set; }
        public decimal? PriceIncreasePercentage { get; set; }
        public string? EffectivityDate { get; set; }
        public int? CreatedBy { get; set; }
    }

    public class PriceIncreaseUpdateDto
    {
        // include subditems connected to the price increase company item and show old and new price increase percentage and effectivity date
    }

}