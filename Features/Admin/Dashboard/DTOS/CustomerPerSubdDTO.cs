namespace STTproject.Features.Admin.Dashboard.DTOs
{
    public class CustomerPerSubdDto
    {
        public int SubDistributorId { get; set; }
        public string SubdName { get; set; } = string.Empty;
        public string SubdCode { get; set; } = string.Empty;
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int TotalCount => ActiveCount + InactiveCount;
    }
}