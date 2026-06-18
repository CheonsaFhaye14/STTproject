using STTproject.Features.Admin.SalesInvoice.DTOs;

namespace STTproject.Features.Admin.SalesInvoice.Services;

public interface IAdminSalesInvoiceService
{
    // View
    Task<(List<SalesInvoiceListRow> Items, int Total)> GetPagedAsync(
    int page, int pageSize,
    string? search,
    string? orderType,
    int? customerId,
    int? subDistributorId,
    string sortColumn,
    bool sortAscending,
    CancellationToken cancellationToken = default);

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

    Task<List<SalesInvoiceSubdItemDropdownItem>> GetSubdItemsForDropdownAsync(
        int subDistributorId,
        CancellationToken cancellationToken = default);
    Task<List<SalesInvoiceSubDistributorDropdownItem>> GetSubDistributorsAsync(CancellationToken cancellationToken = default);

    // CRUD
    Task<SalesInvoiceResult> CreateSalesInvoiceAsync(
        CreateSalesInvoiceDto dto,
        int createdByUserId,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceResult> UpdateSalesInvoiceAsync(
        UpdateSalesInvoiceDto dto,
        int updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<DeleteSalesInvoiceResult> DeleteSalesInvoiceAsync(
        int salesInvoiceId,
        CancellationToken cancellationToken = default);

    // User
    Task<string?> GetUserNameByIdAsync(int? userId);

    // In IAdminSalesInvoiceService:

}