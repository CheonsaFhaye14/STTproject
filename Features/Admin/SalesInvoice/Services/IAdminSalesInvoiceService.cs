using STTproject.Features.Admin.SalesInvoice.DTOs;

namespace STTproject.Features.Admin.SalesInvoice.Services;

public interface IAdminSalesInvoiceService
{
    // View
    Task<(List<SalesInvoiceListRow> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        string? search,
        string? orderType,
        int? subDistributorId,
        string? principal,
        int? month,     
        int? year,       
        string sortColumn,
        bool sortAscending,
        CancellationToken cancellationToken = default);
    Task<List<(int Year, int Month)>> GetAvailableInvoiceMonthsAsync(CancellationToken cancellationToken = default);
    Task<List<SalesInvoiceListRow>> GetSalesInvoicesAsync(
        int subDistributorId,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailDto?> GetSalesInvoiceDetailAsync(
        int salesInvoiceId,
        CancellationToken cancellationToken = default);

    Task<bool> InvoiceCodeExistsAsync(
        string code,
        int subDistributorId,
        int? excludeId = null,
        CancellationToken cancellationToken = default);

    // Dropdowns
    Task<List<SalesInvoiceCustomerDropdownItem>> GetCustomersForDropdownAsync(
        int subDistributorId,
        CancellationToken cancellationToken = default);
    Task<List<string>> GetPrincipalsForDropdownAsync(CancellationToken cancellationToken = default);
    Task<List<SalesInvoiceSubdItemDropdownItem>> GetSubdItemsForDropdownAsync(
        int subDistributorId, 
        DateOnly salesInvoiceDate,
        CancellationToken cancellationToken = default);
    Task<List<SalesInvoiceSubDistributorDropdownItem>> GetSubDistributorsAsync(CancellationToken cancellationToken = default);
    Task<List<SalesInvoiceItemDropdownItem>> GetItemsForDropdownAsync(CancellationToken cancellationToken = default);

    // CRUD
    Task<SalesInvoiceResult> CreateSalesInvoiceAsync(
        CreateSalesInvoiceDto dto,
        int createdByUserId,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceResult> UpdateSalesInvoiceAsync(
        UpdateSalesInvoiceDto dto,
        int updatedByUserId,
        CancellationToken cancellationToken = default);

    // User
    Task<string?> GetUserNameByIdAsync(int? userId);

    Task<DeleteSalesInvoiceResult> DeleteSalesInvoiceAsync(
        int salesInvoiceId, int deletedByUserId, CancellationToken ct = default);

    Task<int> CountForBatchDeleteAsync(
        int subDistributorId, int year, int month, CancellationToken cancellationToken = default);

    Task<BatchDeleteResult> DeleteBatchAsync(
        int subDistributorId, int year, int month, int deletedByUserId, CancellationToken ct = default);

    Task<List<(int Year, int Month)>> GetDeletedInvoiceMonthsAsync(CancellationToken ct = default);

    Task<(List<DeletedBatchRow> Items, int Total)> GetDeletedBatchesPagedAsync(
        int page, int pageSize, string? type, string? status, int? subDistributorId,
        int? month, int? year,
        string? search, string? sortColumn, bool sortAscending,
        CancellationToken ct = default);
    Task<(List<DeletedInvoiceDetailRow> Items, int Total)> GetDeletedBatchInvoicesPagedAsync(
        int deletionBatchId, int page, int pageSize, string? search, CancellationToken ct = default);

    Task<RestoreBatchResult> RestoreBatchAsync(
        int deletionBatchId, int restoredByUserId, CancellationToken ct = default);
}