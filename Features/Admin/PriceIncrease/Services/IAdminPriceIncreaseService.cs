using STTproject.Features.Admin.PriceIncrease.DTOs;

 namespace STTproject.Features.Admin.PriceIncrease.Services
{
     public interface IAdminPriceIncreaseService
     {
        Task<(IEnumerable<PriceIncreaseTableListDto> Items, int TotalCount)> GetPagedAsync(
             int page, int pageSize, string? search, string? status,
             string? principal,
             string? sortColumn = "EffectivityDate", bool sortAscending = true);

        Task<IReadOnlyList<string?>> GetAllPrincipalsAsync();
        Task<IEnumerable<PriceIncreaseTableListDto>> GetAllAsync();
        Task<string?> GetUserNameByIdAsync(int? userId);
        Task<PriceIncreaseTableListDto?> GetPriceIncreaseByIdAsync(int id);
        Task<IReadOnlyList<CompanyItemDropdownItem>> GetCompanyItemsForDropdownAsync();
        Task<(bool success, string? error)> ScheduleIncreaseAsync(AddPriceIncreaseDto dto);
        Task<(bool success, string? error)> UpdatePendingIncreaseAsync(int companyItemPriceHistoryId, decimal priceIncreaseAmount, DateTime effectivityDate, int? updatedBy);
        Task<bool> HasPendingIncreaseAsync(int companyItemId, int? excludeId = null);    
        Task<IReadOnlyList<CompanyItemUomPriceDto>> GetUomPricesByCompanyItemIdAsync(int companyItemId);
     }
}


 