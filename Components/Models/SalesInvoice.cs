namespace STTproject.Components.Models
{
    public class SalesInvoice
    {
        public int SalesInvoiceId { get; set; }
        public String SalesInvoiceCode { get; set; } = string.Empty;
        public DateOnly SalesInvoiceDate { get; set; }
        public String OrderType { get; set; } = string.Empty;
        public String CustomerType { get; set; } = string.Empty;
        public String CustomerCode { get; set; } = string.Empty;
        public String CustomerName { get; set; } = string.Empty;
        public String CustomerAddress { get; set; } = string.Empty;
        public List<SalesInvoiceItems> Items { get; set; } = new();
    }
   
}
