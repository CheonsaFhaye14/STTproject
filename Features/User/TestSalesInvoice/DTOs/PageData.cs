namespace STTproject.Features.User.TestSalesInvoice.DTOs
{
    public class PageData
    {
        public List<string> Customers { get; set; } = new List<string>();
        public List<string> CustomerBranches { get; set; } = new List<string>();
        public List<string> SubdItems { get; set; } = new List<string>();
        public List<string> ItemUoms { get; set; } = new List<string>();
    }
}