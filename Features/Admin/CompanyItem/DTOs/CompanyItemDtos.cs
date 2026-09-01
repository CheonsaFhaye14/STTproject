using STTproject.Shared.Components.Filter;
namespace STTproject.Features.Admin.CompanyItem.DTOs
{
    public class CompanyItemListDto
    {
        public int CompanyItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? Category { get; set; }
        public string? Principal { get; set; }
        public decimal? StockPrice { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }

    public class CompanyItemCreateDto
    {
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? Category { get; set; }
        public string? Principal { get; set; }
        public decimal? StockPrice { get; set; }
        public bool IsActive { get; set; }
        public int? CreatedBy { get; set; }
    }

    public class CompanyItemUpdateDto
    {
        public int CompanyItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public string? Category { get; set; }
        public string? Principal { get; set; }
        public decimal? StockPrice { get; set; }
        public bool IsActive { get; set; }
        public int? UpdatedBy { get; set; }
    }

    public class CompanyItemPriceHistoryDto
    {
        public int CompanyItemPriceHistoryId { get; set; }
        public int CompanyItemId { get; set; }
        public decimal NewPrice { get; set; }
        public decimal OldPrice { get; set; }
        public DateTime EffectivityDate { get; set; }
        public DateTime CreatedDate { get; set; }      
        public int? CreatedBy { get; set; }
    }

    public class CompanyItemImportResultDto
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int TotalCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool HasErrors => Errors.Count > 0;
    }

    public class CompanyItemImportRowDto : IImportRow
    {
        public int RowNumber { get; set; }
        public string? CompanyItemCode { get; set; }
        public string? CompanyItemName { get; set; }
        public string? Principal { get; set; }
        public string? Category { get; set; }
        public decimal? StockPrice { get; set; }
        public bool IsActive { get; set; }
        public List<string> Issues { get; set; } = new();

        IReadOnlyList<string> IImportRow.Issues => Issues;
    }

    public class CompanyItemImportGroupDto : IImportGroup<CompanyItemImportRowDto>
    {
        public string GroupKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<CompanyItemImportRowDto> Rows { get; set; } = new();
        public List<string> Issues { get; set; } = new();
        public bool Selected { get; set; }
        public bool IsSaved { get; set; }

        IReadOnlyList<string> IImportGroup<CompanyItemImportRowDto>.Issues => Issues;
    }

    public sealed class CompanyItemImportResult
    {
        public string? Principal { get; set; }
        public List<string> OriginalHeaders { get; set; } = new();
        public List<CompanyItemImportRowResult> Rows { get; } = new();
        public List<PreparedCompanyItemGroup> PreparedGroups { get; } = new();
        public List<CompanyItemImportIssue> Issues { get; } = new(); 

        public int SuccessCount => Rows.Count(r => r.IsSuccess);
        public int ErrorCount => Rows.Count(r => !r.IsSuccess);
        public bool HasRows => Rows.Count > 0;
        public bool HasIssues => Issues.Count > 0;

        public void AddError(int rowNumber, string companyItemCode, string message)
            => Issues.Add(new CompanyItemImportIssue(rowNumber, companyItemCode, message));
    }

    public sealed class CompanyItemImportRowResult
    {
        public int RowNumber { get; set; }
        public string CompanyItemCode { get; set; } = string.Empty;
        public string CompanyItemName { get; set; } = string.Empty;
        public string? Principal { get; set; }
        public string? Category { get; set; }
        public decimal? StockPrice { get; set; }
        public bool IsSuccess { get; set; }
        public int? CompanyItemId { get; set; }
        public List<string> Issues { get; } = new();
        public List<string> Warnings { get; } = new();
        public Dictionary<string, string?> RawValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class PreparedCompanyItemGroup
    {
        public List<CompanyItemImportRowResult> Rows { get; } = new();
        public List<CompanyItemImportIssue> Issues { get; } = new();
        public bool Selected { get; set; }
        public bool IsSaved { get; set; }

        public PreparedCompanyItemGroup() { }
        public PreparedCompanyItemGroup(List<CompanyItemImportRowResult> rows) => Rows = rows ?? new();
    }

    public sealed record CompanyItemImportIssue(int RowNumber, string CompanyItemCode, string Message);

}