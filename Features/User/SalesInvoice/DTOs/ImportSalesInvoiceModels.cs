using System.Globalization;
using STTproject.Models;

namespace STTproject.Features.User.SalesInvoice.DTOs;

public sealed class PreparedInvoice
{
    public string GroupKey { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string SubDistributor { get; set; } = string.Empty;
    public InputInvoiceModel Invoice { get; set; } = null!;
    public List<InputItemModel> Items { get; set; } = new();
    public List<ImportSalesInvoiceIssue> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<(InputItemModel Item, bool IsFreeItem)> RawItems { get; set; } = new();
    public List<PendingItemChoice> ItemChoices { get; set; } = new();
    public bool Selected { get; set; }
    public bool IsSaved { get; set; }
    public string? SaveErrorMessage { get; set; }
    public PendingCustomerChoice? CustomerChoice { get; set; }
    public string OrderTypeKey { get; set; } = string.Empty;
    public string SalesManKey { get; set; } = string.Empty;
    public string BuildGroupKey(int customerId) => string.Join("|",
        InvoiceNumber.Trim().ToUpperInvariant(),
        customerId.ToString(CultureInfo.InvariantCulture),
        OrderTypeKey, SalesManKey);
    public bool HasUnresolvedDecisions =>
        ItemChoices.Any(c => !c.IsConfirmed) || CustomerChoice is { IsConfirmed: false };
    public bool HasErrors => Issues.Count > 0;
    public bool IsReadyToCommit => !IsSaved && !HasErrors && !HasUnresolvedDecisions;
    public bool TrySetSelected(bool value)
    {
        if (value && !IsReadyToCommit) { Selected = false; return false; }
        Selected = value;
        return true;
    }
    public void DropSelectionIfNotReady()
    {
        if (!IsReadyToCommit) Selected = false;
    }
}

public sealed class ImportSalesInvoiceResult
{
    public List<ImportedInvoiceRow> Rows { get; } = new();
    public int TotalRowsProcessed { get; set; }
    public List<string> OriginalHeaders { get; set; } = new();
    public List<PreparedInvoice> PreparedInvoices { get; } = new();
    public int ImportedInvoiceCount { get; set; }
    public int ImportedRowCount { get; set; }
    public List<ImportSalesInvoiceIssue> Issues { get; } = new();
    public Dictionary<int, Dictionary<string, string?>> RawValuesByRow { get; } = new();
    public List<ErroredRowInfo> ErroredRows { get; } = new();
    public List<string> RawSheetHeaders { get; set; } = new();
    public Dictionary<int, string[]> RawSheetRowsByRow { get; } = new();
    public bool HasIssues => Issues.Count > 0;

    public void AddError(
        int rowNumber,
        string invoiceNumber,
        string header,
        string body,
        string footer,
        string? columnName = null,
        int? suggestionCount = null)
    {
        Issues.Add(new ImportSalesInvoiceIssue(
            rowNumber,
            invoiceNumber,
            header,
            body,
            footer,
            columnName,
            suggestionCount));
    }
    public void AddError(
        int rowNumber,
        string invoiceNumber,
        string message,
        string? columnName = null, int? suggestionCount = null)
    {
        AddError(rowNumber, invoiceNumber, message, string.Empty, string.Empty, columnName, suggestionCount);
    }

    public void AddIssue(ImportSalesInvoiceIssue issue) => Issues.Add(issue);
}

public sealed record ErroredRowInfo(
    int RowNumber,
    string InvoiceCode,
    string CustomerKey,
    string OrderType,
    string SalesMan,
    bool HeaderUnresolved,
    string? HeaderProblem);

public sealed record ErrorReportEntry(int RowNumber, string ErrorType, string Message);
public sealed record ImportedInvoiceRow(int RowNumber, string InvoiceCode, DateOnly InvoiceDate, string CustomerCode, string CustomerName, string OrderType,
    string SalesManName, string SkuCode, string ItemName, string UOM, int Quantity, string? Province, string? CityMunicipality, string? CustomerType, string? AddressLine,
    int ResolvedCustomerId, string ResolvedCustomerCode, string ResolvedCustomerName, int ResolvedSubdItemId, string ResolvedSubdItemCode, int ResolvedItemsUomId,
    bool IsFreeItem, string? AmbiguousItemWarning = null, string? UomFallbackWarning = null, List<int>? UomReviewCandidateIds = null,
    List<int>? CustomerCandidateIds = null, List<int>? SkuCandidateIds = null, int LineId = 0);

public sealed record ImportSalesInvoiceIssue(
    int RowNumber,
    string InvoiceNumber,
    string Header,
    string Body,
    string Footer,
    string? ColumnName = null,
    int? SuggestionCount = null,
    string? GroupKey = null,
    bool IsHoldNote = false)
{
    public string Message => Header;
}

public sealed record ItemCandidateOption(
    int SubdItemId,
    string SubdItemCode,
    string ItemName,
    int ItemsUomId,
    string UomName,
    decimal UnitPrice);

public sealed class PendingItemChoice
{
    public ChoiceKind Kind { get; init; } = ChoiceKind.Sku;
    public int LineItemId { get; init; }
    public string SkuCode { get; init; } = string.Empty;
    public List<ItemCandidateOption> Candidates { get; init; } = new();
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int SelectedSubdItemId { get; private set; }
    public int SelectedItemsUomId { get; private set; }
    public bool IsConfirmed { get; private set; }

    public ItemCandidateOption? Selected =>
        Candidates.FirstOrDefault(c => c.ItemsUomId == SelectedItemsUomId);

    public void Select(ItemCandidateOption candidate)
    {
        if (candidate.ItemsUomId == SelectedItemsUomId) return;
        SelectedSubdItemId = candidate.SubdItemId;
        SelectedItemsUomId = candidate.ItemsUomId;
        IsConfirmed = false;
    }

    public void SetDefault(ItemCandidateOption candidate)
    {
        SelectedSubdItemId = candidate.SubdItemId;
        SelectedItemsUomId = candidate.ItemsUomId;
        IsConfirmed = false;
    }

    public void Confirm() => IsConfirmed = true;
}

public enum ChoiceKind { Sku, Uom, Customer }

public sealed record CustomerCandidateOption(
    int CustomerId, string CustomerCode, string CustomerName, string? Address, string? CustomerType = null);

public sealed class PendingCustomerChoice
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public List<CustomerCandidateOption> Candidates { get; init; } = new();
    public int SelectedCustomerId { get; private set; }
    public bool IsConfirmed { get; private set; }
    public bool IsUserSelected { get; private set; }
    public string? ValidationMessage { get; set; }
    public bool CanConfirm => string.IsNullOrEmpty(ValidationMessage);

    public CustomerCandidateOption? Selected =>
        Candidates.FirstOrDefault(c => c.CustomerId == SelectedCustomerId);

    public void SetDefault(CustomerCandidateOption candidate)
    {
        SelectedCustomerId = candidate.CustomerId;
        IsConfirmed = false;
    }

    public void Select(CustomerCandidateOption candidate)
    {
        IsUserSelected = true;
        if (candidate.CustomerId == SelectedCustomerId) return;
        SelectedCustomerId = candidate.CustomerId;
        IsConfirmed = false;
    }

    public void Confirm() { if (CanConfirm) IsConfirmed = true; }
}
    