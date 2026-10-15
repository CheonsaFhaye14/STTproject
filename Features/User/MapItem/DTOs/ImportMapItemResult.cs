namespace STTproject.Features.User.MapItem.DTOs;

public enum StockUomSource
{
	None,
	Explicit,
	Suggested,
	Chosen
}

public sealed class ImportMapItemResult
{
	public List<ImportMapItemRowResult> Rows { get; } = new();
	public List<PreparedItemGroup> PreparedGroups { get; } = new();
	public List<ImportMapItemIssue> Issues { get; } = new();
    public List<string> OriginalHeaders { get; set; } = new();

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
	public int? Conversion { get; set; }
	public decimal? Price { get; set; }

	public string? StockUom { get; set; }

	public bool IsStockUom { get; set; }

	public bool IsSuccess { get; set; }
	public string Message { get; set; } = string.Empty;
	public int? SubdItemId { get; set; }
	public int? ItemsUomId { get; set; }
	public List<string> Issues { get; } = new();
	public List<string> Warnings { get; } = new(); 
	public Dictionary<string, string?> RawValues { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PreparedItemGroup
{
	public List<ImportMapItemRowResult> Rows { get; } = new();
	public List<ImportMapItemIssue> Issues { get; } = new();
	public bool Selected { get; set; }
	public bool IsSaved { get; set; }
	public string? SaveErrorMessage { get; set; }

	public string? StockUomName { get; set; }
	public StockUomSource StockUomSource { get; set; }
	public string? StockUomReason { get; set; }

	public bool HasStockUom => !string.IsNullOrWhiteSpace(StockUomName);

	public List<string> StockUomOptions => Rows
		.Where(r => !string.IsNullOrWhiteSpace(r.UomName) && r.Conversion is > 0 && r.Price.HasValue)
		.Select(r => r.UomName.Trim())
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToList();

	public string? ChooseStockUom(string uomName)
	{
		var match = StockUomOptions.FirstOrDefault(o => string.Equals(o, uomName?.Trim(), StringComparison.OrdinalIgnoreCase));
		if (match is null)
			return "A stock UOM needs a conversion and a price.";

		foreach (var row in Rows)
		{
			row.IsStockUom = string.Equals(row.UomName?.Trim(), match, StringComparison.OrdinalIgnoreCase);
			row.Warnings.RemoveAll(w => w.StartsWith("Stock UOM", StringComparison.OrdinalIgnoreCase));
		}

		StockUomName = match;
		StockUomSource = StockUomSource.Chosen;
		StockUomReason = null;
		return null;
	}

	public PreparedItemGroup() { }

	public PreparedItemGroup(List<ImportMapItemRowResult> rows)
	{
		Rows = rows ?? new List<ImportMapItemRowResult>();
	}
}

public sealed record ImportMapItemIssue(int RowNumber, string SubdItemCode, string Message);