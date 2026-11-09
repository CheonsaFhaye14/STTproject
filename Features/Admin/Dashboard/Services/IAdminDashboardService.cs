using STTproject.Features.Admin.Dashboard.DTOs;

namespace STTproject.Features.Admin.Dashboard.Services
{
    public interface IAdminDashboardService
    {
        // New dashboard
        Task<SalesDashboardDto> GetSalesAsync();
        Task<PendingErrorsSummaryDto> GetPendingErrorsAsync();
        Task<List<PendingPriceIncreaseDto>> GetPendingPriceIncreasesAsync();
        Task<RecordsDto> GetRecordsAsync();

        // Old sections — remove together with CustPerSubd, TotalMonthlyAnnualPrice and SubdItemPerSubd
        Task<List<CustomerPerSubdDto>> GetCustomersPerSubdAsync();
        Task<int> GetTotalCustomersAsync();
        Task<List<TotalPricesPerSubdMonthlyAnnualDto>> GetTotalPricesPerSubdMonthlyAsync(int year, int month);
        Task<List<TotalPricesPerSubdMonthlyAnnualDto>> GetTotalPricesPerSubdAnnualAsync(int year);
        Task<List<SubdItemPerSubdDto>> GetTotalSubdItemsPerSubdAsync();
    }
}
