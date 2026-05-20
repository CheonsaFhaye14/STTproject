namespace STTproject.Features.User.TestSalesInvoice.DTOs
{
    public class InvoiceCustomer
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int CustomerBranchId { get; set; }
        public string Branch { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public List<CustomerItem> Items { get; set; } = new List<CustomerItem>();
    }
}