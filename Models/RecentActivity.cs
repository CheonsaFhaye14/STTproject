using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace STTproject.Models
{
    public class RecentActivity
    {
        public int RecentActivityId { get; set; }
        public String BatchId { get; set; } = string.Empty;
        public int SubDistributorId { get; set; }
        public required SubDistributor SubDistributor { get; set; }
        public int SalesInvoiceId { get; set; }
        public required SalesInvoice SalesInvoice { get; set; }
    }
}
