namespace STTproject.Features.User.Customer.DTOs;

public class SubDistributorInfoDto
{
    public int SubDistributorId { get; set; }
    public string SubdCode { get; set; } = null!;
    public string SubdName { get; set; } = null!;
    public string CityMunicipality { get; set; } = null!;
    public string Province { get; set; } = null!;
}

public class CustomerBranchInfoDto
{
    public int CustomerBranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string City { get; set; } = null!;
    public string Province { get; set; } = null!;
    public int ZipCode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}

public class CustomerInfoDto
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string CustomerType { get; set; } = null!;
    public bool IsActive { get; set; }
    public CustomerBranchInfoDto? Branch { get; set; }
}

public class CustomerListResponseDto
{
    public SubDistributorInfoDto SubDistributor { get; set; } = null!;
    public List<CustomerInfoDto> Customers { get; set; } = new();
}
