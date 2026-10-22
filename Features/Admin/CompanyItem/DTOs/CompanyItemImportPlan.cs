namespace STTproject.Features.Admin.CompanyItem.DTOs;

public enum CompanyItemOperationKind
{
    CreateItem,
    ApplyCurrentPrice,
    AddPriceHistory,
    SchedulePrice,
    BackfillStockUom,
    SkipDuplicate
}

public sealed record CompanyItemPlannedOperation(
    CompanyItemOperationKind Kind,
    string Description,
    int? RowNumber = null);

public sealed class PreparedCompanyItemGroup
{
    public string ItemCode { get; set; } = string.Empty;
    public bool IsNewItem { get; set; }
    public int? ExistingCompanyItemId { get; set; }

    public List<CompanyItemImportRowResult> Rows { get; } = new();
    public List<CompanyItemImportIssue> Issues { get; } = new();

    public List<CompanyItemPlannedOperation> Operations { get; } = new();

    public List<HistoricalPriceRow> Entries { get; } = new();

    public string? BackfillStockUomName { get; set; }

    public bool Selected { get; set; }
    public bool IsSaved { get; set; }

    public bool HasIssues => Issues.Count > 0;
    public bool HasWork => Entries.Count > 0 || BackfillStockUomName is not null || IsNewItem;

    public PreparedCompanyItemGroup() { }
    public PreparedCompanyItemGroup(List<CompanyItemImportRowResult> rows) => Rows = rows ?? new();
}

public enum CompanyItemCommitStatus { Created, Updated, Skipped, Failed }

public sealed record CompanyItemCommitOutcome(
    string ItemCode,
    CompanyItemCommitStatus Status,
    string Message);

public sealed class CompanyItemCommitSummary
{
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int PricesApplied { get; set; }
    public int PricesScheduled { get; set; }
    public int PricesSkipped { get; set; }
    public int StockUomBackfilled { get; set; }
    public int Failed { get; set; }
    public List<CompanyItemCommitOutcome> Outcomes { get; } = new();

    public int Committed => ItemsCreated + ItemsUpdated;
}