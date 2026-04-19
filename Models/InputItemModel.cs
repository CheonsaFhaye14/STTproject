namespace STTproject.Models
{
    public class InputItemModel
    {
        public int SubdItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int Price { get; set; }
        public int PricePerPiece { get; set; }
    }
}
