using STTproject.Features.Admin.PriceIncrease.DTOs;

// namespace STTproject.Features.Admin.CompanyItem.Services
// {
//     public interface IAdminCompanyItemService
//     {
//         Task<CompanyItemListDto?> CreateCompanyItemAsync(CompanyItemCreateDto dto);
//         Task<CompanyItemUpdateDto?> UpdateCompanyItemAsync(CompanyItemUpdateDto dto);
//         Task ToggleCompanyItemStatusAsync(int id, bool isActive);
//         Task<IEnumerable<CompanyItemListDto>> GetAllAsync();
//         Task<(IEnumerable<CompanyItemListDto> Items, int TotalCount)> GetPagedAsync(
//             int page, int pageSize, string? search, string? status,
//             string? principal,
//             string? sortColumn = "ItemCode", bool sortAscending = true);
//         Task<CompanyItemListDto?> GetCompanyItemByIdAsync(int id);
//         Task<string?> GetUserNameByIdAsync(int? userId);
//         Task<string?> GetCompanyItemNameByIdAsync(int? companyItemId);
//         Task<IReadOnlyList<string?>> GetAllPrincipalsAsync();
//         Task<bool> CompanyItemExistsAsync(string itemCode, string itemName, int? excludeId = null);
//         Task<bool> ItemCodeExistsAsync(string itemCode, int? excludeId = null);
//     }
// }