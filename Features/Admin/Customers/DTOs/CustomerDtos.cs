using System;
namespace STTproject.Features.Admin.Customers.DTOs
{
    public class CustomerListDto
    {
        public int CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? SubDistributorName { get; set; }
    }

    public class CustomerDetailDto
    {
        public int CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
    }

    public class CustomerCreateDto
    {
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
    }

    public class CustomerUpdateDto : CustomerCreateDto
    {
        public int CustomerId { get; set; }
    }

    public class SubDistributorDto
    {
        public int SubDistributorId { get; set; }
        public string? SubDistributorName { get; set; }
    }

    public class ProvinceDto
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
    }

    public class CityDto
    {
        public string? Name { get; set; }

    }
}
