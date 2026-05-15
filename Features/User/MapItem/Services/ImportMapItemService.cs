using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Services;

namespace STTproject.Features.User.MapItem.Services;

public sealed class ImportMapItemService
{
	private readonly IDbContextFactory<SttprojectContext> _contextFactory;
    private readonly IMapItemService _mapItemService;
	private readonly ILogger<ImportMapItemService> _logger;

	public ImportMapItemService(
		IDbContextFactory<SttprojectContext> contextFactory,
        IMapItemService mapItemService,
		ILogger<ImportMapItemService> logger)
	{
		_contextFactory = contextFactory;
		_mapItemService = mapItemService;
		_logger = logger;
	}

	public async Task<ImportMapItemResult> ImportFromExcelAsync(
		Stream excelStream,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		var result = await PrepareFromExcelAsync(excelStream, currentUserId, cancellationToken);
		if (result.PreparedMapItem.Count == 0)
		{
			return result;
		}

		var validPreparedMapItems = result.PreparedMapItems
			.Where(prepared => prepared.Issues.Count == 0)
			.ToList();

        if (validPreparedMapItems.Count == 0)
		{
			return result;
		}

        var commitResult = await CommitPreparedMapItemsAsync(validPreparedMapItems, currentUserId, cancellationToken);
		result.ImportedMapItemCount = commitResult.ImportedMapItemCount;
		result.ImportedRowCount = commitResult.ImportedRowCount;
foreach (var issue in commitResult.Issues)
		{
			result.Issues.Add(issue);
		}

		return result;
	}

	public async Task<ImportMapItemResult> PrepareFromExcelAsync(
		Stream excelStream,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		var result = new ImportMapItemResult();

		if (excelStream is null || !excelStream.CanRead)
		{
			result.AddError(0, string.Empty, "Import file is missing or unreadable.");
			return result;
		}

		if (currentUserId <= 0)
		{
			result.AddError(0, string.Empty, "Unable to identify the current user. Please sign in again.");
			return result;
		}

		using var workbook = new XLWorkbook(excelStream);
		var worksheet = workbook.Worksheets
			.FirstOrDefault(IsMapItemWorksheet)
			?? workbook.Worksheets.First();

		var headers = BuildHeaderMap(worksheet);
		var requiredHeaders = new[]
		{
			"SubDistributorCode",
			"Principal",
			"CompanyItemCode",
			"CompanyItemName",
			"SubdItemCode",
			"SubdItemName",
			"UOM",
            "Conversion",
            "Price"
		};

		var missingHeaders = requiredHeaders
			.Where(header => !headers.ContainsKey(header))
			.ToList();

		if (missingHeaders.Count > 0)
		{
			result.AddError(0, string.Empty, $"Missing required column(s): {string.Join(", ", missingHeaders)}.");
			return result;
		}

		var parsedRows = ReadRows(worksheet, headers, result);
		if (parsedRows.Count == 0)
		{
			result.AddError(0, string.Empty, "No mapped item rows were found in the template.");
			return result;
		}

		foreach (var MapItemGroup in parsedRows.GroupBy(row => row.SubdItemCode, StringComparer.OrdinalIgnoreCase))
		{
			var MapItemRows = MapItemGroup.ToList();
			var SubdItemCode = MapItemGroup.Key.Trim();

			var preparedMapItem = new PreparedMapItem
			{
				SubdItemCode = SubdItemCode,
				
                Uoms = new List<InputUomsModel>(),
				Issues = new List<ImportMapItemIssue>(),
				Selected = true
			};

			try
			{
				var groupConsistencyError = ValidateGroupConsistency(MapItemRows);
				if (!string.IsNullOrWhiteSpace(groupConsistencyError))
				{
					result.AddError(MapItemRows[0].RowNumber, MapItemRows[0].SubdItemCode, groupConsistencyError);
					preparedMapItem.Issues.Add(new ImportMapItemIssue(MapItemRows[0].RowNumber, MapItemRows[0].SubdItemCode, groupConsistencyError));
					result.PreparedMapItems.Add(preparedMapItem);
					continue;
				}

				var firstRow = MapItemRows[0];

				preparedMapItem.Invoice = new InputUomsModel
				{
					SubDistributorCode = firstRow.SubDistributorCode,
					InvoiceDate = firstRow.InvoiceDate,
					OrderType = orderType,
					CustomerId = customer.CustomerId,
					CustomerCode = customer.CustomerCode,
					CustomerName = customer.CustomerName,
					CustomerType = customer.CustomerType,
					CustomerBranchId = customerBranch.CustomerBranchId,
					CustomerBranchName = customerBranch.BranchName,
					SubdistributorId = subDistributorId
				};

				var items = new List<InputItemModel>();
				foreach (var row in invoiceRows)
				{
					if (!subdItemByCode.TryGetValue(Normalize(row.SkuCode), out var subdItem))
					{
						var msg = $"SKU code '{row.SkuCode}' was not found.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, "SkuCode"));
						result.AddError(row.RowNumber, invoiceNumber, msg, "SkuCode");
						items.Clear();
						break;
					}

					if (!uomLookup.TryGetValue((subdItem.SubdItemId, Normalize(row.UOM)), out var uom))
					{
						var msg = $"UOM '{row.UOM}' was not found for SKU '{row.SkuCode}'.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, "UOM"));
						result.AddError(row.RowNumber, invoiceNumber, msg, "UOM");
						items.Clear();
						break;
					}

					if (row.Quantity <= 0)
					{
						var msg = "Quantity must be greater than 0.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, "Quantity"));
						result.AddError(row.RowNumber, invoiceNumber, msg, "Quantity");
						items.Clear();
						break;
					}

					items.Add(new InputItemModel
					{
						ItemCode = subdItem.SubdItemCode,
						ItemName = subdItem.ItemName,
						SubdItemId = subdItem.SubdItemId,
						ItemsUomId = uom.ItemsUomId,
						UomName = uom.UomName,
						Quantity = row.Quantity,
						Amount = uom.Price * row.Quantity
					});
				}

				if (items.Count == 0)
				{
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				var aggregatedItems = items
					.GroupBy(item => new { item.SubdItemId, item.ItemsUomId, item.ItemCode, item.ItemName, item.UomName })
					.Select(group => new InputItemModel
					{
						ItemCode = group.Key.ItemCode,
						ItemName = group.Key.ItemName,
						SubdItemId = group.Key.SubdItemId,
						ItemsUomId = group.Key.ItemsUomId,
						UomName = group.Key.UomName,
						Quantity = group.Sum(item => item.Quantity),
						Amount = group.Sum(item => item.Amount)
					})
					.ToList();

				preparedInvoice.Items.AddRange(aggregatedItems);

				var validationErrors = await SalesInvoiceValidation.ValidateHeaderAsync(
					preparedInvoice.Invoice!,
					hasCustomerBranches,
					() => _salesInvoiceService.InvoiceNumberExistsAsync(preparedInvoice.Invoice!.InvoiceNumber, 0, cancellationToken));

				if (validationErrors.Count > 0)
				{
					var msg = string.Join(" ", validationErrors.Values);
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(firstRow.RowNumber, invoiceNumber, msg));
					result.AddError(firstRow.RowNumber, invoiceNumber, msg);
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				result.PreparedInvoices.Add(preparedInvoice);
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				_logger.LogError(ex, "Failed to prepare sales invoice {InvoiceNumber} from row {RowNumber}: {Message}", invoiceNumber, invoiceRows[0].RowNumber, baseMsg);
				result.AddError(invoiceRows[0].RowNumber, invoiceNumber, $"Unexpected error while preparing invoice '{invoiceNumber}': {baseMsg}");
				preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(invoiceRows[0].RowNumber, invoiceNumber, $"Unexpected error while preparing invoice '{invoiceNumber}': {baseMsg}"));
				result.PreparedInvoices.Add(preparedInvoice);
			}
		}

		return result;
	}

	public async Task<ImportSalesInvoiceResult> CommitPreparedInvoicesAsync(IEnumerable<PreparedInvoice> preparedInvoices, int currentUserId, CancellationToken cancellationToken = default)
	{
		var result = new ImportSalesInvoiceResult();
		if (preparedInvoices is null)
		{
			result.AddError(0, string.Empty, "No prepared invoices provided for commit.");
			return result;
		}

		foreach (var prepared in preparedInvoices)
		{
			if (prepared is null || !prepared.Selected)
				continue;

			if (prepared.Issues != null && prepared.Issues.Count > 0)
			{
				continue;
			}

			try
			{
				var saveResult = await _salesInvoiceService.SaveInvoiceAsync(
					prepared.Invoice!,
					prepared.Items!,
					0,
					currentUserId,
					cancellationToken);

				if (saveResult.IsDuplicate)
				{
					var msg = $"Sales invoice '{prepared.InvoiceNumber}' already exists.";
					result.AddError(0, prepared.InvoiceNumber, msg);
					prepared.IsSaved = false;
					prepared.SaveErrorMessage = msg;
					continue;
				}

				if (!saveResult.IsSaved)
				{
					var msg = saveResult.ErrorMessage ?? "Unable to save invoice.";
					result.AddError(0, prepared.InvoiceNumber, msg);
					prepared.IsSaved = false;
					prepared.SaveErrorMessage = msg;
					continue;
				}

				prepared.IsSaved = true;
				result.ImportedInvoiceCount++;
				result.ImportedRowCount += prepared.Items?.Count ?? 0;
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				result.AddError(0, prepared.InvoiceNumber, $"Unexpected error while saving invoice '{prepared.InvoiceNumber}': {baseMsg}");
				prepared.IsSaved = false;
				prepared.SaveErrorMessage = baseMsg;
			}
		}

		return result;
	}

	private static bool IsSalesInvoiceWorksheet(IXLWorksheet worksheet)
	{
		return BuildHeaderMap(worksheet).ContainsKey("InvoiceCode");
	}

	private static List<ImportedInvoiceRow> ReadRows(
		IXLWorksheet worksheet,
		IReadOnlyDictionary<string, int> headers,
		ImportSalesInvoiceResult result)
	{
		var rows = new List<ImportedInvoiceRow>();
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

		for (int rowNumber = 2; rowNumber <= lastRow; rowNumber++)
		{
			var row = worksheet.Row(rowNumber);
			if (row.CellsUsed().All(cell => cell.IsEmpty()))
			{
				continue;
			}

			var invoiceCode = GetString(row, headers["InvoiceCode"]);
			var customerCode = GetString(row, headers["CustomerCode"]);
			var customerBranch = headers.TryGetValue("CustomerBranch", out var branchColumn)
				? GetString(row, branchColumn)
				: string.Empty;
			var orderType = GetString(row, headers["OrderType"]);
			var skuCode = GetString(row, headers["SkuCode"]);
			var uom = GetString(row, headers["UOM"]);
			var blankRequiredColumns = new List<string>();

			if (string.IsNullOrWhiteSpace(invoiceCode)) blankRequiredColumns.Add("InvoiceCode");
			if (row.Cell(headers["InvoiceDate"]).IsEmpty()) blankRequiredColumns.Add("InvoiceDate");
			if (string.IsNullOrWhiteSpace(customerCode)) blankRequiredColumns.Add("CustomerCode");
			if (string.IsNullOrWhiteSpace(orderType)) blankRequiredColumns.Add("OrderType");
			if (string.IsNullOrWhiteSpace(skuCode)) blankRequiredColumns.Add("SkuCode");
			if (string.IsNullOrWhiteSpace(uom)) blankRequiredColumns.Add("UOM");
			if (row.Cell(headers["Quantity"]).IsEmpty()) blankRequiredColumns.Add("Quantity");

			if (string.IsNullOrWhiteSpace(invoiceCode) && string.IsNullOrWhiteSpace(customerCode) && string.IsNullOrWhiteSpace(skuCode))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(invoiceCode))
			{
				result.AddError(rowNumber, string.Empty, "Invoice code is required.", "InvoiceCode");
				continue;
			}

			if (blankRequiredColumns.Count > 0)
			{
				foreach (var columnName in blankRequiredColumns)
				{
					var message = columnName switch
					{
						"InvoiceCode" => "Invoice code is required.",
						"InvoiceDate" => "Invoice date is required.",
						"CustomerCode" => "Customer code is required.",
						"OrderType" => "Order type is required.",
						"SkuCode" => "SKU code is required.",
						"UOM" => "UOM is required.",
						"Quantity" => "Quantity is required.",
						_ => $"{columnName} is required."
					};

					result.AddError(rowNumber, invoiceCode, message, columnName);
				}
				continue;
			}

			if (!TryGetDateOnly(row.Cell(headers["InvoiceDate"]), out var invoiceDate))
			{
				result.AddError(rowNumber, invoiceCode, "Invoice date is invalid.", "InvoiceDate");
				continue;
			}

			if (!TryGetInt(row.Cell(headers["Quantity"]), out var quantity) || quantity <= 0)
			{
				result.AddError(rowNumber, invoiceCode, "Quantity must be a whole number greater than 0.", "Quantity");
				continue;
			}

			rows.Add(new ImportedInvoiceRow(
				rowNumber,
				invoiceCode,
				invoiceDate,
				customerCode,
				customerBranch,
				orderType,
				skuCode,
				uom,
				quantity));
		}

		return rows;
	}

	private static IReadOnlyDictionary<string, int> BuildHeaderMap(IXLWorksheet worksheet)
	{
		var headerRow = worksheet.Row(1);
		var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		foreach (var cell in headerRow.CellsUsed())
		{
			var header = Normalize(cell.GetString());
			if (string.IsNullOrWhiteSpace(header))
			{
				continue;
			}

			if (header is "invoicecode" or "invoice number" or "salesinvoicecode")
			{
				headers.TryAdd("InvoiceCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "invoicedate" or "salesinvoicedate")
			{
				headers.TryAdd("InvoiceDate", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "customercode")
			{
				headers.TryAdd("CustomerCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "customerbranch" or "branchname" or "customerbranchname")
			{
				headers.TryAdd("CustomerBranch", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "ordertype")
			{
				headers.TryAdd("OrderType", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "skucode" or "subditemcode")
			{
				headers.TryAdd("SkuCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "uom" or "unitofmeasure")
			{
				headers.TryAdd("UOM", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "quantity")
			{
				headers.TryAdd("Quantity", cell.Address.ColumnNumber);
			}
		}

		return headers;
	}

	private static string ValidateGroupConsistency(List<ImportedInvoiceRow> rows)
	{
		if (rows.Select(row => row.InvoiceDate).Distinct().Count() > 1)
		{
			return "Invoice date values must be the same for all rows in the same invoice.";
		}

		if (rows.Select(row => Normalize(row.CustomerCode)).Distinct().Count() > 1)
		{
			return "Customer code values must be the same for all rows in the same invoice.";
		}

		if (rows.Select(row => Normalize(row.CustomerBranch)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Count() > 1)
		{
			return "Customer branch values must be the same for all rows in the same invoice.";
		}

		if (rows.Select(row => Normalize(row.OrderType)).Distinct().Count() > 1)
		{
			return "Order type values must be the same for all rows in the same invoice.";
		}

		return string.Empty;
	}

	private static bool TryResolveBranch(
		string branchName,
		int customerId,
		List<CustomerBranch>? customerBranchList,
		out CustomerBranch customerBranch,
		out string errorMessage)
	{
		customerBranch = null!;
		errorMessage = string.Empty;

		if (customerBranchList is null || customerBranchList.Count == 0)
		{
			errorMessage = "Selected customer has no branch configured.";
			return false;
		}

		if (!string.IsNullOrWhiteSpace(branchName))
		{
			var selectedBranch = customerBranchList.FirstOrDefault(branch =>
				branch.CustomerId == customerId &&
				Normalize(branch.BranchName) == Normalize(branchName));

			if (selectedBranch is not null)
			{
				customerBranch = selectedBranch;
				return true;
			}

			errorMessage = $"Customer branch '{branchName}' was not found for the selected customer.";
			return false;
		}

		customerBranch = customerBranchList.FirstOrDefault(branch => branch.IsDefault)
			?? customerBranchList[0];
		return true;
	}

	private static bool TryParseOrderType(string orderType, out string normalizedOrderType)
	{
		if (string.Equals(orderType, "Invoice", StringComparison.OrdinalIgnoreCase))
		{
			normalizedOrderType = "Invoice";
			return true;
		}

		if (string.Equals(orderType, "Credit", StringComparison.OrdinalIgnoreCase))
		{
			normalizedOrderType = "Credit";
			return true;
		}

		normalizedOrderType = string.Empty;
		return false;
	}

	private static string GetString(IXLRow row, int columnNumber)
	{
		return row.Cell(columnNumber).GetString().Trim();
	}

	private static bool TryGetInt(IXLCell cell, out int value)
	{
		if (cell.DataType == XLDataType.Number)
		{
			value = (int)cell.GetDouble();
			return true;
		}

		if (int.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		return int.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
	}

	private static bool TryGetDateOnly(IXLCell cell, out DateOnly date)
	{
		if (cell.DataType == XLDataType.DateTime)
		{
			date = DateOnly.FromDateTime(cell.GetDateTime());
			return true;
		}

		if (cell.TryGetValue<DateTime>(out var dateTime))
		{
			date = DateOnly.FromDateTime(dateTime);
			return true;
		}

		var text = cell.GetString().Trim();
		if (DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date))
		{
			return true;
		}

		if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
		{
			return true;
		}

		if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out dateTime))
		{
			date = DateOnly.FromDateTime(dateTime);
			return true;
		}

		date = default;
		return false;
	}

	private static string Normalize(string value)
	{
		return value.Trim().ToLowerInvariant();
	}

	private sealed record ImportedInvoiceRow(
		int RowNumber,
		string InvoiceCode,
		DateOnly InvoiceDate,
		string CustomerCode,
		string CustomerBranch,
		string OrderType,
		string SkuCode,
		string UOM,
		int Quantity);
}

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

	public void AddError(int rowNumber, string invoiceNumber, string message, string? columnName = null)
	{
		Issues.Add(new ImportSalesInvoiceIssue(rowNumber, invoiceNumber, message, columnName));
	}
}

public sealed record ImportSalesInvoiceIssue(int RowNumber, string InvoiceNumber, string Message, string? ColumnName = null);
