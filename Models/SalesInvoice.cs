namespace STTproject.Models
{
    public class SalesInvoice
    {
        public int SalesInvoiceId { get; set; }
        public String SalesInvoiceCode { get; set; } = string.Empty;
        public DateOnly SalesInvoiceDate { get; set; }
        public String OrderType { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public required Customer Customer { get; set; }
        public int SubDistributorId { get; set; }
        public required SubDistributor SubDistributor { get; set; }
        public List<SalesInvoiceItem> Items { get; set; } = new();
    }
   
}
