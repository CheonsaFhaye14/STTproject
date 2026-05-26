namespace STTproject.Features.User.Customer.DTOs;

public class SubDistributorInfoDto
{
    public int SubDistributorId { get; set; }
    public string SubdCode { get; set; } = null!;
    public string SubdName { get; set; } = null!;
    public string CityMunicipality { get; set; } = null!;
    public string Province { get; set; } = null!;
}


public class CustomerInfoDto
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string CustomerType { get; set; } = null!;
    public bool IsActive { get; set; }
    // Address fields moved to the customer DTO (sourced from Customer table)
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public int? ZipCode { get; set; }
}

public class CustomerListResponseDto
{
    public SubDistributorInfoDto SubDistributor { get; set; } = null!;
    public List<CustomerInfoDto> Customers { get; set; } = new();
}
