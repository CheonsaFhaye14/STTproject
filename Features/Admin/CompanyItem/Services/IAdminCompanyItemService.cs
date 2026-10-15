using STTproject.Features.Admin.CompanyItem.DTOs;

namespace STTproject.Features.Admin.CompanyItem.Services
{
    public interface IAdminCompanyItemService
    {
        Task<CompanyItemListDto?> CreateCompanyItemAsync(CompanyItemCreateDto dto, CancellationToken cancellationToken = default);
        Task<CompanyItemUpdateDto?> UpdateCompanyItemAsync(CompanyItemUpdateDto dto);
        Task ToggleCompanyItemStatusAsync(int id, bool isActive, int? updatedBy);
        Task<IEnumerable<CompanyItemListDto>> GetAllAsync();
        Task<(IEnumerable<CompanyItemListDto> Items, int TotalCount)> GetPagedAsync(
            int page, int pageSize, string? search, string? status,
            string? principal, string? ppg, string? category,
            string? sortColumn = "ItemCode", bool sortAscending = true);
        Task<CompanyItemListDto?> GetCompanyItemByIdAsync(int id);
        Task<string?> GetCompanyItemNameByIdAsync(int? companyItemId);
        Task<string?> GetUserNameByIdAsync(int? userId);
        Task<IReadOnlyList<string?>> GetAllPPGsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string?>> GetAllCategoriesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string?>> GetAllPrincipalsAsync(CancellationToken cancellationToken = default);
        Task<bool> CompanyItemExistsAsync(string itemCode, string itemName, int? excludeId = null);
        Task<bool> ItemCodeExistsAsync(string itemCode, int? excludeId = null, CancellationToken cancellationToken = default);
        Task<CompanyItemListDto?> GetByItemCodeAsync(string itemCode, CancellationToken cancellationToken = default);
        Task<IEnumerable<CompanyItemPriceHistoryDto>> GetPriceHistoryByCompanyItemIdAsync(int companyItemId);
        Task<bool> UpdateStockPricesAsync(
            int companyItemId, decimal? stock, decimal? regional, decimal? national,
            int? updatedBy, CancellationToken cancellationToken = default);

        Task AddInitialPriceHistoryAsync(
            int companyItemId, decimal price, int userId,
            string? priceListType = null, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ExistingPriceHistoryRow>> GetPriceHistoryRowsAsync(int companyItemId, CancellationToken cancellationToken = default);
        Task<HistoricalPriceApplyResult> ApplyHistoricalPricesAsync(int companyItemId, IReadOnlyList<HistoricalPriceRow> entries, int userId, CancellationToken cancellationToken = default);
        Task<(HistoricalPriceApplyResult Result, int? CompanyItemId)> CreateItemWithHistoricalPricesAsync(
            CompanyItemCreateDto dto, IReadOnlyList<HistoricalPriceRow> entries, int userId, CancellationToken cancellationToken = default);
    }
}