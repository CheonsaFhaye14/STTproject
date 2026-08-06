namespace STTproject.Features.Admin.Subdistributor.DTOs
{
    public class SubDistributorCreateDto
    {
        public string SubdCode { get; set; } = null!;
        public string SubdName { get; set; } = null!;
        public string CityMunicipality { get; set; } = null!;
        public string Province { get; set; } = null!;
        public string CompanySubdCode { get; set; } = null!;
        public int? EncoderId { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedBy { get; set; }
    }

    public class SubDistributorUpdateDto
    {
        public int SubDistributorId { get; set; }
        public string? SubdCode { get; set; }
        public string? SubdName { get; set; }
        public string? CityMunicipality { get; set; }
        public string? Province { get; set; }
        public string? CompanySubdCode { get; set; }
        public int? EncoderId { get; set; }
        public bool IsActive { get; set; }
        public int? UpdatedBy { get; set; }
    }

    public class SubDistributorListDto
    {
        public int SubDistributorId { get; set; }
        public string SubdCode { get; set; } = null!;
        public string SubdName { get; set; } = null!;
        public string CityMunicipality { get; set; } = null!;
        public string Province { get; set; } = null!;
        public string CompanySubdCode { get; set; } = null!;
        public int? EncoderId { get; set; }
        public string? EncoderName { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}