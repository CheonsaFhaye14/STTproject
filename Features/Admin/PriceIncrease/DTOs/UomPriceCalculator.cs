namespace STTproject.Features.Admin.PriceIncrease.DTOs
{
    public static class UomPriceCalculator
    {
        public static decimal? Increase(decimal amount, int? uomConversion, int? stockConversion)
        {
            if (uomConversion is not > 0 || stockConversion is not > 0) return null;
            return Math.Round(amount * uomConversion.Value / stockConversion.Value, 2, MidpointRounding.AwayFromZero);
        }

        public static decimal? NewPrice(decimal? currentPrice, decimal amount, int? uomConversion, int? stockConversion)
        {
            var inc = Increase(amount, uomConversion, stockConversion);
            return currentPrice.HasValue && inc.HasValue ? currentPrice.Value + inc.Value : null;
        }
    }
}