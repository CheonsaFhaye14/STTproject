namespace STTproject.Models
{
    public class InputItemModel
    {
        public string ItemCode { get; set; } = string.Empty;
        public int SubdItemId { get; set; }
        public String ItemName { get; set; } = String.Empty; 
        public int UomName { get; set; }
        public int Quantity { get; set; }
        public int Price { get; set; }
    }
}
