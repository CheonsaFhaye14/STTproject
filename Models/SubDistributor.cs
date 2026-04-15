namespace STTproject.Models
{
    public class SubDistributor
    {
        public int SubDistributorId { get; set; }
        public String SubdCode { get; set; } = string.Empty;
        public String SubdName { get; set; } = string.Empty;
        public String CityMunicipality { get; set; } = string.Empty;
        public String? Province { get; set; }
    }
}
