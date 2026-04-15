namespace STTproject.Models
{
    public class RecentActivity
    {
        public int RecentActivityId { get; set; }
        public int SubdId { get; set; }
        public String SubdCode { get; set; } = string.Empty;
        public int InvoiceNumber { get; set; }
        public int Province { get; set; }
    }
}
