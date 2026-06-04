
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public interface IAdminCustomerService
    {
        Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto);
        Task<CustomerDetailDto?> UpdateCustomerAsync(int id, CustomerUpdateDto dto);
        Task ToggleCustomerStatusAsync(int id, bool isActive);
        Task<IEnumerable<CustomerListDto>> GetAllAsync();
        Task<IEnumerable<SubDistributorDto>> GetSubDistributorsAsync(string? query = null);
        Task<IEnumerable<string>> GetCustomerTypesAsync();
    }
}
