namespace STTproject.Models
{
    public class InputItemModel
    {
        public string ItemCode { get; set; } = string.Empty;
        public int SubdItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int ItemsUomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
    }
}
