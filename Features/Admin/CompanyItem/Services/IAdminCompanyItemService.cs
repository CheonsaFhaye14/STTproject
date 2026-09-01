using STTproject.Features.Admin.CompanyItem.DTOs;

namespace STTproject.Features.Admin.CompanyItem.Services
{
    public interface IAdminCompanyItemService
    {
        Task<CompanyItemListDto?> CreateCompanyItemAsync(CompanyItemCreateDto dto, CancellationToken cancellationToken = default);
        Task<CompanyItemUpdateDto?> UpdateCompanyItemAsync(CompanyItemUpdateDto dto);
        Task ToggleCompanyItemStatusAsync(int id, bool isActive);
        Task<IEnumerable<CompanyItemListDto>> GetAllAsync();
        Task<(IEnumerable<CompanyItemListDto> Items, int TotalCount)> GetPagedAsync(
            int page, int pageSize, string? search, string? status,
            string? principal,
            string? sortColumn = "ItemCode", bool sortAscending = true);
        Task<CompanyItemListDto?> GetCompanyItemByIdAsync(int id);
        Task<string?> GetCompanyItemNameByIdAsync(int? companyItemId);
        Task<string?> GetUserNameByIdAsync(int? userId);
        Task<IReadOnlyList<string?>> GetAllPrincipalsAsync(CancellationToken cancellationToken = default);
        Task<bool> CompanyItemExistsAsync(string itemCode, string itemName, int? excludeId = null);
        Task<bool> ItemCodeExistsAsync(string itemCode, int? excludeId = null, CancellationToken cancellationToken = default);
        Task<CompanyItemListDto?> GetByItemCodeAsync(string itemCode, CancellationToken cancellationToken = default);
        Task<bool> UpdateStockPriceOnlyAsync(int companyItemId, decimal newStockPrice, int? updatedBy, CancellationToken cancellationToken = default);
        Task<IEnumerable<CompanyItemPriceHistoryDto>> GetPriceHistoryByCompanyItemIdAsync(int companyItemId);
        Task AddInitialPriceHistoryAsync(int companyItemId, decimal price, int userId, CancellationToken cancellationToken = default);
    }
}