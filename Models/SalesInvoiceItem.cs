namespace STTproject.Models
{
    public class SalesInvoiceItem
    {
        public int SalesInvoiceItemId { get; set; }
        public int SubdItemId { get; set; }
        public required SubdItem SubdItem { get; set; }
    }
}
