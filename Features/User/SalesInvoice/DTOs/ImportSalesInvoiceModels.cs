using STTproject.Models;

namespace STTproject.Features.User.SalesInvoice.DTOs;

public sealed class PreparedInvoice
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public InputInvoiceModel Invoice { get; set; } = null!;
    public List<InputItemModel> Items { get; set; } = new();
    public List<ImportSalesInvoiceIssue> Issues { get; set; } = new();
    public bool Selected { get; set; }
    public bool IsSaved { get; set; }
    public string? SaveErrorMessage { get; set; }
}

public sealed class ImportSalesInvoiceResult
{
    public List<PreparedInvoice> PreparedInvoices { get; } = new();
    public int ImportedInvoiceCount { get; set; }
    public int ImportedRowCount { get; set; }
    public List<ImportSalesInvoiceIssue> Issues { get; } = new();

    public bool HasIssues => Issues.Count > 0;

    public void AddError(
        int rowNumber,
        string invoiceNumber,
        string header,
        string body,
        string footer,
        string? columnName = null)
    {
        Issues.Add(new ImportSalesInvoiceIssue(
            rowNumber,
            invoiceNumber,
            header,
            body,
            footer,
            columnName));
    }
    public void AddError(
        int rowNumber,
        string invoiceNumber,
        string message,
        string? columnName = null)
    {
        AddError(rowNumber, invoiceNumber, message, string.Empty, string.Empty, columnName);
    }

}

public sealed record ImportedInvoiceRow(
    int RowNumber,
    string InvoiceCode,
    DateOnly InvoiceDate,
    string CustomerCode,
    string CustomerName,
    string OrderType,
    string SalesManName,
    string SkuCode,
    string ItemName,
    string UOM,
    int Quantity,
    string? Province,
    string? CityMunicipality,
    string? CustomerType,
    string? AddressLine,
    int ResolvedCustomerId,
    string ResolvedCustomerCode,
    string ResolvedCustomerName,
    int ResolvedSubdItemId,
    string ResolvedSubdItemCode,
    int ResolvedItemsUomId);

public sealed record ImportSalesInvoiceIssue(
    int RowNumber,
    string InvoiceNumber,
    string Header,
    string Body,
    string Footer,
    string? ColumnName = null)
{
    public string Message => Header;
}