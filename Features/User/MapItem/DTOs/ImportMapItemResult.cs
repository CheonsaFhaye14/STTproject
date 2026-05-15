namespace STTproject.Features.User.MapItem.DTOs;

public sealed class ImportMapItemResult
{
	public List<ImportMapItemRowResult> Rows { get; } = new();

	public int SuccessCount => Rows.Count(row => row.IsSuccess);

	public int ErrorCount => Rows.Count(row => !row.IsSuccess);

	public bool HasRows => Rows.Count > 0;
}

public sealed class ImportMapItemRowResult
{
	public int RowNumber { get; set; }
	public string SubDistributorCode { get; set; } = string.Empty;
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