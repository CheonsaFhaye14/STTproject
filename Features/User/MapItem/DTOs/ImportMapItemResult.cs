namespace STTproject.Features.User.MapItem.DTOs;

public sealed class ImportMapItemResult
{
	public List<ImportMapItemRowResult> Rows { get; } = new();
	public List<PreparedItemGroup> PreparedGroups { get; } = new();
	public List<ImportMapItemIssue> Issues { get; } = new();

	public int SuccessCount => Rows.Count(row => row.IsSuccess);

	public int ErrorCount => Rows.Count(row => !row.IsSuccess);

	public bool HasRows => Rows.Count > 0;

	public bool HasIssues => Issues.Count > 0;

	public void AddError(int rowNumber, string subdItemCode, string message)
	{
		Issues.Add(new ImportMapItemIssue(rowNumber, subdItemCode, message));
	}
}

public sealed class ImportMapItemRowResult
{
	public int RowNumber { get; set; }
	public string SubDistributorCode { get; set; } = string.Empty;
	public string SubDistributorName { get; set; } = string.Empty;
	public string Principal { get; set; } = string.Empty;
	public string CompanyItemCode { get; set; } = string.Empty;
	public string CompanyItemName { get; set; } = string.Empty;
	public string SubdItemCode { get; set; } = string.Empty;
	public string SubdItemName { get; set; } = string.Empty;
	public string UomName { get; set; } = string.Empty;
	public decimal? Conversion { get; set; }
	public decimal? Price { get; set; }
	public bool IsSuccess { get; set; }
	public string Message { get; set; } = string.Empty;
	public int? SubdItemId { get; set; }
	public int? ItemsUomId { get; set; }
	public List<string> Issues { get; } = new();
}

public sealed class PreparedItemGroup
{
	public List<ImportMapItemRowResult> Rows { get; } = new();
	public List<ImportMapItemIssue> Issues { get; } = new();
	public bool Selected { get; set; }
	public bool IsSaved { get; set; }
	public string? SaveErrorMessage { get; set; }

	public PreparedItemGroup() { }

	public PreparedItemGroup(List<ImportMapItemRowResult> rows)
	{
		Rows = rows ?? new List<ImportMapItemRowResult>();
	}
}

public sealed record ImportMapItemIssue(int RowNumber, string SubdItemCode, string Message);