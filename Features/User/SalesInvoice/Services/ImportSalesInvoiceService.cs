using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.SalesInvoice.Validators;
using STTproject.Models;
using STTproject.Services;

namespace STTproject.Features.User.SalesInvoice.Services;

public sealed class ImportSalesInvoiceService
{
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

		using var workbook = new XLWorkbook(excelStream);
		var worksheet = workbook.Worksheets
			.FirstOrDefault(IsSalesInvoiceWorksheet)
			?? workbook.Worksheets.First();

		var headers = BuildHeaderMap(worksheet);
		var hasSplitQuantityHeaders = headers.ContainsKey("CaseQuantity") || headers.ContainsKey("DozenQuantity") || headers.ContainsKey("PieceQuantity");
		var requiredHeaders = new[]
		{
			"InvoiceCode",
			"InvoiceDate",
			"CustomerCode",
			"SkuCode"
		};

		var missingHeaders = requiredHeaders
			.Where(header => !headers.ContainsKey(header))
			.ToList();

		if (missingHeaders.Count > 0)
		{
			result.AddError(0, string.Empty, $"Missing required column(s): {string.Join(", ", missingHeaders)}.");
			return result;
		}

		if (!hasSplitQuantityHeaders)
		{
			if (!headers.ContainsKey("UOM") || !headers.ContainsKey("Quantity"))
			{
				result.AddError(0, string.Empty, "Missing required column(s): UOM and Quantity, or provide case/piece quantity columns.");
				return result;
			}
		}
		else if (!headers.ContainsKey("CaseQuantity") && !headers.ContainsKey("DozenQuantity") && !headers.ContainsKey("PieceQuantity"))
		{
			result.AddError(0, string.Empty, "Missing required column(s): case/dozen/piece quantity columns.");
			return result;
		}

		// NetAmount/Gross are optional; OrderType may be provided per-row or inferred from signed amounts where available.

		await using var context = _contextFactory.CreateDbContext();
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

		var customerByCode = customers.ToDictionary(customer => NormalizeCustomerLookup(customer.CustomerCode));
		var customerByName = customers
			.Where(customer => !string.IsNullOrWhiteSpace(customer.CustomerName))
			.GroupBy(customer => NormalizeCustomerLookup(customer.CustomerName))
			.ToDictionary(group => group.Key, group => group.First());
		var subdItemByCode = subdItems.ToDictionary(item => Normalize(item.SubdItemCode));
		var uomLookup = uoms
			.GroupBy(uom => (uom.SubdItemId, Normalize(uom.UomName)))
			.ToDictionary(group => group.Key, group => group.First());

		var parsedRows = ReadRows(worksheet, headers, result, (IReadOnlyDictionary<string, STTproject.Data.Customer>)customerByCode, customerByName, subdItemByCode);
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
				var groupConsistencyError = ValidateGroupConsistency(invoiceRows);
				if (!string.IsNullOrWhiteSpace(groupConsistencyError))
				{
					result.AddError(invoiceRows[0].RowNumber, invoiceNumber, groupConsistencyError);
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(invoiceRows[0].RowNumber, invoiceNumber, groupConsistencyError, null, invoiceRows[0].CustomerCode));
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				var firstRow = invoiceRows[0];

				if (!TryResolveCustomer(firstRow.CustomerCode, customerByCode, customerByName, out var customer))
				{
					var msg = $"Customer code or name '{firstRow.CustomerCode}' was not found.";
					result.AddError(firstRow.RowNumber, invoiceNumber, msg, "CustomerCode");
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(firstRow.RowNumber, invoiceNumber, msg, "CustomerCode", firstRow.CustomerCode));
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				if (!TryParseOrderType(firstRow.OrderType, out var orderType))
				{
					var msg = $"Order type '{firstRow.OrderType}' is invalid. Use Invoice or Credit.";
					result.AddError(firstRow.RowNumber, invoiceNumber, msg, "OrderType");
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(firstRow.RowNumber, invoiceNumber, msg, "OrderType", firstRow.CustomerCode));
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
					if (!subdItemByCode.TryGetValue(Normalize(row.SkuCode), out var subdItem))
					{
						var msg = $"SKU code '{row.SkuCode}' was not found.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, "SkuCode", row.CustomerCode));
						result.AddError(row.RowNumber, invoiceNumber, msg, "SkuCode");
						items.Clear();
						break;
					}

					if (!TryResolveUom(subdItem.SubdItemId, row.UOM, uomLookup, out var uom))
					{
						var msg = $"UOM '{row.UOM}' was not found for SKU '{row.SkuCode}'.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, "UOM", row.CustomerCode));
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

	private static bool IsSalesInvoiceWorksheet(IXLWorksheet worksheet)
	{
		return BuildHeaderMap(worksheet).ContainsKey("InvoiceCode");
	}

	private static List<ImportedInvoiceRow> ReadRows(
		IXLWorksheet worksheet,
		IReadOnlyDictionary<string, int> headers,
		ImportSalesInvoiceResult result,
		IReadOnlyDictionary<string, STTproject.Data.Customer> customerByCode,
		IReadOnlyDictionary<string, STTproject.Data.Customer> customerByName,
		IReadOnlyDictionary<string, SubdItem> subdItemByCode)
	{
		var rows = new List<ImportedInvoiceRow>();
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
		var hasUomColumn = headers.TryGetValue("UOM", out var uomColumn);
		var hasQuantityColumn = headers.TryGetValue("Quantity", out var quantityColumn);
		var hasCaseQuantityColumn = headers.TryGetValue("CaseQuantity", out var caseQuantityColumn);
		var hasDozenQuantityColumn = headers.TryGetValue("DozenQuantity", out var dozenQuantityColumn);
		var hasPieceQuantityColumn = headers.TryGetValue("PieceQuantity", out var pieceQuantityColumn);
		var useSplitQuantities = hasCaseQuantityColumn || hasDozenQuantityColumn || hasPieceQuantityColumn;

		for (int rowNumber = 2; rowNumber <= lastRow; rowNumber++)
		{
			var row = worksheet.Row(rowNumber);
			var invoiceCode = GetString(row, headers["InvoiceCode"]);
			var customerCode = GetString(row, headers["CustomerCode"]);
			var hasOrderTypeColumn = headers.TryGetValue("OrderType", out var orderTypeColumn);
			var hasNetAmountColumn = headers.TryGetValue("NetAmount", out var netAmountColumn);
			var orderType = hasOrderTypeColumn ? GetString(row, orderTypeColumn) : string.Empty;
			var skuCode = GetString(row, headers["SkuCode"]);
			var uom = hasUomColumn ? GetString(row, uomColumn) : string.Empty;
			var salesManName = headers.TryGetValue("SalesManName", out var salesManCol) ? GetString(row, salesManCol) : string.Empty;
			var netAmountCell = hasNetAmountColumn ? row.Cell(netAmountColumn) : null;

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

			var rowHasErrors = false;
			DateOnly invoiceDate = default;
			string normalizedOrderType = string.Empty;
			var emittedRows = new List<ImportedInvoiceRow>();

			void AddRowError(string message, string? columnName = null)
			{
				result.AddError(rowNumber, invoiceCode, message, columnName);
				if (columnName == "CustomerCode")
				{
					result.Issues[^1] = result.Issues[^1] with { CustomerValue = customerCode };
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
				AddRowError("Customer name or code is required.", "CustomerCode");
			}
			else if (!TryResolveCustomer(customerCode, customerByCode, customerByName, out var customer))
			{
				AddRowError($"Customer code or name '{customerCode}' was not found.", "CustomerCode");
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
					// No explicit OrderType and no NetAmount column to infer from — warn but default to Invoice.
					result.Issues.Add(new ImportSalesInvoiceIssue(rowNumber, invoiceCode, "Order type missing and NetAmount column not present; defaulting to Invoice.", "OrderType", customerCode));
					normalizedOrderType = "Invoice";
				}
			}

			if (string.IsNullOrWhiteSpace(skuCode))
			{
				AddRowError("SKU code is required.", "SkuCode");
			}
			else if (!subdItemByCode.ContainsKey(Normalize(skuCode)))
			{
				AddRowError($"SKU code '{skuCode}' was not found.", "SkuCode");
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
							normalizedOrderType,
							salesManName,
							skuCode,
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
							normalizedOrderType,
							salesManName,
							skuCode,
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
							normalizedOrderType,
							salesManName,
							skuCode,
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
							normalizedOrderType,
							salesManName,
							skuCode,
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

	private static IReadOnlyDictionary<string, int> BuildHeaderMap(IXLWorksheet worksheet)
	{
		var headerRow = worksheet.Row(1);
		var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		int? unitHeaderColumn = null;
		int? caseQuantityHeaderColumn = null;
		int? pieceQuantityHeaderColumn = null;

		foreach (var cell in headerRow.CellsUsed())
		{
			var header = Normalize(cell.GetString());
			if (string.IsNullOrWhiteSpace(header))
			{
				continue;
			}

			if (header is "num" or "invoicecode" or "lst_si" or "invoice number" or "salesinvoicecode" or "invoice no" or "inv nbr" or "po number" or "invoice#" or "invoice #" or "invoice no." or "invoice" or "inv")
			{
				headers.TryAdd("InvoiceCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "invoicedate" or "salesinvoicedate" or "invoice date" or "inv date" or "invoicedt" or "lst_date" or "si_date" or "date" or "p.o date" or "inv date" or "date-no." or "d_date")
			{
				headers.TryAdd("InvoiceDate", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "customercode" or "acc name" or "cust./supp." or "customer code" or "lst_cust1" or "so" or "so number" or "custid" or "customer_code" or "cust. #" or "customer/project: id")
			{
				headers.TryAdd("CustomerCode", cell.Address.ColumnNumber);
				continue;
			}



			if (header is "ordertype" or "order type" or "type" or "trx type" or "trxtype")
			{
				headers.TryAdd("OrderType", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "lst_net2" or "net" or "gross")
			{
				headers.TryAdd("NetAmount", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "skucode" or "item id" or "subditemcode" or "lst_head1" or "item_code" or "item code" or "item no." or "skuid" or "item number" or "stock_no" or "padsa item code" or "code" or "item_number")
			{
				headers.TryAdd("SkuCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "uom" or "unitofmeasure" or "unit" or "um" or "u/m" or "pckg" or "stock unit: units")
			{
				if (header is "unit")
				{
					unitHeaderColumn ??= cell.Address.ColumnNumber;
				}
				else
				{
					headers.TryAdd("UOM", cell.Address.ColumnNumber);
				}
				continue;
			}

			if (header is "case_total")
			{
				headers.TryAdd("CaseQuantity", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "dozen_total")
			{
				headers.TryAdd("DozenQuantity", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "pieces_total")
			{
				headers.TryAdd("PieceQuantity", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "lst_qnty1" or "case" or "cs")
			{
				caseQuantityHeaderColumn ??= cell.Address.ColumnNumber;
				continue;
			}

			if (header is "lst_qnty2" or "pc" or "pcs" or "piece")
			{
				pieceQuantityHeaderColumn ??= cell.Address.ColumnNumber;
				continue;
			}

			if (header is "quantity" or "lst_qnty" or "item_qty_piece" or "qty" or "um_qty" or "qty. sold" or "qty_received")
			{
				headers.TryAdd("Quantity", cell.Address.ColumnNumber);
			}

			if (header is "salesman" or "ads" or "salesmanname" or "salesman name" or "sales man" or "lst_agent2" or "salesrep" or "agent" or "sales person" or "pic name" or "sales rep" or "sales_man" or "salesman_name" or "sales rep (employee): branch")
			{
				headers.TryAdd("SalesManName", cell.Address.ColumnNumber);
			}
		}

		if (caseQuantityHeaderColumn.HasValue)
		{
			headers.TryAdd("CaseQuantity", caseQuantityHeaderColumn.Value);
		}

		if (pieceQuantityHeaderColumn.HasValue)
		{
			headers.TryAdd("PieceQuantity", pieceQuantityHeaderColumn.Value);
		}

		if (unitHeaderColumn.HasValue)
		{
			if (caseQuantityHeaderColumn.HasValue)
			{
				headers.TryAdd("PieceQuantity", unitHeaderColumn.Value);
			}
			else
			{
				headers.TryAdd("UOM", unitHeaderColumn.Value);
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

		if (rows.Select(row => Normalize(row.OrderType)).Distinct().Count() > 1)
		{
			return "Order type values must be the same for all rows in the same invoice.";
		}

		return string.Empty;
	}

	private static bool TryParseOrderType(string orderType, out string normalizedOrderType)
	{
		if (string.Equals(orderType, "Invoice", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(orderType, "Order", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(orderType, "Sales", StringComparison.OrdinalIgnoreCase))
		{
			normalizedOrderType = "Invoice";
			return true;
		}

		if (string.Equals(orderType, "Credit", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(orderType, "Returns", StringComparison.OrdinalIgnoreCase))
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

	private static bool TryResolveUom(
		int subdItemId,
		string value,
		IReadOnlyDictionary<(int SubdItemId, string UomName), ItemsUom> uomLookup,
		out ItemsUom uom)
	{
		foreach (var candidate in GetUomLookupCandidates(value))
		{
			if (uomLookup.TryGetValue((subdItemId, Normalize(candidate)), out var foundUom))
			{
				uom = foundUom;
				return true;
			}
		}

		uom = null!;
		return false;
	}

	private static IEnumerable<string> GetUomLookupCandidates(string value)
	{
		var rawValue = value.Trim();
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			yield break;
		}

		foreach (var candidate in GetUomSynonyms(rawValue))
		{
			yield return candidate;
		}
	}

	private static IEnumerable<string> GetUomSynonyms(string value)
	{
		var normalized = Normalize(value);

		switch (normalized)
		{
			case "cs":
			case "case":
				yield return "case";
				yield return "cs";
				yield break;
			case "dz":
			case "dozen":
				yield return "dozen";
				yield return "dz";
				yield break;
			case "pc":
			case "pcs":
			case "piece":
				yield return "piece";
				yield return "pc";
				yield return "pcs";
				yield break;
			case "pckg":
			case "pck":
			case "pack":
			case "package":
				yield return "package";
				yield return "pckg";
				yield break;
			default:
				yield return value.Trim();
				yield break;
		}
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

	private static string NormalizeUomName(string value)
	{
		var normalized = Normalize(value);

		return normalized switch
		{
			"cs" or "case" => "Case",
			"dz" or "dozen" => "Dozen",
			"pc" or "pcs" or "piece" => "Piece",
			"pckg" or "package" => "Package",
			"pck" or "pack" => value.Trim(),
			_ => value.Trim()
		};
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

	private static bool TryResolveCustomer(
		string value,
		IReadOnlyDictionary<string, STTproject.Data.Customer> customerByCode,
		IReadOnlyDictionary<string, STTproject.Data.Customer> customerByName,
		out STTproject.Data.Customer customer)
	{
		var normalized = NormalizeCustomerLookup(value);
		if (customerByCode.TryGetValue(normalized, out customer!))
		{
			return true;
		}

		if (customerByName.TryGetValue(normalized, out customer!))
		{
			return true;
		}

		customer = null!;
		return false;
	}

	private sealed record ImportedInvoiceRow(
		int RowNumber,
		string InvoiceCode,
		DateOnly InvoiceDate,
		string CustomerCode,
		string OrderType,
		string SalesManName,
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

public sealed record ImportSalesInvoiceIssue(int RowNumber, string InvoiceNumber, string Message, string? ColumnName = null, string? CustomerValue = null);
