using STTproject.Features.Admin.Dashboard.DTOs;

namespace STTproject.Features.Admin.Dashboard.Services
{
    public interface IAdminDashboardService
    {
        Task<List<CustomerPerSubdDto>> GetCustomersPerSubdAsync();
        Task<int> GetTotalCustomersAsync();
        Task<List<TotalPricesPerSubdMonthlyAnnualDto>> GetTotalPricesPerSubdMonthlyAsync(int year, int month);
        Task<List<TotalPricesPerSubdMonthlyAnnualDto>> GetTotalPricesPerSubdAnnualAsync(int year);
    }
}