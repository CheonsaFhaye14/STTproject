namespace STTproject.Features.Admin.PriceIncrease.DTOs
{
    public enum PricingStructure
    {
        Incomplete,   
        Single,       
        Dual,         
        Invalid       
    }

    public static class PricingStructureResolver
    {
        public static PricingStructure Resolve(decimal? stock, decimal? regional, decimal? national)
        {
            if (regional.HasValue && national.HasValue) return PricingStructure.Dual;
            if (regional.HasValue || national.HasValue) return PricingStructure.Invalid;
            return stock.HasValue ? PricingStructure.Single : PricingStructure.Incomplete;
        }

        public static string? Describe(PricingStructure structure) => structure switch
        {
            PricingStructure.Invalid =>
                "This item has only one of its Regional/National prices. Set both on the Company Item before scheduling a price change.",
            PricingStructure.Incomplete =>
                "This item has no price configured. Set its price on the Company Item before scheduling a price change.",
            _ => null
        };
    }
}