using System.Globalization;
using STTproject.Models;
using STTproject.Features.Admin.ImportTemplate.Services;

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
    /// <summary>
    /// The ImportBatch this Check File attempt created. Carried to the commit and close steps so they update the same batch.
    /// Null when no batch exists (the request was rejected before one was made, or logging failed).
    /// </summary>
    public int? ImportBatchId { get; set; }
    public List<ImportedInvoiceRow> Rows { get; } = new();
    public int TotalRowsProcessed { get; set; }
    /// <summary>
    /// Field keys mapped across the sheets that were read, in sheet then column order, each listed once.
    /// For a multi-sheet file this is a combined list: nothing may infer a column layout or position from it.
    /// Per-sheet layouts live in <see cref="Sheets"/>.
    /// </summary>
    public List<string> OriginalHeaders { get; set; } = new();
    public List<SheetShortfall> SheetShortfalls { get; } = new();
    /// <summary>
    /// File-level things worth knowing that don't stop the import, such as an optional sheet that wasn't found.
    /// Kept for the results page to show; nothing displays them yet.
    /// </summary>
    public List<string> FileWarnings { get; } = new();
    public List<PreparedInvoice> PreparedInvoices { get; } = new();
    public int ImportedInvoiceCount { get; set; }
    public int ImportedRowCount { get; set; }
    public List<ImportSalesInvoiceIssue> Issues { get; } = new();
    public Dictionary<SourceRowKey, Dictionary<string, string?>> RawValuesByRow { get; } = new();
    public List<ErroredRowInfo> ErroredRows { get; } = new();

    /// <summary>
    /// Everything specific to one worksheet that was read, keyed by its position in the workbook
    /// (1-based, hidden sheets counted). The key is the sheet's identity, so nothing inside repeats it.
    /// </summary>
    public Dictionary<int, ImportSheetResult> Sheets { get; } = new();

    public ImportSheetResult GetOrAddSheet(int sheetIndex, string name)
    {
        if (!Sheets.TryGetValue(sheetIndex, out var sheet))
            Sheets[sheetIndex] = sheet = new ImportSheetResult { Name = name };
        return sheet;
    }

    /// <summary>Matched sheets that were not imported, and why. Filled by the partial-import step.</summary>
    public List<SkippedSheet> SkippedSheets { get; } = new();
    public bool HasSkippedSheets => SkippedSheets.Count > 0;

    /// <summary>The person has accepted that some sheets were skipped. Never true by default.</summary>
    public bool PartialImportAcknowledged { get; set; }

    public bool HasIssues => Issues.Count > 0;

    /// <summary>Sheet names are only shown when more than one sheet was read; a single-sheet file keeps reading "Row 5".</summary>
    public bool ShowSheetNames => Sheets.Count > 1;

    /// <summary>"Sales!5" for a multi-sheet file, "Row 5" for a single-sheet one, "File" for a file-level issue.</summary>
    public string FormatSource(SourceRowKey key)
    {
        if (key.IsFileLevel || key.RowNumber <= 0) return "File";
        return ShowSheetNames ? RowToken(key) : $"Row {key.RowNumber}";
    }

    /// <summary>
    /// "row 5", "rows 3, 4 &amp; 9", "rows 1, 2, 3, 4, 5 and 2 more"; with sheet names ("rows Sales!5 &amp; Free Goods!5")
    /// when more than one sheet was read. Ordered by sheet, then row.
    /// </summary>
    public string FormatRows(IEnumerable<SourceRowKey> keys)
    {
        var list = keys.Where(k => !k.IsFileLevel && k.RowNumber > 0)
            .Distinct().OrderBy(k => k.SheetIndex).ThenBy(k => k.RowNumber).Select(RowToken).ToList();

        if (list.Count == 0) return "the file";
        if (list.Count == 1) return $"row {list[0]}";
        if (list.Count <= 5) return $"rows {string.Join(", ", list.Take(list.Count - 1))} & {list[^1]}";
        return $"rows {string.Join(", ", list.Take(5))} and {list.Count - 5} more";
    }

    public string RowToken(SourceRowKey key) =>
        ShowSheetNames && Sheets.TryGetValue(key.SheetIndex, out var sheet)
            ? $"{sheet.Name}!{key.RowNumber}"
            : key.RowNumber.ToString(CultureInfo.InvariantCulture);

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

    public void AddError(
        SourceRowKey source,
        string invoiceNumber,
        string message,
        string? columnName = null,
        int? suggestionCount = null,
        string? code = null,
        string? value = null,
        string? context = null,
        IReadOnlyDictionary<string, string?>? details = null) =>
        Issues.Add(new ImportSalesInvoiceIssue(
            source.RowNumber, invoiceNumber, message, string.Empty, string.Empty,
            columnName, suggestionCount, SheetIndex: source.SheetIndex,
            Code: code, Value: value, Context: context, Details: details));

    public void AddIssue(ImportSalesInvoiceIssue issue) => Issues.Add(issue);
}

/// <summary>One worksheet that was read: its name, its raw header texts and its raw rows.</summary>
public sealed class ImportSheetResult
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Raw header texts, one per column, as they appear in the file.</summary>
    public List<string> RawHeaders { get; set; } = new();

    /// <summary>Raw cell text per row, keyed by row number on this sheet.</summary>
    public Dictionary<int, string[]> RawRows { get; } = new();
}

/// <summary>A sheet that matched the template but wasn't imported.</summary>
public sealed record SkippedSheet(int SheetIndex, string SheetName, string TemplateLabel, string Reason);

public sealed record ErroredRowInfo(
    int RowNumber,
    string InvoiceCode,
    string CustomerKey,
    string OrderType,
    string SalesMan,
    bool HeaderUnresolved,
    string? HeaderProblem,
    int SheetIndex = 0)
{
    public SourceRowKey Source => new(SheetIndex, RowNumber);
}

public sealed record ErrorReportEntry(int RowNumber, string ErrorType, string Message, int SheetIndex = 0)
{
    public SourceRowKey Source => new(SheetIndex, RowNumber);
}
public sealed record ImportedInvoiceRow(int RowNumber, string InvoiceCode, DateOnly InvoiceDate, string CustomerCode, string CustomerName, string OrderType,
    string SalesManName, string SkuCode, string ItemName, string UOM, int Quantity, string? Province, string? CityMunicipality, string? CustomerType, string? AddressLine,
    int ResolvedCustomerId, string ResolvedCustomerCode, string ResolvedCustomerName, int ResolvedSubdItemId, string ResolvedSubdItemCode, int ResolvedItemsUomId,
    bool IsFreeItem, string? AmbiguousItemWarning = null, string? UomFallbackWarning = null, List<int>? UomReviewCandidateIds = null,
    List<int>? CustomerCandidateIds = null, List<int>? SkuCandidateIds = null, int LineId = 0, int SheetIndex = 0)
{
    public SourceRowKey Source => new(SheetIndex, RowNumber);
}

public sealed record ImportSalesInvoiceIssue(
    int RowNumber,
    string InvoiceNumber,
    string Header,
    string Body,
    string Footer,
    string? ColumnName = null,
    int? SuggestionCount = null,
    string? GroupKey = null,
    bool IsHoldNote = false,
    int SheetIndex = 0,
    // Phase 7: how the row error is logged. Code is an ImportErrorTypes value; Value is the offending text
    // (SKU, customer, UOM...); Context narrows it where the same value means different things (the SKU for a UOM).
    string? Code = null,
    string? Value = null,
    string? Context = null,
    // Structured facts about the row for the details page (customer code and name, item code and name, uploaded UOM). Evidence only:
    // they never take part in the error's identity or fingerprint, which stay Code + field + Value + Context.
    IReadOnlyDictionary<string, string?>? Details = null)
{
    public string Message => Header;
    public SourceRowKey Source => new(SheetIndex, RowNumber);
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