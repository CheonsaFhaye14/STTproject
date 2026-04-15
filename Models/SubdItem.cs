namespace STTproject.Models
{
    public class SubdItem
    {
        public int SubdItemId { get; set; }
        public String SubdItemCode { get; set; } = string.Empty;
        public String ItemName { get; set; } = string.Empty;
        public String UnitContent { get; set; } = string.Empty;
        public String UOM { get; set; } = string.Empty;
        public int QuantityPerPiece { get; set; }
        public int Price { get; set; }
        public int SubDistributorId { get; set; }
        public required SubDistributor SubDistributor { get; set; }
    }
}
