using System.Collections.Generic;
using System.Threading.Tasks;
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public interface IAdminCustomerService
    {
        Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto);
        Task<CustomerDetailDto?> UpdateCustomerAsync(int id, CustomerUpdateDto dto);
        Task ToggleCustomerStatusAsync(int id, bool isActive);
        Task<bool> CustomerCodeExistsAsync(string code, int subDistributorId, int? excludeCustomerId = null);
        Task<IEnumerable<CustomerListDto>> GetAllAsync();
        Task<IEnumerable<SubDistributorDto>> GetSubDistributorsAsync(string? query = null);
        Task<IEnumerable<ProvinceDto>> GetProvincesAsync();
        Task<IEnumerable<CityDto>> GetCitiesForProvinceAsync(string provinceCode);
        Task<IEnumerable<string>> GetCustomerTypesAsync();
    }
}
