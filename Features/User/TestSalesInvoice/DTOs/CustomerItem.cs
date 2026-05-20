namespace STTproject.Features.User.TestSalesInvoice.DTOs
{
    public class CustomerItem
    {
        public int SubdItemId { get; set; }
        public string Principal { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int ItemsUomId { get; set; }
        public string UomName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
        public string OrderType { get; set; } = string.Empty;
    }
}