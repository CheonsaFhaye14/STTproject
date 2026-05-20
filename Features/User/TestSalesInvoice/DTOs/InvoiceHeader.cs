namespace STTproject.Features.User.TestSalesInvoice.DTOs
{
    public class InvoiceHeader
    {
        public int SubdistributorId { get; set; }
        public string InvoiceCode { get; set; } = string.Empty;
        public DateOnly InvoiceDate { get; set; }
        public string SalesManName { get; set; } = string.Empty;
        public List<InvoiceCustomer> Customers { get; set; } = new List<InvoiceCustomer>();

    }
}