using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Features.User.SalesInvoice.Validators;
using STTproject.Models;
using STTproject.Services;


namespace STTproject.Features.User.SalesInvoice.Services;

public sealed class ImportSalesInvoiceService
{
	private const int MaxHeaderScanRows = 10;
	private readonly IDbContextFactory<SttprojectContext> _contextFactory;
	private readonly ISalesInvoiceService _salesInvoiceService;
	private readonly ILogger<ImportSalesInvoiceService> _logger;

	public ImportSalesInvoiceService(
		IDbContextFactory<SttprojectContext> contextFactory,
		ISalesInvoiceService salesInvoiceService,
		ILogger<ImportSalesInvoiceService> logger)
	{
		_contextFactory = contextFactory;
		_salesInvoiceService = salesInvoiceService;
		_logger = logger;
	}

	public async Task<ImportSalesInvoiceResult> ImportFromExcelAsync(
		Stream excelStream,
		int subDistributorId,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		var result = await PrepareFromExcelAsync(excelStream, subDistributorId, currentUserId, cancellationToken);
		if (result.PreparedInvoices.Count == 0)
		{
			return result;
		}

		var validPreparedInvoices = result.PreparedInvoices
			.Where(prepared => prepared.Issues.Count == 0)
			.ToList();

		if (validPreparedInvoices.Count == 0)
		{
			return result;
		}

		var commitResult = await CommitPreparedInvoicesAsync(validPreparedInvoices, currentUserId, cancellationToken);
		result.ImportedInvoiceCount = commitResult.ImportedInvoiceCount;
		result.ImportedRowCount = commitResult.ImportedRowCount;

		foreach (var issue in commitResult.Issues)
		{
			result.Issues.Add(issue);
		}

		return result;
	}

	public async Task<ImportSalesInvoiceResult> PrepareFromExcelAsync(
		Stream excelStream,
		int subDistributorId,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		var result = new ImportSalesInvoiceResult();

		if (excelStream is null || !excelStream.CanRead)
		{
			result.AddError(0, string.Empty, "Import file is missing or unreadable.");
			return result;
		}

		if (subDistributorId <= 0)
		{
			result.AddError(0, string.Empty, "A valid subdistributor is required before importing sales invoices.");
			return result;
		}

		if (currentUserId <= 0)
		{
			result.AddError(0, string.Empty, "Unable to identify the current user. Please sign in again.");
			return result;
		}

		await using var context = _contextFactory.CreateDbContext();
		var subDistributor = await context.SubDistributors
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.SubDistributorId == subDistributorId && item.IsActive, cancellationToken);

		if (subDistributor is null)
		{
			result.AddError(0, string.Empty, "A valid subdistributor is required before importing sales invoices.");
			return result;
		}

		using var workbook = new XLWorkbook(excelStream);
		var worksheetCandidates = workbook.Worksheets
			.Select(worksheet => new { Worksheet = worksheet, HeaderRowNumber = DetectHeaderRow(worksheet, subDistributor) })
			.Where(candidate => candidate.HeaderRowNumber > 0)
			.OrderBy(candidate => candidate.HeaderRowNumber)
			.ToList();

		var worksheetWithHeader = worksheetCandidates.FirstOrDefault();

		if (worksheetWithHeader is null)
		{
			result.AddError(0, string.Empty, $"Could not find a sales invoice header row within the first {MaxHeaderScanRows} rows of any worksheet.");
			return result;
		}

		var worksheet = worksheetWithHeader.Worksheet;
		var headerRowNumber = worksheetWithHeader.HeaderRowNumber;
		var headers = BuildHeaderMap(worksheet, headerRowNumber, subDistributor);

		// Required headers with OR-groups: InvoiceCode, InvoiceDate, SalesManName, (CustomerCode|CustomerName), (SkuCode|ItemName)
		var hasInvoiceCode = headers.ContainsKey("InvoiceCode");
		var hasInvoiceDate = headers.ContainsKey("InvoiceDate");
		var hasSalesManName = headers.ContainsKey("SalesManName");
		var hasCustomer = headers.ContainsKey("CustomerCode") || headers.ContainsKey("CustomerName");
		var hasSku = headers.ContainsKey("SkuCode") || headers.ContainsKey("ItemName");

		var (isValid, errorMessage) = InvoiceDataValidator.ValidateRequiredHeaders(headers);
		if (!isValid)
		{
			result.AddError(0, string.Empty, errorMessage);
			return result;
		}

		// NetAmount/Gross are optional; OrderType may be provided per-row or inferred from signed amounts where available.
		var customers = await context.Customers
			.AsNoTracking()
			.Where(customer => customer.SubDistributorId == subDistributorId && customer.IsActive)
			.ToListAsync(cancellationToken);

		var subdItems = await context.SubdItems
			.AsNoTracking()
			.Where(item => item.SubDistributorId == subDistributorId && item.IsActive)
			.ToListAsync(cancellationToken);

		var subdItemIds = subdItems.Select(item => item.SubdItemId).Distinct().ToList();
		var uoms = await context.ItemsUoms
			.AsNoTracking()
			.Where(uom => subdItemIds.Contains(uom.SubdItemId))
			.ToListAsync(cancellationToken);

		var customerByCode = BuildLookupDictionary(customers, customer => customer.CustomerCode, NormalizeCustomerLookup);
		var customerByName = BuildLookupDictionary(customers, customer => customer.CustomerName, NormalizeCustomerLookup);
		var subdItemByCode = BuildLookupDictionary(subdItems, item => item.SubdItemCode, Normalize);
		var subdItemById = subdItems.ToDictionary(item => item.SubdItemId);
		var uomLookup = uoms
			.GroupBy(uom => (uom.SubdItemId, Normalize(uom.UomName)))
			.ToDictionary(group => group.Key, group => group.First());
		var customerById = customers.ToDictionary(customer => customer.CustomerId);
		var itemsUomById = uoms.ToDictionary(uom => uom.ItemsUomId);

		var parsedRows = ReadRows(
			worksheet,
			headers,
			result,
			customerByCode,
			customerByName,
			subdItemByCode,
			uomLookup,
			customers,
			subdItems,
			headerRowNumber);
		if (parsedRows.Count == 0)
		{
			result.AddError(0, string.Empty, "No invoice rows were found in the template.");
			return result;
		}

		// Group by invoice code plus customer and key header fields so rows with the same invoice code
		// but different customer/order/date are treated as separate prepared invoices.
		foreach (var invoiceGroup in parsedRows.GroupBy(row =>
					 string.Join("|",
						 (row.InvoiceCode ?? string.Empty).Trim().ToUpperInvariant(),
						 (row.CustomerCode ?? string.Empty).Trim().ToUpperInvariant(),
						 (row.OrderType ?? string.Empty).Trim().ToUpperInvariant(),
						 row.InvoiceDate.ToString("yyyy-MM-dd")), StringComparer.OrdinalIgnoreCase))
		{
			var invoiceRows = invoiceGroup.ToList();
			var invoiceNumber = invoiceRows.First().InvoiceCode.Trim();

			var preparedInvoice = new PreparedInvoice
			{
				InvoiceNumber = invoiceNumber,
				Items = new List<InputItemModel>(),
				Issues = new List<ImportSalesInvoiceIssue>(),
				Selected = true
			};

			try
			{
				var groupConsistencyError = InvoiceDataValidator.ValidateGroupConsistency(invoiceRows);
				if (!string.IsNullOrWhiteSpace(groupConsistencyError))
				{
					result.AddError(invoiceRows[0].RowNumber, invoiceNumber, groupConsistencyError);
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						invoiceRows[0].RowNumber,
						invoiceNumber,
						groupConsistencyError,
						string.Empty,
						BuildCustomerDisplayValue(invoiceRows[0].CustomerCode, invoiceRows[0].CustomerName)));
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				var firstRow = invoiceRows[0];
				var firstRowCustomerValue = BuildCustomerDisplayValue(firstRow.CustomerCode, firstRow.CustomerName);

				if (firstRow.ResolvedCustomerId == 0 || !customerById.TryGetValue(firstRow.ResolvedCustomerId, out var customer))
				{
					var msg = $"Customer '{firstRowCustomerValue}' could not be resolved.";
					result.AddError(firstRow.RowNumber, invoiceNumber, msg, "CustomerCode");
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						firstRow.RowNumber,
						invoiceNumber,
						msg,
						string.Empty,
						firstRowCustomerValue,
						"CustomerCode"));
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				if (!InvoiceDataValidator.ResolveOrderType(firstRow.OrderType, null, firstRow.Quantity, out var orderType))
				{
					var msg = $"Order type '{firstRow.OrderType}' is invalid. Use Invoice or Credit.";
					result.AddError(firstRow.RowNumber, invoiceNumber, msg, "OrderType");
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						firstRow.RowNumber,
						invoiceNumber,
						msg,
						string.Empty,
						firstRowCustomerValue,
						"OrderType"));
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				preparedInvoice.Invoice = new InputInvoiceModel
				{
					InvoiceNumber = invoiceNumber,
					InvoiceDate = firstRow.InvoiceDate,
					OrderType = orderType,
					CustomerId = customer.CustomerId,
					CustomerCode = customer.CustomerCode,
					CustomerName = customer.CustomerName,
					CustomerType = customer.CustomerType,
					SubdistributorId = subDistributorId,
					SalesManName = firstRow.SalesManName
				};

				var items = new List<InputItemModel>();
				var unitPriceCache = new Dictionary<int, decimal>();
				var reportedMissingSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				async Task<decimal> GetUnitPriceAsync(int itemsUomId, DateOnly invoiceDate)
				{
					if (!unitPriceCache.TryGetValue(itemsUomId, out var cachedPrice))
					{
						cachedPrice = await _salesInvoiceService.ResolveUomPriceAsync(itemsUomId, invoiceDate, cancellationToken);
						unitPriceCache[itemsUomId] = cachedPrice;
					}

					return cachedPrice;
				}
				foreach (var row in invoiceRows)
				{
					if (row.ResolvedSubdItemId == 0 || !subdItemById.TryGetValue(row.ResolvedSubdItemId, out var subdItem))
					{
						var itemKey = string.IsNullOrWhiteSpace(row.ResolvedSubdItemCode) ? row.SkuCode : row.ResolvedSubdItemCode;
						if (reportedMissingSkus.Add(itemKey))
						{
							var msg = $"SKU code '{row.SkuCode}' was not found.";
							preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, "SkuCode", BuildCustomerDisplayValue(row.CustomerCode, row.CustomerName), row.SkuCode));
							result.AddError(row.RowNumber, invoiceNumber, msg, "SkuCode");
						}
						items.Clear();
						break;
					}

					if (row.ResolvedItemsUomId == 0 || !itemsUomById.TryGetValue(row.ResolvedItemsUomId, out var uom))
					{
						var msg = $"UOM '{row.UOM}' was not found for SKU '{row.SkuCode}'.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, "UOM", BuildCustomerDisplayValue(row.CustomerCode, row.CustomerName)));
						result.AddError(row.RowNumber, invoiceNumber, msg, "UOM");
						items.Clear();
						break;
					}

					if (row.Quantity <= 0)
					{
						if (row.Quantity == 0)
						{
							continue;
						}
					}

					var unitPrice = await GetUnitPriceAsync(uom.ItemsUomId, firstRow.InvoiceDate);
					var absoluteQuantity = Math.Abs(row.Quantity);

					items.Add(new InputItemModel
					{
						ItemCode = subdItem.SubdItemCode,
						ItemName = subdItem.ItemName,
						SubdItemId = subdItem.SubdItemId,
						ItemsUomId = uom.ItemsUomId,
						UomName = uom.UomName,
						Quantity = absoluteQuantity,
						Amount = unitPrice * absoluteQuantity
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

				// If the invoice OrderType is Credit, ensure amounts are negative
				if (string.Equals(preparedInvoice.Invoice?.OrderType, "Credit", StringComparison.OrdinalIgnoreCase))
				{
					for (int i = 0; i < aggregatedItems.Count; i++)
					{
						var it = aggregatedItems[i];
						it.Amount = -Math.Abs(it.Amount);
						aggregatedItems[i] = it;
					}
				}

				preparedInvoice.Items.AddRange(aggregatedItems);

				var validationErrors = await SalesInvoiceValidation.ValidateHeaderAsync(
					preparedInvoice.Invoice!,
					() => _salesInvoiceService.InvoiceNumberExistsAsync(preparedInvoice.Invoice!.InvoiceNumber, preparedInvoice.Invoice!.OrderType, 0, cancellationToken));

				if (validationErrors.Count > 0)
				{
					var msg = string.Join(" ", validationErrors.Values);
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						firstRow.RowNumber,
						invoiceNumber,
						msg,
						string.Empty,
						string.Empty));
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
				preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
					invoiceRows[0].RowNumber,
					invoiceNumber,
					$"Unexpected error while preparing invoice '{invoiceNumber}': {baseMsg}",
					string.Empty,
					string.Empty));
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
					var msg = $"Sales invoice '{prepared.InvoiceNumber}' already exists for this order type.";
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

	private static List<ImportedInvoiceRow> ReadRows(
		IXLWorksheet worksheet,
		IReadOnlyDictionary<string, int> headers,
		ImportSalesInvoiceResult result,
		IReadOnlyDictionary<string, Data.Customer> customerByCode,
		IReadOnlyDictionary<string, Data.Customer> customerByName,
		IReadOnlyDictionary<string, SubdItem> subdItemByCode,
		IReadOnlyDictionary<(int subdItemId, string UomName), ItemsUom> uomLookup,
		IEnumerable<Data.Customer> allCustomers,
		IEnumerable<SubdItem> allSubdItems,
		int headerRowNumber)
	{
		var rows = new List<ImportedInvoiceRow>();
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
		var hasCustomerNameColumn = headers.TryGetValue("CustomerName", out var customerNameColumn);
		var hasItemNameColumn = headers.TryGetValue("ItemName", out var itemNameColumn);
		var hasUomColumn = headers.TryGetValue("UOM", out var uomColumn);
		var hasQuantityColumn = headers.TryGetValue("Quantity", out var quantityColumn);
		var hasCaseQuantityColumn = headers.TryGetValue("CaseQuantity", out var caseQuantityColumn);
		var hasDozenQuantityColumn = headers.TryGetValue("DozenQuantity", out var dozenQuantityColumn);
		var hasPieceQuantityColumn = headers.TryGetValue("PieceQuantity", out var pieceQuantityColumn);
		var useSplitQuantities = hasCaseQuantityColumn || hasDozenQuantityColumn || hasPieceQuantityColumn;

		for (int rowNumber = headerRowNumber + 1; rowNumber <= lastRow; rowNumber++)
		{
			var row = worksheet.Row(rowNumber);
			var invoiceCode = GetString(row, headers["InvoiceCode"]);
			var customerCode = GetString(row, headers.ContainsKey("CustomerCode") ? headers["CustomerCode"] : -1);
			var customerName = hasCustomerNameColumn ? GetString(row, customerNameColumn) : string.Empty;
			var hasOrderTypeColumn = headers.TryGetValue("OrderType", out var orderTypeColumn);
			var hasNetAmountColumn = headers.TryGetValue("NetAmount", out var netAmountColumn);
			var orderType = hasOrderTypeColumn ? GetString(row, orderTypeColumn) : string.Empty;
			var skuCode = headers.ContainsKey("SkuCode") ? GetString(row, headers["SkuCode"]) : string.Empty;
			var itemName = hasItemNameColumn ? GetString(row, itemNameColumn) : string.Empty;
			var uom = hasUomColumn ? GetString(row, uomColumn) : string.Empty;
			var salesManName = headers.TryGetValue("SalesManName", out var salesManCol) ? GetString(row, salesManCol) : string.Empty;
			var netAmountCell = hasNetAmountColumn ? row.Cell(netAmountColumn) : null;
			var rowHasErrors = false;
			string normalizedOrderType = string.Empty;


			// Infer OrderType from amount or explicit field
			if (!string.IsNullOrWhiteSpace(orderType) && InvoiceDataValidator.TryParseOrderType(orderType, out var parsedOrderType))
			{
				normalizedOrderType = parsedOrderType;
			}
			else if (hasNetAmountColumn && netAmountCell != null && TryGetDecimal(netAmountCell, out var netAmountValue))
			{
				var inferredOrderType = netAmountValue < 0 ? "Credit" : "Invoice";
				if (!string.IsNullOrWhiteSpace(normalizedOrderType) && !string.Equals(normalizedOrderType, inferredOrderType, StringComparison.OrdinalIgnoreCase))
				{
					var msg = $"Inconsistent order type: '{normalizedOrderType}' does not match the sign of the Net Amount.";
					result.AddError(rowNumber, invoiceCode, msg, "OrderType");
					rowHasErrors = true;
				}
				else
				{
					normalizedOrderType = inferredOrderType;
				}
			}

			// Skip completely empty rows
			if (string.IsNullOrWhiteSpace(invoiceCode) &&
				string.IsNullOrWhiteSpace(customerCode) &&
				string.IsNullOrWhiteSpace(orderType) &&
				string.IsNullOrWhiteSpace(skuCode) &&
				string.IsNullOrWhiteSpace(uom) &&
				row.Cell(headers["InvoiceDate"]).IsEmpty() &&
				(!hasQuantityColumn || row.Cell(quantityColumn).IsEmpty()) &&
				(!hasCaseQuantityColumn || row.Cell(caseQuantityColumn).IsEmpty()) &&
				(!hasDozenQuantityColumn || row.Cell(dozenQuantityColumn).IsEmpty()) &&
				(!hasPieceQuantityColumn || row.Cell(pieceQuantityColumn).IsEmpty()) &&
				(netAmountCell is null || netAmountCell.IsEmpty()))
			{
				continue;
			}

			DateOnly invoiceDate = default;
			var emittedRows = new List<ImportedInvoiceRow>();

			void AddRowError(string message, string? columnName = null)
			{
				result.AddError(rowNumber, invoiceCode, message, columnName);
				if (columnName == "CustomerCode")
				{
					var customerValue = string.IsNullOrWhiteSpace(customerCode) ? customerName : customerCode;
					var combinedCustomerValue = BuildCustomerDisplayValue(customerCode, customerName);
					result.Issues[^1] = result.Issues[^1] with { CustomerValue = combinedCustomerValue };
				}
				else if (columnName == "CustomerName")
				{
					var combinedCustomerValue = BuildCustomerDisplayValue(customerCode, customerName);
					result.Issues[^1] = result.Issues[^1] with { CustomerValue = combinedCustomerValue };
				}
				else if (columnName == "SkuCode")
				{
					result.Issues[^1] = result.Issues[^1] with { SkuValue = skuCode };
				}
				rowHasErrors = true;
			}

			if (string.IsNullOrWhiteSpace(invoiceCode))
			{
				AddRowError("Invoice code is required.", "InvoiceCode");
			}

			if (row.Cell(headers["InvoiceDate"]).IsEmpty())
			{
				AddRowError("Invoice date is required.", "InvoiceDate");
			}
			else if (!TryGetDateOnly(row.Cell(headers["InvoiceDate"]), out invoiceDate))
			{
				AddRowError("Invoice date is invalid.", "InvoiceDate");
			}

			if (string.IsNullOrWhiteSpace(customerCode))
			{
				if (hasCustomerNameColumn && !string.IsNullOrWhiteSpace(customerName) && TryResolveCustomer(customerName, customerByCode, customerByName, out var customer))
				{
					// Resolved using the alternate customer-name column.
				}
				else
				{
					AddRowError("Customer name or code is required.", string.IsNullOrWhiteSpace(customerName) ? "CustomerCode" : "CustomerName");
				}
			}
			else if (!TryResolveCustomer(customerCode, customerByCode, customerByName, out var customer))
			{
				if (hasCustomerNameColumn && !string.IsNullOrWhiteSpace(customerName) && TryResolveCustomer(customerName, customerByCode, customerByName, out customer))
				{
					// Resolved using the alternate customer-name column.
				}
				else
				{
					var customerLabel = BuildCustomerDisplayValue(customerCode, customerName);
					var message = string.IsNullOrWhiteSpace(customerName)
						? $"Customer code or name '{customerCode}' was not found."
						: $"Customer code/name '{customerLabel}' was not found.";
					AddRowError(message, string.IsNullOrWhiteSpace(customerName) ? "CustomerCode" : "CustomerName");
				}
			}
			else
			{
				if (!string.IsNullOrWhiteSpace(orderType))
				{
					if (!TryParseOrderType(orderType, out normalizedOrderType))
					{
						if (hasNetAmountColumn)
						{
							if (netAmountCell is null || netAmountCell.IsEmpty())
							{
								AddRowError($"Order type '{orderType}' is invalid and Net/Gross amount is missing.", "OrderType");
							}
							else if (!TryGetDecimal(netAmountCell, out var amountValue))
							{
								AddRowError($"Order type '{orderType}' is invalid and Net/Gross amount must be a valid number.", "OrderType");
							}
							else
							{
								normalizedOrderType = amountValue < 0 ? "Credit" : "Invoice";
							}
						}
						else
						{
							AddRowError($"Order type '{orderType}' is invalid. Use Invoice or Credit.", "OrderType");
						}
					}
				}
				else if (hasNetAmountColumn)
				{
					if (netAmountCell is null || netAmountCell.IsEmpty())
					{
						AddRowError("Net amount is required when OrderType is empty.", "NetAmount");
					}
					else if (!TryGetDecimal(netAmountCell, out var amountValue))
					{
						AddRowError("Net amount must be a valid number.", "NetAmount");
					}
					else if (amountValue < 0)
					{
						normalizedOrderType = "Credit";
					}
					else
					{
						normalizedOrderType = "Invoice";
					}
				}
				else
				{
					if (string.IsNullOrWhiteSpace(normalizedOrderType))
					{
						normalizedOrderType = "Invoice";
					}
				}
			}

			if (string.IsNullOrWhiteSpace(skuCode))
			{
				if (hasItemNameColumn && !string.IsNullOrWhiteSpace(itemName))
				{
					// Try to resolve by ItemName
					var normalizedItemName = Normalize(itemName);
					if (!subdItemByCode.ContainsKey(normalizedItemName))
					{
						AddRowError($"Item name '{itemName}' was not found.", "ItemName");
					}
					// If found, keep itemName and leave skuCode empty (will map correctly)
				}
				else
				{
					AddRowError("SKU code or Item name is required.", hasItemNameColumn ? "ItemName" : "SkuCode");
				}
			}
			else if (!subdItemByCode.ContainsKey(Normalize(skuCode)))
			{
				// Try fallback to ItemName if available
				if (hasItemNameColumn && !string.IsNullOrWhiteSpace(itemName))
				{
					var normalizedItemName = Normalize(itemName);
					if (!subdItemByCode.ContainsKey(normalizedItemName))
					{
						AddRowError($"SKU code '{skuCode}' and item name '{itemName}' were not found.", "SkuCode");
					}
					// If itemName found, will use itemName instead
				}
				else
				{
					AddRowError($"SKU code '{skuCode}' was not found.", "SkuCode");
				}
			}

			if (!useSplitQuantities)
			{
				if (string.IsNullOrWhiteSpace(uom))
				{
					uom = "pc";
				}

				if (row.Cell(quantityColumn).IsEmpty())
				{
					AddRowError("Quantity is required.", "Quantity");
				}
				else if (!TryGetInt(row.Cell(quantityColumn), out var quantity))
				{
					AddRowError("Quantity must be a whole number.", "Quantity");
				}
				else
				{
					if (quantity == 0)
					{
						continue;
					}

					if (string.IsNullOrWhiteSpace(normalizedOrderType) && quantity < 0)
					{
						normalizedOrderType = "Credit";
					}

					if (!rowHasErrors)
					{
						emittedRows.Add(new ImportedInvoiceRow(
							rowNumber,
							invoiceCode,
							invoiceDate,
							customerCode,
							customerName,
							normalizedOrderType,
							salesManName,
							skuCode,
							itemName,
							uom,
							quantity));
					}
				}
			}
			else
			{
				if (hasCaseQuantityColumn && !row.Cell(caseQuantityColumn).IsEmpty())
				{
					if (!TryGetInt(row.Cell(caseQuantityColumn), out var caseQuantity))
					{
						AddRowError("Case quantity must be a whole number.", "CaseQuantity");
					}
					else if (caseQuantity != 0)
					{
						if (string.IsNullOrWhiteSpace(normalizedOrderType) && caseQuantity < 0)
						{
							normalizedOrderType = "Credit";
						}
						emittedRows.Add(new ImportedInvoiceRow(
							rowNumber,
							invoiceCode,
							invoiceDate,
							customerCode,
							customerName,
							normalizedOrderType,
							salesManName,
							skuCode,
							itemName,
							NormalizeUomName("case"),
							caseQuantity));
					}
				}

				if (hasDozenQuantityColumn && !row.Cell(dozenQuantityColumn).IsEmpty())
				{
					if (!TryGetInt(row.Cell(dozenQuantityColumn), out var dozenQuantity))
					{
						AddRowError("Dozen quantity must be a whole number.", "DozenQuantity");
					}
					else if (dozenQuantity != 0)
					{
						if (string.IsNullOrWhiteSpace(normalizedOrderType) && dozenQuantity < 0)
						{
							normalizedOrderType = "Credit";
						}
						emittedRows.Add(new ImportedInvoiceRow(
							rowNumber,
							invoiceCode,
							invoiceDate,
							customerCode,
							customerName,
							normalizedOrderType,
							salesManName,
							skuCode,
							itemName,
							NormalizeUomName("dozen"),
							dozenQuantity));
					}
				}

				if (hasPieceQuantityColumn && !row.Cell(pieceQuantityColumn).IsEmpty())
				{
					if (!TryGetInt(row.Cell(pieceQuantityColumn), out var pieceQuantity))
					{
						AddRowError("Piece quantity must be a whole number.", "PieceQuantity");
					}
					else if (pieceQuantity != 0)
					{
						if (string.IsNullOrWhiteSpace(normalizedOrderType) && pieceQuantity < 0)
						{
							normalizedOrderType = "Credit";
						}
						emittedRows.Add(new ImportedInvoiceRow(
							rowNumber,
							invoiceCode,
							invoiceDate,
							customerCode,
							customerName,
							normalizedOrderType,
							salesManName,
							skuCode,
							itemName,
							NormalizeUomName("piece"),
							pieceQuantity));
					}
				}

				if (emittedRows.Count == 0)
				{
					AddRowError("Case or piece quantity is required.", hasCaseQuantityColumn ? "CaseQuantity" : "PieceQuantity");
				}
			}

			if (rowHasErrors)
			{
				continue;
			}

			rows.AddRange(emittedRows);
		}

		return rows;
	}
	private static int DetectHeaderRow(IXLWorksheet worksheet, SubDistributor subDistributor)
	{
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
		var scanLimit = Math.Min(lastRow, MaxHeaderScanRows);

		var templateAliases = SubdTemplateHeaders.GetTemplateAliases(subDistributor);
		var globalAliases = SubdTemplateHeaders.GetGlobalAliases();

		var requiredGroups = new[]
		{
			new[] { "InvoiceCode" },
			new[] { "InvoiceDate" },
			new[] { "SalesManName" },
			new[] { "CustomerCode", "CustomerName" },
			new[] { "SkuCode", "ItemName" }
		};

		var candidates = new List<(int RowNumber, int Score, int TemplateMatches, int RequiredFieldsCount)>();

		for (var rowNumber = 1; rowNumber <= scanLimit; rowNumber++)
		{
			var row = worksheet.Row(rowNumber);

			// Filter out noisy rows
			if (IsRowLikelyEmpty(row) || IsRowLikelyTitle(row))
				continue;

			// Gate 1: Check required groups
			var foundCanonicalKeys = new HashSet<string>();
			var templateMatches = 0;
			var globalMatches = 0;

			foreach (var cell in row.CellsUsed())
			{
				var normalized = NormalizeHeader(cell.GetString());
				if (string.IsNullOrWhiteSpace(normalized))
					continue;

				// Check template aliases first (highest priority)
				foreach (var kvp in templateAliases)
				{
					foreach (var alias in kvp.Value)
					{
						if (NormalizeHeader(alias) == normalized)
						{
							foundCanonicalKeys.Add(kvp.Key);
							templateMatches++;
							break;
						}
					}
				}

				// Check global aliases (fallback)
				if (!foundCanonicalKeys.Any(k => k == "InvoiceCode"))
				{
					foreach (var kvp in globalAliases)
					{
						foreach (var alias in kvp.Value)
						{
							if (NormalizeHeader(alias) == normalized)
							{
								foundCanonicalKeys.Add(kvp.Key);
								globalMatches++;
								break;
							}
						}
					}
				}
			}

			// Gate: Check all required groups present
			var satisfiesAllGroups = true;
			var requiredFieldsFound = 0;

			foreach (var group in requiredGroups)
			{
				var hasGroupMember = group.Any(key => foundCanonicalKeys.Contains(key));
				if (!hasGroupMember)
				{
					satisfiesAllGroups = false;
					break;
				}
				if (hasGroupMember) requiredFieldsFound++;
			}

			if (!satisfiesAllGroups)
				continue;

			// Score calculation
			var score = (templateMatches * 100) + (globalMatches * 10) - 1;
			candidates.Add((rowNumber, score, templateMatches, requiredFieldsFound));
		}

		if (candidates.Count == 0)
			return 0;

		// Select highest score; tie-break by: more template matches, more required fields, earlier row
		var best = candidates
			.OrderByDescending(c => c.Score)
			.ThenByDescending(c => c.TemplateMatches)
			.ThenByDescending(c => c.RequiredFieldsCount)
			.ThenBy(c => c.RowNumber)
			.First();

		return best.RowNumber;
	}

	private static IReadOnlyDictionary<string, int> BuildHeaderMap(IXLWorksheet worksheet, int headerRowNumber, SubDistributor subDistributor)
	{
		var headerRow = worksheet.Row(headerRowNumber);
		var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		var templateAliases = SubdTemplateHeaders.GetTemplateAliases(subDistributor);
		var globalAliases = SubdTemplateHeaders.GetGlobalAliases();

		// Single-write rule: template > detected > global
		var assignedCanonicalKeys = new HashSet<string>();

		foreach (var cell in headerRow.CellsUsed())
		{
			var rawHeader = cell.GetString();
			if (string.IsNullOrWhiteSpace(rawHeader))
				continue;

			var normalized = NormalizeHeader(rawHeader);
			if (string.IsNullOrWhiteSpace(normalized))
				continue;

			// Priority 1: Check template aliases
			foreach (var kvp in templateAliases)
			{
				if (assignedCanonicalKeys.Contains(kvp.Key))
					continue;

				foreach (var templateAlias in kvp.Value)
				{
					if (NormalizeHeader(templateAlias) == normalized)
					{
						headers[kvp.Key] = cell.Address.ColumnNumber;
						assignedCanonicalKeys.Add(kvp.Key);
						goto NextCell;
					}
				}
			}

			// Priority 2: Check global aliases
			foreach (var kvp in globalAliases)
			{
				if (assignedCanonicalKeys.Contains(kvp.Key))
					continue;

				foreach (var globalAlias in kvp.Value)
				{
					if (NormalizeHeader(globalAlias) == normalized)
					{
						headers[kvp.Key] = cell.Address.ColumnNumber;
						assignedCanonicalKeys.Add(kvp.Key);
						goto NextCell;
					}
				}
			}

		NextCell:;
		}

		return headers;
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

	private static bool TryGetDecimal(IXLCell cell, out decimal value)
	{
		if (cell.DataType == XLDataType.Number)
		{
			value = Convert.ToDecimal(cell.GetDouble(), CultureInfo.InvariantCulture);
			return true;
		}

		var text = cell.GetString().Trim();
		if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		return decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses, CultureInfo.CurrentCulture, out value);
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

		if (string.IsNullOrWhiteSpace(text))
		{
			date = default;
			return false;
		}

		// Try DateOnly parsing first
		if (DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date))
		{
			return true;
		}

		if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
		{
			return true;
		}

		// Try flexible DateTime parsing with both cultures
		if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out dateTime))
		{
			date = DateOnly.FromDateTime(dateTime);
			return true;
		}

		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dateTime))
		{
			date = DateOnly.FromDateTime(dateTime);
			return true;
		}

		// Try several common explicit formats
		var formats = new[]
		{
			"M/d/yyyy",
			"M/d/yy",
			"MM/dd/yyyy",
			"dd/MM/yyyy",
			"d/M/yyyy",
			"yyyy-MM-dd",
			"dd-MMM-yyyy",
			"dd MMM yyyy",
			"MMM dd, yyyy"
		};

		if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dateTime))
		{
			date = DateOnly.FromDateTime(dateTime);
			return true;
		}

		if (DateTime.TryParseExact(text, formats, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out dateTime))
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

	private static string NormalizeCustomerLookup(string value)
	{
		return Normalize(value)
			.Replace("'", string.Empty)
			.Replace("`", string.Empty)
			.Replace("’", string.Empty);
	}

	private static string NormalizeHeader(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;

		return System.Text.RegularExpressions.Regex.Replace(
			value.Trim().ToLowerInvariant(),
			@"[\s\.\#\/\-\,\:\(\)]+",
			" ").Trim();
	}

	private static bool IsRowLikelyEmpty(IXLRow row)
	{
		var usedCells = row.CellsUsed().Count();
		var totalCells = row.LastCellUsed()?.Address.ColumnNumber ?? 1;
		return usedCells < (totalCells * 0.3);
	}

	private static bool IsRowLikelyTitle(IXLRow row)
	{
		var cellCount = row.CellsUsed().Count();
		if (cellCount > 3) return false;

		var allText = string.Join(" ", row.CellsUsed().Select(c => c.GetString().Trim()));
		return allText.Length < 15 || allText.ToLowerInvariant().Contains("report");
	}

	private static string BuildCustomerDisplayValue(string? customerCode, string? customerName)
	{
		var code = string.IsNullOrWhiteSpace(customerCode) ? string.Empty : customerCode.Trim();
		var name = string.IsNullOrWhiteSpace(customerName) ? string.Empty : customerName.Trim();

		if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name))
		{
			return $"{code} - {name}";
		}

		return !string.IsNullOrWhiteSpace(code) ? code : name;
	}

	private static Dictionary<string, T> BuildLookupDictionary<T>(
		IEnumerable<T> source,
		Func<T, string> keySelector,
		Func<string, string> normalizeKey)
	{
		var lookup = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

		foreach (var item in source)
		{
			var rawKey = keySelector(item);
			var key = normalizeKey(rawKey);

			if (string.IsNullOrWhiteSpace(key) || key == "#n/a")
			{
				continue;
			}

			lookup.TryAdd(key, item);
		}

		return lookup;
	}




}