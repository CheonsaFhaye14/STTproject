namespace STTproject.Features.Admin.PriceIncrease.DTOs
{
    /// <summary>UOM prices follow the company price: UOM price = stock price × conversion (stock UOM = 1).</summary>
    public static class UomPriceCalculator
    {
        public static decimal? Increase(decimal amount, decimal? uomConversion) =>
            uomConversion is decimal c && c > 0 ? Math.Round(amount * c, 2, MidpointRounding.AwayFromZero) : null;

        public static decimal? Increase(decimal amount, decimal? uomConversion, decimal? stockConversion)
        {
            if (uomConversion is not > 0 || stockConversion is not > 0) return null;
            return Math.Round(amount * uomConversion.Value / stockConversion.Value, 2, MidpointRounding.AwayFromZero);
        }

        public static decimal? NewPrice(decimal? currentPrice, decimal amount, decimal? uomConversion, decimal? stockConversion)
        {
            var inc = Increase(amount, uomConversion, stockConversion);
            return currentPrice.HasValue && inc.HasValue ? currentPrice.Value + inc.Value : null;
        }
    }
}