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
	private const int MinTemplateMatchThreshold = 6;
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

		// Basic validations before processing
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

		// Load the workbook and detect the header row and mapping for each worksheet, then select the best candidate worksheet to process.
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

		// Validate required headers and stop processing if critical headers are missing, since that will cause a large number of downstream errors.
		var (isValid, errorMessage) = InvoiceDataValidator.ValidateRequiredHeaders(headers);
		if (!isValid)
		{
			result.AddError(0, string.Empty, errorMessage);
			return result;
		}

		// Load necessary reference data for lookups and validations
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

		// Read and parse rows from the worksheet starting after the header row, performing validations and lookups to enrich the data, and collecting any issues found along the way.
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
		// If no rows were parsed, return early since there is nothing to prepare or commit, and this likely indicates an issue with the template or header detection that would cause a large number of downstream errors.
		if (parsedRows.Count == 0)
		{
			if (!result.HasIssues)
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
					SalesManName = firstRow.SalesManName,
					CustomerAddress = string.Join(", ", new[]
					{
						customer.AddressLine,
						customer.City,
						customer.Province,
						customer.ZipCode?.ToString()
					}.Where(s => !string.IsNullOrWhiteSpace(s)))
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
		// Reads and validates rows from the worksheet starting after the header row, returning a list of parsed invoice rows along with any issues found.
		var rows = new List<ImportedInvoiceRow>();
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

		// Customer Columns
		var hasCustomerCodeColumn = headers.ContainsKey("CustomerCode");
		var hasCustomerNameColumn = headers.TryGetValue("CustomerName", out var customerNameColumn);
		var hasCustomerTypeColumn = headers.TryGetValue("CustomerType", out var customerTypeColumn);

		// Address/location columns (used for customer disambiguation)
		var hasAddressLineColumn = headers.TryGetValue("AddressLine", out var addressLineColumn);
		var hasCityMunicipalityColumn = headers.TryGetValue("CityMunicipality", out var cityMunicipalityColumn);
		var hasProvinceColumn = headers.TryGetValue("Province", out var provinceColumn);

		// Item columns
		var hasSkuCodeColumn = headers.ContainsKey("SkuCode");
		var hasItemNameColumn = headers.TryGetValue("ItemName", out var itemNameColumn);

		// Quantity columns
		var hasQuantityColumn        = headers.TryGetValue("Quantity",         out var quantityColumn);
		var hasCaseQuantityColumn    = headers.TryGetValue("CaseQuantity",     out var caseQuantityColumn);
		var hasDozenQuantityColumn   = headers.TryGetValue("DozenQuantity",    out var dozenQuantityColumn);
		var hasPieceQuantityColumn   = headers.TryGetValue("PieceQuantity",    out var pieceQuantityColumn);
		var hasInBoxQuantityColumn   = headers.TryGetValue("InBoxQuantity",    out var inBoxQuantityColumn);
		var useSplitQuantities       = hasCaseQuantityColumn || hasDozenQuantityColumn || hasPieceQuantityColumn || hasInBoxQuantityColumn;

		// UOM column (required when using simple Quantity)
		var hasUomColumn             = headers.TryGetValue("UnitOfMeasure",              out var uomColumn);

		// Order columns
		var hasOrderTypeColumn       = headers.TryGetValue("OrderType",        out var orderTypeColumn);
		var hasNetAmountColumn       = headers.TryGetValue("NetAmount",        out var netAmountColumn);

		for (int rowNumber = headerRowNumber + 1; rowNumber <= lastRow; rowNumber++)
		{
			var row = worksheet.Row(rowNumber);

			// ── Read raw cell values ─────────────────────────────────────────────
			var invoiceCode  = GetString(row, headers["InvoiceCode"]);
			var customerCode = hasCustomerCodeColumn     ? GetString(row, headers["CustomerCode"])  : string.Empty;
			var customerName = hasCustomerNameColumn      ? GetString(row, customerNameColumn)       : string.Empty;
			var customerType = hasCustomerTypeColumn      ? GetString(row, customerTypeColumn)       : null;
			var province     = hasProvinceColumn          ? GetString(row, provinceColumn)           : null;
			var city         = hasCityMunicipalityColumn  ? GetString(row, cityMunicipalityColumn)   : null;
			var addressLine  = hasAddressLineColumn       ? GetString(row, addressLineColumn)        : null;
			var orderType    = hasOrderTypeColumn         ? GetString(row, orderTypeColumn)          : string.Empty;
			var skuCode      = hasSkuCodeColumn           ? GetString(row, headers["SkuCode"])       : string.Empty;
			var itemName     = hasItemNameColumn          ? GetString(row, itemNameColumn)           : string.Empty;
			var uom          = hasUomColumn               ? GetString(row, uomColumn)               : string.Empty;
			var salesManName = headers.TryGetValue("SalesManName", out var salesManCol) ? GetString(row, salesManCol) : string.Empty;
			var netAmountCell = hasNetAmountColumn        ? row.Cell(netAmountColumn)               : null;

		// ── Skip completely empty rows ───────────────────────────────────────
		if (string.IsNullOrWhiteSpace(invoiceCode) &&
			string.IsNullOrWhiteSpace(customerCode) &&
			string.IsNullOrWhiteSpace(customerName) &&
			string.IsNullOrWhiteSpace(orderType) &&
			string.IsNullOrWhiteSpace(skuCode) &&
			string.IsNullOrWhiteSpace(itemName) &&
			string.IsNullOrWhiteSpace(uom) &&
			IsCellEffectivelyEmpty(row.Cell(headers["InvoiceDate"])) &&
			(!hasQuantityColumn      || IsCellEffectivelyEmpty(row.Cell(quantityColumn))) &&
			(!hasCaseQuantityColumn  || IsCellEffectivelyEmpty(row.Cell(caseQuantityColumn))) &&
			(!hasDozenQuantityColumn || IsCellEffectivelyEmpty(row.Cell(dozenQuantityColumn))) &&
			(!hasPieceQuantityColumn || IsCellEffectivelyEmpty(row.Cell(pieceQuantityColumn))) &&
			(!hasInBoxQuantityColumn || IsCellEffectivelyEmpty(row.Cell(inBoxQuantityColumn))) &&
			(netAmountCell is null   || IsCellEffectivelyEmpty(netAmountCell)))
		{
			continue;
		}

			DateOnly invoiceDate  = default;
			var emittedRows       = new List<ImportedInvoiceRow>();
			var rowHasErrors      = false;

			// Local function to add an error for the current row and mark it as having errors, which will prevent it from being emitted.
			void AddRowError(string message, string? columnName = null)
			{
				result.AddError(rowNumber, invoiceCode, message, columnName);
				rowHasErrors = true;
			}

			//-------Validate required fields------------
			// Invoice code is required
			if (string.IsNullOrWhiteSpace(invoiceCode))
				AddRowError("Invoice code is required.", "InvoiceCode");
			// Invoice date is required and must be a valid date
			if (IsCellEffectivelyEmpty(row.Cell(headers["InvoiceDate"])))
				AddRowError("Invoice date is required.", "InvoiceDate");
			else if (!TryGetDateOnly(row.Cell(headers["InvoiceDate"]), out invoiceDate))
				AddRowError("Invoice date is invalid.", "InvoiceDate");

			//-------Validate customer-----------------
			if (!InvoiceDataValidator.TryResolveCustomer(
					customerCode, customerName,
					province, city, customerType, addressLine,
					customerByCode, customerByName, allCustomers,
					out var resolvedCustomer, out var customerSuggestions))
			{
				if (string.IsNullOrWhiteSpace(customerCode) && string.IsNullOrWhiteSpace(customerName))
					AddRowError("Customer code or name is required.", "CustomerCode");
				else
				{
					var label   = BuildCustomerDisplayValue(customerCode, customerName);
					var colName = string.IsNullOrWhiteSpace(customerName) ? "CustomerCode" : "CustomerName";
					var message = customerSuggestions is { Count: > 0 }
						? $"Customer '{label}' was not found. Did you mean: {string.Join(", ", customerSuggestions.Select(s => BuildCustomerDisplayValue(s.CustomerCode, s.CustomerName)))}?"
						: $"Customer '{label}' was not found.";
					AddRowError(message, colName);
				}
			}

			// ── Order Type Resolution ────────────────────────────────────────────
			decimal? netAmountValue = null;
			if (hasNetAmountColumn && netAmountCell != null && TryGetDecimal(netAmountCell, out var parsedNet))
				netAmountValue = parsedNet;

			string normalizedOrderType;

			if (!string.IsNullOrWhiteSpace(orderType))
			{
				// Explicit OrderType column — must be valid
				if (!InvoiceDataValidator.TryParseOrderType(orderType, out normalizedOrderType))
					AddRowError($"Order type '{orderType}' is invalid. Use Invoice or Credit.", "OrderType");
			}
			else if (netAmountValue.HasValue)
			{
				// Infer from NetAmount sign
				normalizedOrderType = netAmountValue.Value < 0 ? "Credit" : "Invoice";
			}
			else
			{
				// Leave empty — quantity sign will resolve it below in the quantity blocks
				normalizedOrderType = string.Empty;
			}

			// ── SKU / Item Resolution ────────────────────────────────────────────
			if (!InvoiceDataValidator.TryResolveItem(
					skuCode, itemName,
					subdItemByCode, allSubdItems,
					out var resolvedItem, out var itemSuggestions))
			{
				if (string.IsNullOrWhiteSpace(skuCode) && string.IsNullOrWhiteSpace(itemName))
				{
					AddRowError("SKU code or Item name is required.", hasItemNameColumn ? "ItemName" : "SkuCode");
				}
				else if (!string.IsNullOrWhiteSpace(skuCode) && !string.IsNullOrWhiteSpace(itemName))
				{
					var message = itemSuggestions is { Count: > 0 }
						? $"SKU code '{skuCode}' and item name '{itemName}' were not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => s.SubdItemCode))}?"
						: $"SKU code '{skuCode}' and item name '{itemName}' were not found.";
					AddRowError(message, "SkuCode");
				}
				else if (!string.IsNullOrWhiteSpace(skuCode))
				{
					var message = itemSuggestions is { Count: > 0 }
						? $"SKU code '{skuCode}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => s.SubdItemCode))}?"
						: $"SKU code '{skuCode}' was not found.";
					AddRowError(message, "SkuCode");
				}
				else
				{
					var message = itemSuggestions is { Count: > 0 }
						? $"Item name '{itemName}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => s.ItemName))}?"
						: $"Item name '{itemName}' was not found.";
					AddRowError(message, "ItemName");
				}
			}

			// ── Quantity & UOM ───────────────────────────────────────────────────

			int ResolveUomId(string uomString)
			{
				if (resolvedItem is null)
					return 0;
			
				foreach (var synonym in InvoiceDataValidator.GetUomSynonyms(uomString))
				{
					if (InvoiceDataValidator.TryResolveUom(
							resolvedItem.SubdItemId,
							synonym,
							uomLookup,
							out var matched) && matched is not null)
					{
						return matched.ItemsUomId;
					}
				}
			
				return 0;
			}

			if (!useSplitQuantities)
			{
				// Simple path — use TryResolveQuantity to normalize quantity + UOM together
				int? rawQuantity = null;
				if (!IsCellEffectivelyEmpty(row.Cell(quantityColumn)) && TryGetInt(row.Cell(quantityColumn), out var parsedQty))
					rawQuantity = parsedQty;
			
				if (!InvoiceDataValidator.TryResolveQuantity(
						rawQuantity,
						uom,
						caseQuantity: null,
						pieceQuantity: null,
						inBoxQuantity: null,
						dozenQuantity: null,
						out var resolvedQty,
						out var resolvedUom,
						out _))
				{
					if (IsCellEffectivelyEmpty(row.Cell(quantityColumn)))
						AddRowError("Quantity is required.", "Quantity");
					else if (rawQuantity is null)
						AddRowError("Quantity must be a whole number.", "Quantity");
					else if (InvoiceDataValidator.IsMissingUomValue(uom))
						AddRowError("UOM is required.", "UOM");
				}
				else
				{
					if (resolvedQty == 0)
						continue;
			
					// Quantity sign as final fallback for order type
					if (string.IsNullOrWhiteSpace(normalizedOrderType))
						normalizedOrderType = resolvedQty < 0 ? "Credit" : "Invoice";
			
					var resolvedUomId = ResolveUomId(resolvedUom);
					if (resolvedUomId == 0 && resolvedItem is not null)
						AddRowError($"UOM '{uom}' was not found for SKU '{skuCode}'.", "UOM");
			
					if (!rowHasErrors)
					{
						emittedRows.Add(new ImportedInvoiceRow(
							RowNumber:            rowNumber,
							InvoiceCode:          invoiceCode,
							InvoiceDate:          invoiceDate,
							CustomerCode:         customerCode,
							CustomerName:         customerName,
							OrderType:            normalizedOrderType,
							SalesManName:         salesManName,
							SkuCode:              skuCode,
							ItemName:             itemName,
							UOM:                  resolvedUom,
							Quantity:             resolvedQty,
							Province:             province,
							CityMunicipality:     city,
							CustomerType:         customerType,
							AddressLine:          addressLine,
							ResolvedCustomerId:   resolvedCustomer?.CustomerId ?? 0,
							ResolvedCustomerCode: resolvedCustomer?.CustomerCode ?? string.Empty,
							ResolvedCustomerName: resolvedCustomer?.CustomerName ?? string.Empty,
							ResolvedSubdItemId:   resolvedItem?.SubdItemId ?? 0,
							ResolvedSubdItemCode: resolvedItem?.SubdItemCode ?? string.Empty,
							ResolvedItemsUomId:   resolvedUomId));
					}
				}
			}
			else
			{
				// Split-quantity path: one ImportedInvoiceRow emitted per non-zero UOM column.
				// TryResolveQuantity is not used here because we intentionally emit one row
				// per UOM type rather than collapsing them into one.
				void TryEmitSplitRow(bool hasColumn, int columnIndex, string uomLabel, string errorField)
				{
					if (!hasColumn || IsCellEffectivelyEmpty(row.Cell(columnIndex)))
						return;
			
					// Use the same missing-value rule as the validator (handles "-", "–", "n/a")
					var rawValue = row.Cell(columnIndex).GetString().Trim();
					if (InvoiceDataValidator.IsMissingUomValue(rawValue))
						return;
			
					if (!TryGetInt(row.Cell(columnIndex), out var qty))
					{
						AddRowError($"{errorField} must be a whole number.", errorField);
						return;
					}
			
					if (qty == 0)
						return;
			
					var rowOrderType = string.IsNullOrWhiteSpace(normalizedOrderType)
						? (qty < 0 ? "Credit" : "Invoice")
						: normalizedOrderType;
			
					// Normalize the UOM label and resolve its ID via synonyms
					var normalizedUomName = InvoiceDataValidator.NormalizeUomName(uomLabel);
					var resolvedUomId     = ResolveUomId(normalizedUomName);
			
					if (resolvedUomId == 0 && resolvedItem is not null)
					{
						AddRowError($"UOM '{normalizedUomName}' was not found for SKU '{skuCode}'.", errorField);
						return;
					}
			
					emittedRows.Add(new ImportedInvoiceRow(
						RowNumber:            rowNumber,
						InvoiceCode:          invoiceCode,
						InvoiceDate:          invoiceDate,
						CustomerCode:         customerCode,
						CustomerName:         customerName,
						OrderType:            rowOrderType,
						SalesManName:         salesManName,
						SkuCode:              skuCode,
						ItemName:             itemName,
						UOM:                  normalizedUomName,
						Quantity:             qty,
						Province:             province,
						CityMunicipality:     city,
						CustomerType:         customerType,
						AddressLine:          addressLine,
						ResolvedCustomerId:   resolvedCustomer?.CustomerId ?? 0,
						ResolvedCustomerCode: resolvedCustomer?.CustomerCode ?? string.Empty,
						ResolvedCustomerName: resolvedCustomer?.CustomerName ?? string.Empty,
						ResolvedSubdItemId:   resolvedItem?.SubdItemId ?? 0,
						ResolvedSubdItemCode: resolvedItem?.SubdItemCode ?? string.Empty,
						ResolvedItemsUomId:   resolvedUomId));
				}
			
				TryEmitSplitRow(hasCaseQuantityColumn,  caseQuantityColumn,  "case",  "CaseQuantity");
				TryEmitSplitRow(hasDozenQuantityColumn, dozenQuantityColumn, "dozen", "DozenQuantity");
				TryEmitSplitRow(hasPieceQuantityColumn, pieceQuantityColumn, "piece", "PieceQuantity");
				TryEmitSplitRow(hasInBoxQuantityColumn, inBoxQuantityColumn, "inbox", "InBoxQuantity");
			
				if (emittedRows.Count == 0 && !rowHasErrors)
				{
					var errorCol = hasCaseQuantityColumn  ? "CaseQuantity"  :
								hasPieceQuantityColumn ? "PieceQuantity" :
								hasInBoxQuantityColumn ? "InBoxQuantity" : "DozenQuantity";
					AddRowError("At least one quantity column (Case, Dozen, Piece, or InBox) is required.", errorCol);
				}
			}
			if (rowHasErrors)
				continue;
		
			rows.AddRange(emittedRows);
		}
		
		return rows;
	}
	private static int DetectHeaderRow(IXLWorksheet worksheet, SubDistributor subDistributor)
	{
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
		var scanLimit = Math.Min(lastRow, MaxHeaderScanRows);

		// Pre-build a flat reverse lookup: normalized alias → canonical key
		// This makes cell matching O(1) instead of O(keys × aliases)
		var templateAliases = SubdTemplateHeaders.GetTemplateAliases(subDistributor);
		var globalAliases = SubdTemplateHeaders.GetGlobalAliases();

		var templateLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var globalLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var kvp in templateAliases)
			foreach (var alias in kvp.Value)
				templateLookup.TryAdd(NormalizeHeader(alias), kvp.Key);

		foreach (var kvp in globalAliases)
			foreach (var alias in kvp.Value)
				globalLookup.TryAdd(NormalizeHeader(alias), kvp.Key);

		var alwaysRequiredGroups = new[]
		{
			new[] { "InvoiceCode" },
			new[] { "InvoiceDate" },
			new[] { "SalesManName" },
			new[] { "CustomerCode", "CustomerName" },
			new[] { "SkuCode", "SkuName" }
		};

		// Read the entire scan range in ONE call — avoids per-row ClosedXML overhead
		var usedRange = worksheet.Range(1, 1, scanLimit, worksheet.LastColumnUsed()?.ColumnNumber() ?? 50);

		// Group cells by row number
		var cellsByRow = usedRange.Cells()
			.Where(c => !c.IsEmpty())
			.GroupBy(c => c.Address.RowNumber)
			.OrderBy(g => g.Key);

		foreach (var rowGroup in cellsByRow)
		{
			var rowNumber = rowGroup.Key;
			var foundCanonicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var templateMatches = 0;

			foreach (var cell in rowGroup)
			{
				var normalized = NormalizeHeader(cell.GetString());
				if (string.IsNullOrWhiteSpace(normalized)) continue;

				if (templateLookup.TryGetValue(normalized, out var templateKey))
				{
					if (foundCanonicalKeys.Add(templateKey))
						templateMatches++;
				}
				else if (globalLookup.TryGetValue(normalized, out var globalKey))
				{
					foundCanonicalKeys.Add(globalKey);
				}
			}

			// Early exit for high-confidence template match
			if (templateMatches >= MinTemplateMatchThreshold)
				return rowNumber;

			// Check required groups
			var satisfiesAllGroups = alwaysRequiredGroups
				.All(group => group.Any(key => foundCanonicalKeys.Contains(key)));

			if (!satisfiesAllGroups) continue;

			// Quantity group check
			bool hasSplitQuantity =
				foundCanonicalKeys.Contains("CaseQuantity") ||
				foundCanonicalKeys.Contains("PieceQuantity") ||
				foundCanonicalKeys.Contains("DozenQuantity") ||
				foundCanonicalKeys.Contains("InBoxQuantity");

			if (!hasSplitQuantity)
			{
				bool hasUom = foundCanonicalKeys.Contains("UnitOfMeasure");
				bool hasQty = foundCanonicalKeys.Contains("Quantity");
				if (!hasUom && !hasQty) continue;
			}

			// First row that satisfies all required groups wins (no more scoring needed)
			return rowNumber;
		}

		return 0;
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
		var cell = row.Cell(columnNumber);
		if (cell.HasFormula)
			return cell.CachedValue.ToString()?.Trim() ?? string.Empty;
		return cell.GetString().Trim();
	}

	private static bool TryGetInt(IXLCell cell, out int value)
	{
		// Resolve formula cached value first
		if (cell.HasFormula)
		{
			var cached = cell.CachedValue.ToString().Trim();
			if (int.TryParse(cached, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				return true;
			if (double.TryParse(cached, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
			{
				value = (int)d;
				return true;
			}
			value = 0;
			return false;
		}

		if (cell.DataType == XLDataType.Number)
		{
			value = (int)cell.GetDouble();
			return true;
		}

		if (int.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			return true;

		return int.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
	}


	private static bool TryGetDecimal(IXLCell cell, out decimal value)
	{
		if (cell.HasFormula)
		{
			var cached = cell.CachedValue.ToString().Trim();
			if (decimal.TryParse(cached, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out value))
				return true;
			value = 0;
			return false;
		}

		if (cell.DataType == XLDataType.Number)
		{
			value = Convert.ToDecimal(cell.GetDouble(), CultureInfo.InvariantCulture);
			return true;
		}

		var text = cell.GetString().Trim();
		if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out value))
			return true;

		return decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses, CultureInfo.CurrentCulture, out value);
	}

	private static bool TryGetDateOnly(IXLCell cell, out DateOnly date)
	{
		if (cell.HasFormula)
		{
			var cached = cell.CachedValue.ToString().Trim();
			// Cached date serials come back as numbers (e.g. "46163")
			if (double.TryParse(cached, NumberStyles.Number, CultureInfo.InvariantCulture, out var serial))
			{
				date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
				return true;
			}
			// Or as a date string
			if (DateTime.TryParse(cached, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
			{
				date = DateOnly.FromDateTime(dt);
				return true;
			}
			date = default;
			return false;
		}

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

	private static bool IsCellEffectivelyEmpty(IXLCell cell)
	{
		if (cell.HasFormula)
			return string.IsNullOrWhiteSpace(cell.CachedValue.ToString());
		return cell.IsEmpty();
	}




}