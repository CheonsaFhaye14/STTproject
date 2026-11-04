using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Features.User.SalesInvoice.Validators;
using STTproject.Models;
using STTproject.Services;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;

namespace STTproject.Features.User.SalesInvoice.Services;

public sealed class ImportSalesInvoiceService
{
	private readonly IDbContextFactory<EntrielContext> _contextFactory;
	private readonly ISalesInvoiceService _salesInvoiceService;
	private readonly ILogger<ImportSalesInvoiceService> _logger;
	private readonly IImportTemplateService _templates;

	public ImportSalesInvoiceService(
		IDbContextFactory<EntrielContext> contextFactory,
		ISalesInvoiceService salesInvoiceService,
		IImportTemplateService templates,
		ILogger<ImportSalesInvoiceService> logger)
	{
		_contextFactory = contextFactory;
		_salesInvoiceService = salesInvoiceService;
		_templates = templates;
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
			.Where(prepared => prepared.IsReadyToCommit)
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

	public Task<ImportSalesInvoiceResult> PrepareFromExcelAsync(
		Stream excelStream,
		int subDistributorId,
		int currentUserId,
		CancellationToken cancellationToken = default)
		=> PrepareFromExcelAsync(excelStream, subDistributorId, currentUserId, null, int.MaxValue, cancellationToken);

	public async Task<ImportSalesInvoiceResult> PrepareFromExcelAsync(
		Stream excelStream,
		int subDistributorId,
		int currentUserId,
		ImportTemplateEditDto? template,
		int maxRows,
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

		try
		{
			template ??= await _templates.ResolveForImportAsync("SalesInvoice", subDistributorId, cancellationToken);
		}
		catch (TemplateSelectionRequiredException ex)
		{
			// More than one template could apply and nothing has chosen one. Never guess: stop with a clear message.
			result.AddError(0, string.Empty, ex.Message);
			return result;
		}
		if (template is null)
		{
			result.AddError(0, string.Empty,
				"No active import template was found for this subdistributor and there is no global default. Ask an admin to set one up under Import Templates.");
			return result;
		}

		// The template decides which sheets, which header rows and which columns.
		using var workbook = new XLWorkbook(excelStream);

		var resolvedWorkbook = WorkbookResolver.Resolve(template, workbook);
		var plan = WorkbookReader.Plan(resolvedWorkbook, template.ImportType, maxRows);
		result.FileWarnings.AddRange(plan.Warnings);

		// Everything the sheets that did resolve can tell the results page, even if the workbook can't be read as a whole.
		result.OriginalHeaders = plan.Sheets
			.SelectMany(sheet => sheet.Columns!.Headers.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var sheet in plan.Sheets)
		{
			result.GetOrAddSheet(sheet.Inspection.SheetIndex, sheet.Inspection.Name).RawHeaders = Enumerable.Range(1, sheet.Inspection.LastCol)
				.Select(c => sheet.Inspection.HeaderText(sheet.HeaderRow!.Value, sheet.HeaderRowCount, c))
				.ToList();
		}

		// Stop here when the workbook can't be read: a missing sheet or column would otherwise cause a flood of row errors.
		if (plan.Rows is null)
		{
			foreach (var error in plan.Errors)
				result.AddError(0, string.Empty, error);
			result.SheetShortfalls.AddRange(plan.SheetShortfalls);
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

		var uoms = await (from u in context.ItemsUoms.AsNoTracking()
						  join i in context.SubdItems on u.SubdItemId equals i.SubdItemId
						  where i.SubDistributorId == subDistributorId && i.IsActive
						  select u).ToListAsync(cancellationToken);

		var customerByCode = BuildLookupDictionary(customers, customer => customer.CustomerCode, NormalizeCustomerLookup);
		var customerByName = BuildLookupDictionary(customers, customer => customer.CustomerName, NormalizeCustomerLookup);
		var subdItemsBySkuGroup = subdItems.ToLookup(item => Normalize(item.SubdItemCode ?? string.Empty));
		var subdItemById = subdItems.ToDictionary(item => item.SubdItemId);
		var uomLookup = uoms.ToLookup(uom => (uom.SubdItemId, Normalize(uom.UomName)));
		var uomsBySubdItemId = uoms.Where(u => u.IsActive).ToLookup(u => u.SubdItemId);
		var customerById = customers.ToDictionary(customer => customer.CustomerId);
		var itemsUomById = uoms.ToDictionary(uom => uom.ItemsUomId);

		// Pre-scan every sheet once to know which conversions each item actually uses,
		// so the fallback in ReadRows can restrict itself to a confirmed match instead of a guess.
		var knownConversionsBySubdItem = BuildKnownConversionsBySubdItem(
			plan.Rows, subdItemsBySkuGroup, subdItems, uomLookup);

		var parsedRows = ReadRows(
			plan.Rows,
			result,
			customerByCode,
			customerByName,
			subdItemsBySkuGroup,
			uomLookup,
			uomsBySubdItemId,
			knownConversionsBySubdItem,
			customers,
			subdItems);

		result.Rows.AddRange(parsedRows);

		if (parsedRows.Count == 0)
		{
			if (!result.HasIssues)
				result.AddError(0, string.Empty, "No invoice rows were found in the template.");
			return result;
		}

		foreach (var invoiceGroup in parsedRows.GroupBy(row =>
					string.Join("|",
						(row.InvoiceCode ?? string.Empty).Trim().ToUpperInvariant(),
						BuildCustomerGroupPart(row),
						(row.OrderType ?? string.Empty).Trim().ToUpperInvariant(),
						(row.SalesManName ?? string.Empty).Trim().ToUpperInvariant()),
					StringComparer.OrdinalIgnoreCase))
		{
			var invoiceRows = invoiceGroup.ToList();
			var invoiceNumber = invoiceRows.First().InvoiceCode.Trim();

			var preparedInvoice = new PreparedInvoice
			{
				SubDistributor = subDistributor.SubdCode + " - " + subDistributor.SubdName,
				InvoiceNumber = invoiceNumber,
				GroupKey = invoiceGroup.Key,
				OrderTypeKey = (invoiceRows[0].OrderType ?? string.Empty).Trim().ToUpperInvariant(),
				SalesManKey = (invoiceRows[0].SalesManName ?? string.Empty).Trim().ToUpperInvariant(),
				Items = new List<InputItemModel>(),
				Issues = new List<ImportSalesInvoiceIssue>(),
				Selected = true
			};

			try
			{
				var blockingRows = FindBlockingRows(result.ErroredRows, invoiceNumber, invoiceRows[0]);
				if (blockingRows.Count > 0)
				{
					var holdFirst = blockingRows.OrderBy(b => b.SheetIndex).ThenBy(b => b.RowNumber).First();
					var holdIssue = new ImportSalesInvoiceIssue(
						holdFirst.RowNumber,
						invoiceNumber,
						BuildHoldMessage(blockingRows, result),
						string.Empty,
						string.Empty,
						GroupKey: invoiceGroup.Key,
						IsHoldNote: true,
						SheetIndex: holdFirst.SheetIndex);

					preparedInvoice.Issues.Add(holdIssue);
					result.AddIssue(holdIssue);
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}
				var groupConsistencyError = InvoiceDataValidator.ValidateGroupConsistency(invoiceRows);
				if (!string.IsNullOrWhiteSpace(groupConsistencyError))
				{
					result.AddError(invoiceRows[0].Source, invoiceNumber, groupConsistencyError);
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						invoiceRows[0].RowNumber,
						invoiceNumber,
						groupConsistencyError,
						string.Empty,
						BuildCustomerDisplayValue(invoiceRows[0].CustomerCode, invoiceRows[0].CustomerName),
						SheetIndex: invoiceRows[0].SheetIndex));
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				var firstRow = invoiceRows[0];
				var firstRowCustomerValue = BuildCustomerDisplayValue(firstRow.CustomerCode, firstRow.CustomerName);

				if (firstRow.ResolvedCustomerId == 0 || !customerById.TryGetValue(firstRow.ResolvedCustomerId, out var customer))
				{
					var msg = $"Customer '{firstRowCustomerValue}' could not be resolved.";
					result.AddError(firstRow.Source, invoiceNumber, msg, "CustomerCode");
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						firstRow.RowNumber,
						invoiceNumber,
						msg,
						string.Empty,
						firstRowCustomerValue,
						"CustomerCode",
						SheetIndex: firstRow.SheetIndex));
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				if (!InvoiceDataValidator.ResolveOrderType(firstRow.OrderType, null, firstRow.Quantity, out var orderType))
				{
					var msg = $"Order type '{firstRow.OrderType}' is invalid. Use Invoice or Credit.";
					result.AddError(firstRow.Source, invoiceNumber, msg, "OrderType");
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						firstRow.RowNumber,
						invoiceNumber,
						msg,
						string.Empty,
						firstRowCustomerValue,
						"OrderType",
						SheetIndex: firstRow.SheetIndex));
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
					CustomerType = customer.CustomerType ?? string.Empty,
					SubdistributorId = subDistributorId,
					SalesManName = SalesInvoiceService.NormalizeSalesMan(firstRow.SalesManName) ?? string.Empty,
					CustomerAddress = BuildCustomerAddress(customer)
				};

				if (firstRow.CustomerCandidateIds is { Count: > 1 } customerIds)
				{
					var customerOptions = customerIds
						.Where(customerById.ContainsKey)
						.Select(id => customerById[id])
						.Select(c => new CustomerCandidateOption(
							c.CustomerId, c.CustomerCode, c.CustomerName, BuildCustomerAddress(c), c.CustomerType))
						.ToList();

					if (customerOptions.Count > 1)
					{
						var customerChoice = new PendingCustomerChoice
						{
							Key = firstRowCustomerValue.Trim().ToUpperInvariant(),
							Label = firstRowCustomerValue,
							Message = $"Customer '{firstRowCustomerValue}' matched {customerOptions.Count} records. Pick the correct one.",
							Candidates = customerOptions
						};
						customerChoice.SetDefault(customerOptions.First(o => o.CustomerId == customer.CustomerId));
						preparedInvoice.CustomerChoice = customerChoice;
					}
				}

				var items = new List<(InputItemModel Item, bool IsFreeItem)>();
				var unitPriceCache = new Dictionary<int, decimal>();
				var reportedMissingSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var reportedItemWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
							var msg = $"SKU '{itemKey}' was not found.";
							preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, string.Empty, BuildCustomerDisplayValue(row.CustomerCode, row.CustomerName), "SkuCode", SheetIndex: row.SheetIndex));
							result.AddError(row.Source, invoiceNumber, msg, "SkuCode");
						}
						items.Clear();
						break;
					}

					if (row.ResolvedItemsUomId == 0 || !itemsUomById.TryGetValue(row.ResolvedItemsUomId, out var uom))
					{
						var itemNameSuffix = !string.IsNullOrWhiteSpace(subdItem.ItemName) ? $" ({subdItem.ItemName})" : string.Empty;
						var msg = $"UOM '{row.UOM}' was not found for SKU '{row.SkuCode}'{itemNameSuffix}.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, string.Empty, BuildCustomerDisplayValue(row.CustomerCode, row.CustomerName), "UOM", SheetIndex: row.SheetIndex));
						result.AddError(row.Source, invoiceNumber, msg, "UOM");
						items.Clear();
						break;
					}

					if (row.SkuCandidateIds != null && row.SkuCandidateIds.Count > 1 &&
					!preparedInvoice.ItemChoices.Any(c => c.LineItemId == row.LineId))
					{
						var options = new List<ItemCandidateOption>();
						foreach (var candidateId in row.SkuCandidateIds)
						{
							if (!subdItemById.TryGetValue(candidateId, out var candidateItem)) continue;

							List<ItemsUom>? candidateUoms = null;
							foreach (var synonym in InvoiceDataValidator.GetUomSynonyms(row.UOM))
							{
								if (InvoiceDataValidator.TryResolveUom(candidateItem.SubdItemId, synonym, uomLookup, out var m, out var mAmbiguous) && m is not null)
								{ candidateUoms = mAmbiguous is { Count: > 1 } ? mAmbiguous : new List<ItemsUom> { m }; break; }
							}
							if (candidateUoms is null) continue;

							foreach (var cu in candidateUoms)
							{
								var price = row.IsFreeItem ? 0m : await GetUnitPriceAsync(cu.ItemsUomId, firstRow.InvoiceDate);
								options.Add(new ItemCandidateOption(candidateItem.SubdItemId, candidateItem.SubdItemCode, candidateItem.ItemName, cu.ItemsUomId, cu.UomName, price));
							}
						}

						if (options.Count > 1)
						{
							var choice = new PendingItemChoice
							{
								Kind = ChoiceKind.Sku,
								Key = BuildChoiceKey(row, includeUom: false),
								Label = BuildChoiceLabel(row),
								Message = row.AmbiguousItemWarning ?? $"'{BuildChoiceLabel(row)}' matched {options.Count} records.",
								LineItemId = row.LineId,
								Candidates = options
							};
							choice.SetDefault(
								options.FirstOrDefault(o => o.SubdItemId == row.ResolvedSubdItemId && o.ItemsUomId == row.ResolvedItemsUomId)
								?? options.FirstOrDefault(o => o.SubdItemId == row.ResolvedSubdItemId)
								?? options[0]);
							preparedInvoice.ItemChoices.Add(choice);
						}
					}

					if (!string.IsNullOrWhiteSpace(row.UomFallbackWarning) && reportedItemWarnings.Add(row.UomFallbackWarning))
					{
						preparedInvoice.Warnings.Add(row.UomFallbackWarning);
					}
					if (row.UomReviewCandidateIds is { Count: > 0 } &&
						!preparedInvoice.ItemChoices.Any(c => c.LineItemId == row.LineId))
					{
						var options = new List<ItemCandidateOption>();
						foreach (var uomId in row.UomReviewCandidateIds)
						{
							if (!itemsUomById.TryGetValue(uomId, out var candidateUom)) continue;
							var price = row.IsFreeItem ? 0m : await GetUnitPriceAsync(candidateUom.ItemsUomId, firstRow.InvoiceDate);
							// Same SubdItemId/Code/Name for every option — only the UOM/price differs.
							options.Add(new ItemCandidateOption(subdItem.SubdItemId, subdItem.SubdItemCode, subdItem.ItemName,
								candidateUom.ItemsUomId, candidateUom.UomName, price));
						}
						if (options.Count > 0)
						{
							var uomChoice = new PendingItemChoice
							{
								Kind = ChoiceKind.Uom,
								Key = BuildChoiceKey(row, includeUom: true),
								Label = $"{BuildChoiceLabel(row)} ({row.UOM})",
								Message = $"UOM '{row.UOM}' has multiple possible matches for '{BuildChoiceLabel(row)}'.",
								LineItemId = row.LineId,
								Candidates = options
							};
							uomChoice.SetDefault(options.FirstOrDefault(o => o.ItemsUomId == row.ResolvedItemsUomId) ?? options[0]);
							preparedInvoice.ItemChoices.Add(uomChoice);
						}
					}

					if (row.Quantity == 0)
					{
						continue;
					}

					var unitPrice = row.IsFreeItem
						? 0m
						: await GetUnitPriceAsync(uom.ItemsUomId, firstRow.InvoiceDate);
					var absoluteQuantity = Math.Abs(row.Quantity);

					items.Add((new InputItemModel
					{
						LineItemId = row.LineId,
						ItemCode = subdItem.SubdItemCode,
						ItemName = subdItem.ItemName,
						SubdItemId = subdItem.SubdItemId,
						ItemsUomId = uom.ItemsUomId,
						UomName = uom.UomName,
						Quantity = absoluteQuantity,
						Amount = unitPrice * absoluteQuantity,
						SourceRowNumber = row.RowNumber,
						SourceSheetIndex = row.SheetIndex,
						SourceRows = new List<SourceRowKey> { row.Source }
					}, row.IsFreeItem));
				}
				if (items.Count == 0)
				{
					if (preparedInvoice.Issues.Count == 0)
					{
						var msg = $"Invoice '{invoiceNumber}' has no valid item lines to import.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
							firstRow.RowNumber, invoiceNumber, msg, string.Empty, firstRowCustomerValue, SheetIndex: firstRow.SheetIndex));
						result.AddError(firstRow.Source, invoiceNumber, msg);
					}
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}
				preparedInvoice.RawItems = items;
				preparedInvoice.Items = BuildAggregatedItems(items, preparedInvoice.Invoice?.OrderType);

				var validationErrors = await SalesInvoiceValidation.ValidateHeaderAsync(
					preparedInvoice.Invoice!,
					() => _salesInvoiceService.InvoiceNumberExistsAsync(preparedInvoice.Invoice!.InvoiceNumber, preparedInvoice.Invoice!.OrderType, preparedInvoice.Invoice!.SubdistributorId, preparedInvoice.Invoice!.CustomerId, preparedInvoice.Invoice!.SalesManName, 0, cancellationToken));

				if (validationErrors.Count > 0 && preparedInvoice.CustomerChoice is { } pendingCustomer)
				{
					pendingCustomer.ValidationMessage = string.Join(" ", validationErrors.Values);
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				if (validationErrors.Count > 0)
				{
					var msg = string.Join(" ", validationErrors.Values);
					preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
						firstRow.RowNumber,
						invoiceNumber,
						msg,
						string.Empty,
						string.Empty,
						SheetIndex: firstRow.SheetIndex));
					result.AddError(firstRow.Source, invoiceNumber, msg);
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				result.PreparedInvoices.Add(preparedInvoice);
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				_logger.LogError(ex, "Failed to prepare sales invoice {InvoiceNumber} from row {RowNumber}: {Message}", invoiceNumber, invoiceRows[0].RowNumber, baseMsg);
				result.AddError(invoiceRows[0].Source, invoiceNumber, $"Unexpected error while preparing invoice '{invoiceNumber}': {baseMsg}");
				preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
					invoiceRows[0].RowNumber,
					invoiceNumber,
					$"Unexpected error while preparing invoice '{invoiceNumber}': {baseMsg}",
					string.Empty,
					string.Empty,
					SheetIndex: invoiceRows[0].SheetIndex));
				result.PreparedInvoices.Add(preparedInvoice);
			}
		}

		return result;
	}
	public async Task<string?> ValidateInvoiceHeaderAsync(PreparedInvoice prepared, CancellationToken cancellationToken = default)
	{
		var invoice = prepared.Invoice;
		var errors = await SalesInvoiceValidation.ValidateHeaderAsync(
			invoice,
			() => _salesInvoiceService.InvoiceNumberExistsAsync(
				invoice.InvoiceNumber, invoice.OrderType, invoice.SubdistributorId,
				invoice.CustomerId, invoice.SalesManName, 0, cancellationToken));
		return errors.Count > 0 ? string.Join(" ", errors.Values) : null;
	}
	private static List<ErroredRowInfo> FindBlockingRows(
		IReadOnlyList<ErroredRowInfo> erroredRows, string invoiceCode, ImportedInvoiceRow groupFirstRow)
	{
		var code = invoiceCode.Trim();
		var customerKey = groupFirstRow.ResolvedCustomerId.ToString(CultureInfo.InvariantCulture);
		var orderKey = (groupFirstRow.OrderType ?? string.Empty).Trim().ToUpperInvariant();
		var salesKey = (groupFirstRow.SalesManName ?? string.Empty).Trim().ToUpperInvariant();

		return erroredRows
			.Where(e => string.Equals(e.InvoiceCode, code, StringComparison.OrdinalIgnoreCase))
			.Where(e => e.HeaderUnresolved
				|| ((e.CustomerKey.Length == 0 || e.CustomerKey == customerKey)
					&& (e.OrderType.Length == 0 || e.OrderType == orderKey)
					&& (e.SalesMan.Length == 0 || e.SalesMan == salesKey)))
			.OrderBy(e => e.SheetIndex)
			.ThenBy(e => e.RowNumber)
			.ToList();
	}

	private static string BuildHoldMessage(List<ErroredRowInfo> blockers, ImportSalesInvoiceResult result)
	{
		var sources = blockers.Select(b => b.Source).Distinct().ToList();
		var plural = sources.Count > 1;
		var rows = result.FormatRows(sources);
		var unresolved = blockers.Where(b => b.HeaderUnresolved).ToList();

		string what;
		if (unresolved.Count == 0)
			what = plural ? "with the same invoice number have errors" : "with the same invoice number has an error";
		else if (!plural)
			what = $"has the same invoice number but its {unresolved[0].HeaderProblem}, so we can't tell which invoice it belongs to";
		else
			what = "have the same invoice number, and some of them have a customer or order type problem, so we can't tell which invoice they belong to";

		return $"On hold: {rows} {what}. Fix {(plural ? "those rows" : "that row")} and this invoice will import.";
	}

	/// <summary>The sheet all the rows are on, or null when they span several sheets (never the lowest of them).</summary>
	private static int? SingleSheetOrNull(IEnumerable<SourceRowKey> rows)
	{
		var sheets = rows.Select(r => r.SheetIndex).Distinct().Take(2).ToList();
		return sheets.Count == 1 ? sheets[0] : null;
	}

	public static List<InputItemModel> BuildAggregatedItems(List<(InputItemModel Item, bool IsFreeItem)> rawItems, string? orderType)
	{
		var aggregated = rawItems
			.GroupBy(e => new { e.Item.SubdItemId, e.Item.ItemsUomId, e.Item.ItemCode, e.Item.ItemName, e.Item.UomName, e.IsFreeItem })
			.Select(g => new InputItemModel
			{
				LineItemId = g.Min(e => e.Item.LineItemId),
				ItemCode = g.Key.ItemCode,
				ItemName = g.Key.IsFreeItem ? $"{g.Key.ItemName} (Free)" : g.Key.ItemName,
				SubdItemId = g.Key.SubdItemId,
				ItemsUomId = g.Key.ItemsUomId,
				UomName = g.Key.UomName,
				Quantity = g.Sum(e => e.Item.Quantity),
				Amount = g.Sum(e => e.Item.Amount),
				SourceRowNumber = g.Min(e => e.Item.SourceRowNumber),
				SourceSheetIndex = SingleSheetOrNull(g.SelectMany(e => e.Item.SourceRows)),
				SourceRows = g.SelectMany(e => e.Item.SourceRows).Distinct().OrderBy(k => k.SheetIndex).ThenBy(k => k.RowNumber).ToList(),
				LineItemIds = g.Select(e => e.Item.LineItemId).Distinct().OrderBy(id => id).ToList()
			})
			.ToList();

		if (string.Equals(orderType, "Credit", StringComparison.OrdinalIgnoreCase))
			foreach (var it in aggregated) it.Amount = -Math.Abs(it.Amount);

		return aggregated;
	}

	public static string? TryMergeInvoices(PreparedInvoice target, PreparedInvoice source)
	{
		if (ReferenceEquals(target, source)) return null;

		if (target.Invoice is null || source.Invoice is null)
			return $"Invoice '{source.InvoiceNumber}' cannot be combined with its other rows because one copy is incomplete.";
		if (target.IsSaved || source.IsSaved)
			return $"Invoice '{source.InvoiceNumber}' is already saved.";
		if (target.Issues.Count > 0 || source.Issues.Count > 0)
			return $"Invoice '{source.InvoiceNumber}' appears in several rows and one of them has errors. Fix the file and re-upload.";
		if (target.Invoice.InvoiceDate != source.Invoice.InvoiceDate)
			return $"Invoice '{source.InvoiceNumber}' has different invoice dates across its rows. Fix the file and re-upload.";

		target.RawItems.AddRange(source.RawItems);
		target.Items = BuildAggregatedItems(target.RawItems, target.Invoice.OrderType);
		target.ItemChoices.AddRange(source.ItemChoices);
		foreach (var w in source.Warnings)
			if (!target.Warnings.Contains(w)) target.Warnings.Add(w);

		target.Selected = false;
		return null;
	}
	public async Task<ImportSalesInvoiceResult> CommitPreparedInvoicesAsync(IEnumerable<PreparedInvoice> preparedInvoices, int currentUserId, CancellationToken cancellationToken = default)
	{
		var result = new ImportSalesInvoiceResult();
		if (preparedInvoices is null)
		{
			result.AddError(0, string.Empty, "No prepared invoices provided for commit.");
			return result;
		}

		void FailInvoice(PreparedInvoice p, string msg)
		{
			var firstItem = p.Items.FirstOrDefault();
			// A source row, or the file as a whole when the item has none. LineItemId is not a row number.
			var source = firstItem?.SourceRows.FirstOrDefault() ?? default;
			p.Issues.Add(new ImportSalesInvoiceIssue(source.RowNumber, p.InvoiceNumber, msg, string.Empty, string.Empty, SheetIndex: source.SheetIndex));
			result.AddError(source, p.InvoiceNumber, msg);
			p.IsSaved = false;
			p.SaveErrorMessage = msg;
		}

		foreach (var prepared in preparedInvoices)
		{
			if (prepared is null || !prepared.Selected)
				continue;

			if (!prepared.IsReadyToCommit)
			{
				prepared.Selected = false;
				continue;
			}

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
					FailInvoice(prepared, $"Sales invoice '{prepared.InvoiceNumber}' already exists for this order type, customer and salesman.");
					continue;
				}
				if (!saveResult.IsSaved)
				{
					FailInvoice(prepared, saveResult.ErrorMessage ?? "Unable to save invoice.");
					continue;
				}

				prepared.IsSaved = true;
				result.ImportedInvoiceCount++;
				result.ImportedRowCount += prepared.Items?.Count ?? 0;
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				FailInvoice(prepared, $"Unexpected error while saving invoice '{prepared.InvoiceNumber}': {baseMsg}");
			}
		}

		return result;
	}


	private static List<ImportedInvoiceRow> ReadRows(
		WorkbookRows source,
		ImportSalesInvoiceResult result,
		IReadOnlyDictionary<string, Data.Customer> customerByCode,
		IReadOnlyDictionary<string, Data.Customer> customerByName,
		ILookup<string, SubdItem> subdItemsBySkuGroup,
		ILookup<(int subdItemId, string UomName), ItemsUom> uomLookup,
		ILookup<int, ItemsUom> uomsBySubdItemId,
		IReadOnlyDictionary<int, HashSet<int>> knownConversionsBySubdItem,
		IEnumerable<Data.Customer> allCustomers,
		IEnumerable<SubdItem> allSubdItems)
	{
		// Reads and validates rows from the worksheet starting after the header row, returning a list of parsed invoice rows along with any issues found.
		var rows = new List<ImportedInvoiceRow>();

		// One line-id counter for the whole workbook, so line ids stay unique across sheets.
		var nextLineId = 0;

		foreach (var logicalRow in source.Rows())
		{
			var row = logicalRow.Row;
			var rowNumber = logicalRow.RowNumber;

			// Source identity: the worksheet's position in the original workbook (1-based, hidden sheets counted).
			var sheetIndex = logicalRow.SheetIndex;
			var sheetResult = result.GetOrAddSheet(sheetIndex, logicalRow.Sheet.Inspection.Name);
			var sheetLastColumn = logicalRow.Sheet.Inspection.LastCol;

			// Optional columns. Text columns are read through ReadValue, which returns "" when a column isn't mapped.
			var hasCustomerTypeColumn = logicalRow.Has("CustomerType");
			var hasAddressLineColumn = logicalRow.Has("AddressLine");
			var hasCityMunicipalityColumn = logicalRow.Has("CityMunicipality");
			var hasProvinceColumn = logicalRow.Has("Province");
			var hasItemNameColumn = logicalRow.Has("ItemName");

			// Quantity columns
			var hasQuantityColumn = logicalRow.Has("Quantity");
			var hasCaseQuantityColumn = logicalRow.Has("CaseQuantity");
			var hasDozenQuantityColumn = logicalRow.Has("DozenQuantity");
			var hasPieceQuantityColumn = logicalRow.Has("PieceQuantity");
			var hasInBoxQuantityColumn = logicalRow.Has("InBoxQuantity");
			var useSplitQuantities = hasCaseQuantityColumn || hasDozenQuantityColumn || hasPieceQuantityColumn || hasInBoxQuantityColumn;

			var hasNetAmountColumn = logicalRow.Has("NetAmount");

			// If the admin picked a text format for the invoice date, use exactly that.
			var invoiceDateFormat = logicalRow.Rule("InvoiceDate")?.OptionsJson;

			// ── Read raw cell values (with the template's "how to read it" rule applied) ──
			var invoiceCode = ReadValue(logicalRow, "InvoiceCode");
			var customerCode = ReadValue(logicalRow, "CustomerCode");
			var customerName = ReadValue(logicalRow, "CustomerName");
			var customerType = hasCustomerTypeColumn ? ReadValue(logicalRow, "CustomerType") : null;
			var province = hasProvinceColumn ? ReadValue(logicalRow, "Province") : null;
			var city = hasCityMunicipalityColumn ? ReadValue(logicalRow, "CityMunicipality") : null;
			var addressLine = hasAddressLineColumn ? ReadValue(logicalRow, "AddressLine") : null;
			var orderType = ReadValue(logicalRow, "OrderType");
			var skuCode = ReadValue(logicalRow, "SkuCode");
			var itemName = ReadValue(logicalRow, "ItemName");
			var uom = ReadValue(logicalRow, "UnitOfMeasure");
			var salesManName = ReadValue(logicalRow, "SalesManName");
			var netAmountCell = logicalRow.Cell("NetAmount");
			var freeItemsRaw = ReadValue(logicalRow, "FreeItems");
			var isFreeItem = string.Equals(freeItemsRaw, "Yes", StringComparison.OrdinalIgnoreCase)
			  || InvoiceDataValidator.IsFreeItemValue(freeItemsRaw);

			// ── Skip completely empty rows ───────────────────────────────────────
			if (string.IsNullOrWhiteSpace(invoiceCode) &&
				string.IsNullOrWhiteSpace(customerCode) &&
				string.IsNullOrWhiteSpace(customerName) &&
				string.IsNullOrWhiteSpace(orderType) &&
				string.IsNullOrWhiteSpace(skuCode) &&
				string.IsNullOrWhiteSpace(itemName) &&
				string.IsNullOrWhiteSpace(uom) &&
				IsMissingOrEmpty(logicalRow.Cell("InvoiceDate")) &&
				(!hasQuantityColumn || IsCellEffectivelyEmpty(logicalRow.Cell("Quantity")!)) &&
				(!hasCaseQuantityColumn || IsCellEffectivelyEmpty(logicalRow.Cell("CaseQuantity")!)) &&
				(!hasDozenQuantityColumn || IsCellEffectivelyEmpty(logicalRow.Cell("DozenQuantity")!)) &&
				(!hasPieceQuantityColumn || IsCellEffectivelyEmpty(logicalRow.Cell("PieceQuantity")!)) &&
				(!hasInBoxQuantityColumn || IsCellEffectivelyEmpty(logicalRow.Cell("InBoxQuantity")!)) &&
				(netAmountCell is null || IsAmountCellEffectivelyEmptyForRowSkip(netAmountCell)))
			{
				continue;
			}

			result.TotalRowsProcessed++;
			var issueStart = result.Issues.Count;

			var rawValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
			foreach (var field in logicalRow.FieldKeys)
			{
				rawValues[field] = GetString(logicalRow.Cell(field)!);
			}
			result.RawValuesByRow[new SourceRowKey(sheetIndex, rowNumber)] = rawValues;

			var sheetCells = new string[sheetLastColumn];
			for (int c = 1; c <= sheetLastColumn; c++)
				sheetCells[c - 1] = GetString(row, c);
			sheetResult.RawRows[rowNumber] = sheetCells;

			DateOnly invoiceDate = default;
			var emittedRows = new List<ImportedInvoiceRow>();
			var rowHasErrors = false;
			var orderTypeInvalid = false;

			void AddRowError(string message, string? columnName = null, int? suggestionCount = null)
			{
				result.AddError(new SourceRowKey(sheetIndex, rowNumber), invoiceCode, message, columnName, suggestionCount);
				rowHasErrors = true;
			}

			//-------Validate required fields------------
			if (string.IsNullOrWhiteSpace(invoiceCode))
				AddRowError("Invoice code is required.", "InvoiceCode");
			if (IsMissingOrEmpty(logicalRow.Cell("InvoiceDate")))
				AddRowError("Invoice date is required.", "InvoiceDate");
			else if (!TryGetDateOnly(logicalRow.Cell("InvoiceDate")!, out invoiceDate, invoiceDateFormat))
				AddRowError("Invoice date is invalid.", "InvoiceDate");

			//-------Validate customer-----------------
			List<int>? customerCandidateIds = null;
			if (!InvoiceDataValidator.TryResolveCustomer(customerCode, customerName, province, city, customerType, addressLine,
					customerByCode, allCustomers, out var resolvedCustomer, out var customerSuggestions, out var customerAmbiguous))
			{
				if (customerAmbiguous is { Count: > 1 })
				{
					customerCandidateIds = customerAmbiguous.Select(c => c.CustomerId).OrderBy(id => id).ToList();
					resolvedCustomer = customerAmbiguous.OrderBy(c => c.CustomerId).First();
				}
				else if (string.IsNullOrWhiteSpace(customerCode) && string.IsNullOrWhiteSpace(customerName))
					AddRowError("Customer code or name is required.", "CustomerCode");
				else
				{
					var label = BuildCustomerDisplayValue(customerCode, customerName);
					var colName = string.IsNullOrWhiteSpace(customerName) ? "CustomerCode" : "CustomerName";
					var message = customerSuggestions is { Count: > 0 }
						? $"Customer '{label}' was not found. Did you mean: {string.Join(", ", customerSuggestions.Select(s => BuildCustomerDisplayValue(s.CustomerCode, s.CustomerName)))}?"
						: $"Customer '{label}' was not found.";
					AddRowError(message, colName, customerSuggestions?.Count);
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
				{
					AddRowError($"Order type '{orderType}' is invalid. Use Invoice or Credit.", "OrderType");
					orderTypeInvalid = true;
				}
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
					subdItemsBySkuGroup, allSubdItems,
					out var resolvedItem, out var itemSuggestions, out var itemWarning, out var ambiguousCandidates))
			{
				if (string.IsNullOrWhiteSpace(skuCode) && string.IsNullOrWhiteSpace(itemName))
				{
					AddRowError("SKU code or Item name is required.", hasItemNameColumn ? "ItemName" : "SkuCode");
				}
				else if (!string.IsNullOrWhiteSpace(skuCode) && !string.IsNullOrWhiteSpace(itemName))
				{
					var label = BuildCustomerDisplayValue(skuCode, itemName);
					var colName = "SkuCode";
					var message = itemSuggestions is { Count: > 0 }
						? $"SKU '{label}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => BuildCustomerDisplayValue(s.SubdItemCode, s.ItemName)))}?"
						: $"SKU '{label}' was not found.";
					AddRowError(message, colName, itemSuggestions?.Count);
				}
				else if (!string.IsNullOrWhiteSpace(skuCode))
				{
					var message = itemSuggestions is { Count: > 0 }
						? $"SKU '{skuCode}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => BuildCustomerDisplayValue(s.SubdItemCode, s.ItemName)))}?"
						: $"SKU '{skuCode}' was not found.";
					AddRowError(message, "SkuCode", itemSuggestions?.Count);
				}
				else
				{
					var message = itemSuggestions is { Count: > 0 }
						? $"Item name '{itemName}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => BuildCustomerDisplayValue(s.SubdItemCode, s.ItemName)))}?"
						: $"Item name '{itemName}' was not found.";
					AddRowError(message, "ItemName", itemSuggestions?.Count);
				}
			}

			// ── Quantity & UOM ───────────────────────────────────────────────────
			void FinalizeErroredRow()
			{
				if (!rowHasErrors || string.IsNullOrWhiteSpace(invoiceCode)) return;

				var customerResolved = resolvedCustomer is not null;
				var headerProblem = !customerResolved
					? (string.IsNullOrWhiteSpace(customerCode) && string.IsNullOrWhiteSpace(customerName)
						? "customer is missing"
						: "customer was not found")
					: orderTypeInvalid ? "order type is invalid" : null;

				var finalOrderType = orderTypeInvalid
					? string.Empty
					: !string.IsNullOrWhiteSpace(normalizedOrderType)
						? normalizedOrderType
						: (emittedRows.FirstOrDefault()?.OrderType ?? string.Empty);

				var code = invoiceCode.Trim();
				var customerKey = customerResolved && customerCandidateIds is null
					? resolvedCustomer!.CustomerId.ToString(CultureInfo.InvariantCulture)
					: string.Empty;
				var groupCustomerPart = customerCandidateIds is { Count: > 1 }
					? BuildCustomerCandidatePart(customerCandidateIds)
					: customerKey; var orderKey = finalOrderType.Trim().ToUpperInvariant();
				var salesKey = (salesManName ?? string.Empty).Trim().ToUpperInvariant();

				result.ErroredRows.Add(new ErroredRowInfo(
					rowNumber, code, customerKey, orderKey, salesKey, headerProblem is not null, headerProblem, sheetIndex));

				if (headerProblem is null && orderKey.Length > 0)
				{
					var groupKey = string.Join("|", code.ToUpperInvariant(), groupCustomerPart, orderKey, salesKey);
					for (var i = issueStart; i < result.Issues.Count; i++)
						result.Issues[i] = result.Issues[i] with { GroupKey = groupKey };
				}
			}

			(int UomId, List<Data.ItemsUom>? Ambiguous, Data.SubdItem? MatchedItem) ResolveUomWithMatches(string uomString)
			{
				if (resolvedItem is null) return (0, null, null);

				var pool = new List<Data.SubdItem> { resolvedItem };
				if (ambiguousCandidates is { Count: > 1 })
					pool.AddRange(ambiguousCandidates
						.Where(c => c.SubdItemId != resolvedItem.SubdItemId)
						.OrderBy(c => c.SubdItemId));

				foreach (var candidate in pool)
					foreach (var synonym in InvoiceDataValidator.GetUomSynonyms(uomString))
						if (InvoiceDataValidator.TryResolveUom(candidate.SubdItemId, synonym, uomLookup, out var matched, out var ambiguous)
							&& matched is not null)
							return (matched.ItemsUomId, ambiguous, candidate);

				return (0, null, null);
			}

			if (!useSplitQuantities)
			{
				var quantityCell = logicalRow.Cell("Quantity");
				var quantityIsEmpty = quantityCell is null || IsCellEffectivelyEmpty(quantityCell);

				int? rawQuantity = null;
				if (!quantityIsEmpty && TryGetInt(quantityCell!, out var parsedQty))
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
					if (quantityIsEmpty)
						AddRowError("Quantity is required.", "Quantity");
					else if (rawQuantity is null)
						AddRowError("Quantity must be a whole number.", "Quantity");
					else if (InvoiceDataValidator.IsMissingUomValue(uom))
						AddRowError("UOM is required.", "UOM");
				}
				else
				{
					if (resolvedQty == 0)
					{
						FinalizeErroredRow();
						continue;
					}

					if (string.IsNullOrWhiteSpace(normalizedOrderType))
						normalizedOrderType = resolvedQty < 0 ? "Credit" : "Invoice";

					var (resolvedUomId, uomAmbiguousMatches, matchedItem) = ResolveUomWithMatches(resolvedUom);
					var rowItem = matchedItem ?? resolvedItem;
					var finalUomName = resolvedUom;
					string? uomFallbackWarning = null;
					List<int>? uomReviewCandidateIds = null;

					if (resolvedUomId == 0 && resolvedItem is not null)
					{
						// Not found at all → stays an ERROR (unchanged)
						InvoiceDataValidator.TryResolveFallbackUom(out _, out _, out var reviewUoms);

						if (reviewUoms is { Count: > 0 })
						{
							var tentative = reviewUoms[0];
							resolvedUomId = tentative.ItemsUomId;
							finalUomName = tentative.UomName;
							uomReviewCandidateIds = reviewUoms.Select(u => u.ItemsUomId).ToList();
						}
						else
						{
							var itemNameSuffix = !string.IsNullOrWhiteSpace(resolvedItem.ItemName) ? $" ({resolvedItem.ItemName})" : string.Empty;
							AddRowError($"UOM '{uom}' was not found for SKU '{skuCode}'{itemNameSuffix}.", "UOM");
						}
					}
					else if (uomAmbiguousMatches is { Count: > 1 })
					{
						// Found, but more than one active record for the same UOM name → WARNING
						uomReviewCandidateIds = uomAmbiguousMatches.Select(u => u.ItemsUomId).ToList();
					}

					if (!rowHasErrors)
					{
						emittedRows.Add(new ImportedInvoiceRow(
							RowNumber: rowNumber,
							SheetIndex: sheetIndex,
							InvoiceCode: invoiceCode,
							InvoiceDate: invoiceDate,
							CustomerCode: customerCode,
							CustomerName: customerName,
							OrderType: normalizedOrderType,
							SalesManName: salesManName,
							SkuCode: skuCode,
							ItemName: itemName,
							UOM: finalUomName,
							Quantity: resolvedQty,
							Province: province ?? string.Empty,
							CityMunicipality: city ?? string.Empty,
							CustomerType: customerType,
							AddressLine: addressLine,
							ResolvedCustomerId: resolvedCustomer?.CustomerId ?? 0,
							ResolvedCustomerCode: resolvedCustomer?.CustomerCode ?? string.Empty,
							ResolvedCustomerName: resolvedCustomer?.CustomerName ?? string.Empty,
							ResolvedSubdItemId: rowItem?.SubdItemId ?? 0,
							ResolvedSubdItemCode: rowItem?.SubdItemCode ?? string.Empty,
							ResolvedItemsUomId: resolvedUomId,
							IsFreeItem: isFreeItem,
							AmbiguousItemWarning: itemWarning,
							SkuCandidateIds: ambiguousCandidates?.Select(c => c.SubdItemId).ToList(),
							UomFallbackWarning: uomFallbackWarning,
							UomReviewCandidateIds: uomReviewCandidateIds,
							CustomerCandidateIds: customerCandidateIds));
					}
				}
			}
			else
			{
				void TryEmitSplitRow(string field, string uomLabel, string errorField)
				{
					var cell = logicalRow.Cell(field);
					if (cell is null || IsCellEffectivelyEmpty(cell))
						return;

					var rawValue = cell.GetString().Trim();
					if (InvoiceDataValidator.IsMissingUomValue(rawValue))
						return;

					if (!TryGetInt(cell, out var qty))
					{
						AddRowError($"{errorField} must be a whole number.", errorField);
						return;
					}

					if (qty == 0)
						return;

					var rowOrderType = string.IsNullOrWhiteSpace(normalizedOrderType)
						? (qty < 0 ? "Credit" : "Invoice")
						: normalizedOrderType;

					var normalizedUomName = InvoiceDataValidator.NormalizeUomName(uomLabel);
					var (resolvedUomId, uomAmbiguousMatches, matchedItem) = ResolveUomWithMatches(normalizedUomName);
					var rowItem = matchedItem ?? resolvedItem;
					var finalUomName = normalizedUomName;
					List<int>? uomReviewCandidateIds = null;

					if (resolvedUomId == 0 && resolvedItem is not null)
					{
						InvoiceDataValidator.TryResolveFallbackUom(out _, out _, out var reviewUoms);

						if (reviewUoms is { Count: > 0 })
						{
							var tentative = reviewUoms[0];
							resolvedUomId = tentative.ItemsUomId;
							finalUomName = tentative.UomName;
							uomReviewCandidateIds = reviewUoms.Select(u => u.ItemsUomId).ToList();
						}
						else
						{
							var itemNameSuffix = !string.IsNullOrWhiteSpace(resolvedItem.ItemName) ? $" ({resolvedItem.ItemName})" : string.Empty;
							AddRowError($"UOM '{normalizedUomName}' was not found for SKU '{skuCode}'{itemNameSuffix}.", errorField);
							return;
						}
					}
					else if (uomAmbiguousMatches is { Count: > 1 })
					{
						uomReviewCandidateIds = uomAmbiguousMatches.Select(u => u.ItemsUomId).ToList();
					}

					emittedRows.Add(new ImportedInvoiceRow(
						RowNumber: rowNumber,
						SheetIndex: sheetIndex,
						InvoiceCode: invoiceCode,
						InvoiceDate: invoiceDate,
						CustomerCode: customerCode,
						CustomerName: customerName,
						OrderType: rowOrderType,
						SalesManName: salesManName,
						SkuCode: skuCode,
						ItemName: itemName,
						UOM: finalUomName,
						Quantity: qty,
						Province: province ?? string.Empty,
						CityMunicipality: city ?? string.Empty,
						CustomerType: customerType,
						AddressLine: addressLine,
						ResolvedCustomerId: resolvedCustomer?.CustomerId ?? 0,
						ResolvedCustomerCode: resolvedCustomer?.CustomerCode ?? string.Empty,
						ResolvedCustomerName: resolvedCustomer?.CustomerName ?? string.Empty,
						ResolvedSubdItemId: rowItem?.SubdItemId ?? 0,
						ResolvedSubdItemCode: rowItem?.SubdItemCode ?? string.Empty,
						ResolvedItemsUomId: resolvedUomId,
						IsFreeItem: isFreeItem,
						AmbiguousItemWarning: itemWarning,
						SkuCandidateIds: ambiguousCandidates?.Select(c => c.SubdItemId).ToList(),
						UomFallbackWarning: null,
						UomReviewCandidateIds: uomReviewCandidateIds,
						CustomerCandidateIds: customerCandidateIds));
				}

				TryEmitSplitRow("CaseQuantity", "case", "CaseQuantity");
				TryEmitSplitRow("DozenQuantity", "dozen", "DozenQuantity");
				TryEmitSplitRow("PieceQuantity", "piece", "PieceQuantity");
				TryEmitSplitRow("InBoxQuantity", "inbox", "InBoxQuantity");

				if (emittedRows.Count == 0 && !rowHasErrors)
				{
					var errorCol = hasCaseQuantityColumn ? "CaseQuantity" :
								hasPieceQuantityColumn ? "PieceQuantity" :
								hasInBoxQuantityColumn ? "InBoxQuantity" : "DozenQuantity";
					AddRowError("At least one quantity column (Case, Dozen, Piece, or InBox) is required.", errorCol);
				}
			}
			if (rowHasErrors)
			{
				FinalizeErroredRow();
				continue;
			}

			foreach (var e in emittedRows)
				rows.Add(e with { LineId = ++nextLineId });
		}

		return rows;
	}

	private static bool IsAmountCellEffectivelyEmptyForRowSkip(IXLCell cell)
	{
		if (cell.HasFormula)
		{
			var cached = cell.CachedValue.ToString()?.Trim();
			if (string.IsNullOrWhiteSpace(cached))
				return true;

			if (decimal.TryParse(cached, NumberStyles.Number | NumberStyles.AllowLeadingSign,
					CultureInfo.InvariantCulture, out var d) && d == 0)
				return true;

			return false;
		}

		return cell.IsEmpty();
	}

	private static string GetString(IXLRow row, int columnNumber) => GetString(row.Cell(columnNumber));

	private static string GetString(IXLCell cell)
	{
		if (cell.HasFormula)
			return cell.CachedValue.ToString()?.Trim() ?? string.Empty;
		return cell.GetString().Trim();
	}

	// Reads a text field and applies the column's "how to read it" rule from the template.
	private static string ReadValue(LogicalRow row, string field)
	{
		var cell = row.Cell(field);
		if (cell is null) return string.Empty;

		var raw = GetString(cell);
		if (raw.Length == 0) return raw;

		var rule = row.Rule(field);
		if (rule is null || rule.RuleType == ColumnRuleTypes.Direct) return raw;

		if (ImportRules.TryApply(raw, rule.RuleType, rule.OptionsJson, out var applied, out _))
			return applied;

		return rule.RuleType == ColumnRuleTypes.ValueMap ? string.Empty : raw;
	}

	private static bool TryGetInt(IXLCell cell, out int value)
	{
		if (cell.HasFormula)
		{
			var cached = cell.CachedValue.ToString().Trim();
			if (int.TryParse(cached, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
				return true;
			if (double.TryParse(cached, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
			{
				if (d != Math.Truncate(d))
				{
					value = 0;
					return false;
				}
				value = (int)d;
				return true;
			}
			value = 0;
			return false;
		}

		if (cell.DataType == XLDataType.Number)
		{
			var d = cell.GetDouble();
			if (d != Math.Truncate(d))
			{
				value = 0;
				return false;
			}
			value = (int)d;
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

	private static bool TryGetDateOnly(IXLCell cell, out DateOnly date, string? dateFormat = null)
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

		// The admin picked a text format for this column: use exactly that, never guess.
		// This has to come before TryGetValue<DateTime>, which would otherwise guess first.
		if (!string.IsNullOrWhiteSpace(dateFormat) && cell.DataType != XLDataType.Number)
		{
			var formatted = cell.GetString().Trim();
			if (DateTime.TryParseExact(formatted, dateFormat, CultureInfo.InvariantCulture,
					DateTimeStyles.AllowWhiteSpaces, out var exact))
			{
				date = DateOnly.FromDateTime(exact);
				return true;
			}

			date = default;
			return false;
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

	// A field that isn't mapped on this row counts as empty.
	private static bool IsMissingOrEmpty(IXLCell? cell) => cell is null || IsCellEffectivelyEmpty(cell);

	private static bool IsCellEffectivelyEmpty(IXLCell cell)
	{
		if (cell.HasFormula)
			return string.IsNullOrWhiteSpace(cell.CachedValue.ToString());
		return cell.IsEmpty();
	}

	private static Dictionary<int, HashSet<int>> BuildKnownConversionsBySubdItem(
		WorkbookRows source,
		ILookup<string, SubdItem> subdItemsBySkuGroup,
		IEnumerable<SubdItem> allSubdItems,
		ILookup<(int subdItemId, string UomName), ItemsUom> uomLookup)
	{
		var result = new Dictionary<int, HashSet<int>>();

		void Record(int subdItemId, int conversion)
		{
			if (!result.TryGetValue(subdItemId, out var set))
			{
				set = new HashSet<int>();
				result[subdItemId] = set;
			}
			set.Add(conversion);
		}

		int? TryMatchConversion(SubdItem item, string uomString)
		{
			foreach (var synonym in InvoiceDataValidator.GetUomSynonyms(uomString))
			{
				if (InvoiceDataValidator.TryResolveUom(item.SubdItemId, synonym, uomLookup, out var matched, out _) && matched is not null)
					return matched.ConversionToBase;
			}
			return null;
		}

		// Every sheet, under the same shared row budget the real read uses.
		foreach (var logicalRow in source.Rows())
		{
			if (logicalRow.Row.CellsUsed().All(c => c.IsEmpty())) continue;

			var skuCode = ReadValue(logicalRow, "SkuCode");
			var itemName = ReadValue(logicalRow, "ItemName");

			if (!InvoiceDataValidator.TryResolveItem(
					skuCode, itemName, subdItemsBySkuGroup, allSubdItems,
					out var resolvedItem, out _, out _, out _) || resolvedItem is null)
				continue;

			var useSplitQuantities = logicalRow.Has("CaseQuantity") || logicalRow.Has("DozenQuantity")
				|| logicalRow.Has("PieceQuantity") || logicalRow.Has("InBoxQuantity");

			if (!useSplitQuantities)
			{
				var uom = ReadValue(logicalRow, "UnitOfMeasure");
				if (string.IsNullOrWhiteSpace(uom)) continue;
				var conversion = TryMatchConversion(resolvedItem, uom);
				if (conversion.HasValue) Record(resolvedItem.SubdItemId, conversion.Value);
			}
			else
			{
				void CheckSplit(string field, string uomLabel)
				{
					var cell = logicalRow.Cell(field);
					if (cell is null || IsCellEffectivelyEmpty(cell)) return;
					var normalizedUomName = InvoiceDataValidator.NormalizeUomName(uomLabel);
					var conversion = TryMatchConversion(resolvedItem, normalizedUomName);
					if (conversion.HasValue) Record(resolvedItem.SubdItemId, conversion.Value);
				}

				CheckSplit("CaseQuantity", "case");
				CheckSplit("DozenQuantity", "dozen");
				CheckSplit("PieceQuantity", "piece");
				CheckSplit("InBoxQuantity", "inbox");
			}
		}

		return result;
	}

	private static string BuildChoiceKey(ImportedInvoiceRow row, bool includeUom)
	{
		var basis = !string.IsNullOrWhiteSpace(row.SkuCode) ? row.SkuCode : row.ItemName;
		var key = Normalize(basis ?? string.Empty);
		return includeUom ? $"{key}||{Normalize(row.UOM)}" : key;
	}
	private static string BuildCustomerCandidatePart(IEnumerable<int> ids) =>
		"?" + string.Join(",", ids.OrderBy(i => i));

	private static string BuildCustomerGroupPart(ImportedInvoiceRow row)
	{
		if (row.CustomerCandidateIds is { Count: > 1 } ids)
			return BuildCustomerCandidatePart(ids);

		return row.ResolvedCustomerId > 0
			? row.ResolvedCustomerId.ToString(CultureInfo.InvariantCulture)
			: (row.ResolvedCustomerCode ?? row.CustomerCode ?? row.CustomerName ?? string.Empty).Trim().ToUpperInvariant();
	}

	private static string BuildCustomerAddress(Data.Customer c) =>
		string.Join(", ", new[] { c.AddressLine, c.City, c.Province, c.ZipCode?.ToString() }
			.Where(s => !string.IsNullOrWhiteSpace(s)));
	private static string BuildChoiceLabel(ImportedInvoiceRow row) =>
		!string.IsNullOrWhiteSpace(row.SkuCode) ? row.SkuCode : row.ItemName;

}