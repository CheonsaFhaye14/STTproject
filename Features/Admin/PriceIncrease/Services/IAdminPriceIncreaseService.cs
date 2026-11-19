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
          /// <summary>Edits any price change (pending or applied); rebuilds the price chain and re-syncs the item's price.</summary>
          Task<(bool success, string? error)> UpdatePriceChangeAsync(int companyItemPriceHistoryId, decimal priceIncreaseAmount, DateTime effectivityDate, int? updatedBy);
          /// <summary>Deletes any price change (pending or applied); rebuilds the price chain and re-syncs the item's price.</summary>
          Task<(bool success, string? error)> DeletePriceChangeAsync(int companyItemPriceHistoryId, int? updatedBy = null);
          /// <summary>Invoice lines priced from this change.</summary>
          Task<int> CountInvoiceLinesUsingAsync(int companyItemPriceHistoryId);
          /// <summary>"By company item" view: one row per item with price changes.</summary>
          Task<(IEnumerable<CompanyItemPriceSummaryDto> Items, int TotalCount)> GetCompanyItemSummaryPagedAsync(
              int page, int pageSize, string? search, string? status, string? principal,
              string? sortColumn = "CreatedDate", bool sortAscending = false);
          /// <summary>Every price change of one company item, newest first.</summary>
          Task<IReadOnlyList<PriceIncreaseTableListDto>> GetCompanyItemHistoryAsync(int companyItemId);
          Task<(bool success, string? error)> UpdatePendingIncreaseAsync(int companyItemPriceHistoryId, decimal priceIncreaseAmount, DateTime effectivityDate, int? updatedBy);
          Task<(bool success, string? error)> CancelPendingIncreaseAsync(int companyItemPriceHistoryId);
          Task<bool> HasPendingIncreaseAsync(int companyItemId, string? priceListType = null, int? excludeId = null);
          Task<bool> HasPendingIncreaseOnDateAsync(int companyItemId, string? priceListType, DateTime effectivityDate);
          Task<IReadOnlyList<CompanyItemUomPriceDto>> GetUomPricesByCompanyItemIdAsync(int companyItemId, string? priceListType = null);
          Task<IReadOnlyList<PriceIncreaseViewDto>> GetCascadedUomDetailsAsync(int companyItemPriceHistoryId);
          Task<IReadOnlyList<string>> GetSubDistributorsWithoutPriceListAsync(int companyItemId);
          Task<(IEnumerable<GroupedPriceIncreaseDto> Items, int TotalCount)> GetGroupedPagedAsync(int page, int pageSize, string? search, string? status, string? principal, string? sortColumn = "CreatedDate", bool sortAscending = true);
     }

}