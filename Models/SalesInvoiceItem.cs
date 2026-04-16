namespace STTproject.Models
{
    public class SalesInvoiceItem
    {
        public int SalesInvoiceItemId { get; set; }
        public int SalesInvoiceId { get; set; }
        public SalesInvoice SalesInvoice { get; set; }
        public int SubdItemId { get; set; }
        public SubdItem SubdItem { get; set; }
        public int Quantity { get; set; }
        public int Price { get; set; }

    }
}
