namespace STTproject.Features.Admin.Dashboard.DTOs
{
    public class CustomerPerSubdDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int TotalCount => ActiveCount + InactiveCount;
    }

    public class TotalPricesPerSubdMonthlyAnnualDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public decimal TotalPrice { get; set; }
    }

    public class SubdItemPerSubdDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = string.Empty;
        public string SubdName { get; set; } = string.Empty;
        public int SubdItemCount { get; set; }
        public List<string> Principals { get; set; } = new();
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int TotalCount => ActiveCount + InactiveCount;
    }
}