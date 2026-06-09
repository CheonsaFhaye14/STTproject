
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public interface IAdminCustomerService
    {
        Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto);
        Task<CustomerDetailDto?> UpdateCustomerAsync(int id, CustomerUpdateDto dto);
        Task ToggleCustomerStatusAsync(int id, bool isActive);
        Task<IEnumerable<CustomerListDto>> GetAllAsync();
        Task<(IEnumerable<CustomerListDto> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, string? search, string? status,
        string? customerType, int? subDistributorId);
        Task<IEnumerable<SubDistributorDto>> GetSubDistributorsAsync(string? query = null);
        Task<IEnumerable<string>> GetCustomerTypesAsync();
    }
}
