using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public interface IAdminCustomerService
    {
        Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto);
        Task<CustomerDetailDto?> UpdateCustomerAsync(int id, CustomerUpdateDto dto);
        Task<CustomerDetailDto?> UpdateCustomerAsync(CustomerUpdateDto dto);
        Task ToggleCustomerStatusAsync(int id, bool isActive);
        Task<IEnumerable<CustomerListDto>> GetAllAsync();
        Task<(IEnumerable<CustomerListDto> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, string? status, string? customerType, int? subDistributorId, string? sortColumn = "CustomerName", bool sortAscending = true);
        Task<IEnumerable<SubDistributorDto>> GetSubDistributorsAsync(string? query = null);
        Task<IEnumerable<string>> GetCustomerTypesAsync();
        Task<CustomerDetailDto?> GetCustomerByIdAsync(int id);
        Task<string?> GetUserNameByIdAsync(int? userId);
        Task<bool> CustomerCodeExistsAsync(string customerCode, int subDistributorId, IEnumerable<int>? excludeIds = null);        
        Task<IEnumerable<SubdMappingDto>> GetCustomerGroupMappingsAsync(int customerId);
        Task<(bool success, string? error)> UpdateCustomerGroupAsync(CustomerGroupUpdateDto dto);
        Task<ImportMatchResult> CheckImportDuplicateAsync(string customerCode, string customerName, int subDistributorId, string? subdCustCode, string? subdCustName);
        Task<CustomerDetailDto?> FillBlankSubdMappingAsync(int customerId, string? subdCustCode, string? subdCustName, int? updatedBy);
    }
}