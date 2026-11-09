using STTproject.Data;
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public interface IAdminCustomerService
    {
        // Customer + its subd mappings, saved together.
        Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto);
        Task<CustomerDetailDto?> UpdateCustomerAsync(int id, CustomerUpdateDto dto);
        Task<CustomerDetailDto?> UpdateCustomerAsync(CustomerUpdateDto dto);
        Task ToggleCustomerStatusAsync(int id, bool isActive, int? updatedBy);

        Task<IEnumerable<CustomerListDto>> GetAllAsync();
        Task<(IEnumerable<CustomerListDto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, string? status, string? customerType, int? subDistributorId, string? sortColumn = "CustomerName", bool sortAscending = true);
        Task<IEnumerable<SubDistributorDto>> GetSubDistributorsAsync(string? query = null);
        Task<IEnumerable<string>> GetCustomerTypesAsync();
        Task<CustomerDetailDto?> GetCustomerByIdAsync(int id);
        Task<string?> GetUserNameByIdAsync(int? userId);

        // Validation
        Task<bool> CustomerCodeExistsAsync(string customerCode, int subDistributorId, int? excludeCustomerId = null);
        Task<List<SubdMappingConflictDto>> FindMappingConflictsAsync(int subDistributorId, IEnumerable<SubdMappingDto> mappings, int? excludeCustomerId = null);

        // Used by the customer import.
        Task<ImportMatchResult> CheckImportDuplicateAsync(string customerCode, int subDistributorId, string? subdCustCode, string? subdCustName);
        Task<CustomerDetailDto?> AddSubdMappingAsync(int customerId, string? subdCustCode, string? subdCustName, int? createdBy);
        Task<List<Customer>> GetCustomersByKeyAsync(int subdistributorId, string customerCode);
        Task<int> UpdateGroupDetailsAsync(int subdistributorId, string customerCode, CustomerDetailsUpdate details, int userId);
    }
}