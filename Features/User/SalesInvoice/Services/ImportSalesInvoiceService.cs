using System.Globalization;
using System.Xml;
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
	private readonly ImportLogService _importLog;
	private readonly ImportFileStore _importFiles;
	private readonly ImportFileService? _importFileService;

	public ImportSalesInvoiceService(
		IDbContextFactory<EntrielContext> contextFactory,
		ISalesInvoiceService salesInvoiceService,
		IImportTemplateService templates,
		ImportLogService importLog,
		ImportFileStore importFiles,
		ILogger<ImportSalesInvoiceService> logger,
		ImportFileService? importFileService = null)
	{
		_contextFactory = contextFactory;
		_salesInvoiceService = salesInvoiceService;
		_templates = templates;
		_importLog = importLog;
		_importFiles = importFiles;
		_importFileService = importFileService;
		_logger = logger;
	}

	public async Task<ImportSalesInvoiceResult> ImportFromExcelAsync(
		Stream excelStream,
		string fileName,
		int subDistributorId,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		var result = await PrepareFromExcelAsync(excelStream, fileName, subDistributorId, currentUserId, cancellationToken);
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
		string fileName,
		int subDistributorId,
		int currentUserId,
		CancellationToken cancellationToken = default)
		=> PrepareFromExcelAsync(excelStream, subDistributorId, currentUserId, null, int.MaxValue, cancellationToken, fileName);

	public async Task<ImportSalesInvoiceResult> PrepareFromExcelAsync(
		Stream excelStream,
		int subDistributorId,
		int currentUserId,
		ImportTemplateEditDto? template,
		int maxRows,
		CancellationToken cancellationToken = default,
		string? fileName = null)   // null = not a real import (the Template Tester's preview): nothing is logged
	{
		var result = new ImportSalesInvoiceResult();

		// Basic validations before processing. These reject the request without creating an import batch.
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

		// One Check File = one batch. Created before the template and workbook steps so their failures can be logged against it.
		// Only real imports have a file name; the Template Tester's preview passes none and so never creates a batch.
		// A null batchId (preview, or logging unavailable) means no further logging; the import carries on regardless.
		var batchId = fileName is null
			? null
			: await _importLog.CreateBatchAsync("SalesInvoice", fileName, subDistributorId, null, currentUserId, cancellationToken);
		result.ImportBatchId = batchId;

		if (excelStream is null || !excelStream.CanRead)
		{
			await FailFileUnreadableAsync(batchId, fileName ?? string.Empty, "StreamNotReadable",
				"The upload stream was missing or could not be read.", cancellationToken);
			result.AddError(0, string.Empty, "Import file is missing or unreadable.");
			return result;
		}

		// Keep the original upload under its batch id, before anything else can fail, so even an unreadable or rejected file can be inspected later.
		// Identical content is stored once and shared by every batch that uploads it (ImportBatch.ImportFileId). If that cannot be done
		// for any reason, the file is saved the old way under its own batch folder, so an upload is never lost because of deduplication.
		if (batchId is int storedBatchId)
		{
			var attached = _importFileService is null
				? null
				: await _importFileService.StoreAndAttachAsync(storedBatchId, fileName!, excelStream, cancellationToken);

			if (attached is null)
				await _importFiles.SaveAsync(storedBatchId, fileName!, excelStream, cancellationToken);
		}

		try
		{
			template ??= await _templates.ResolveForImportAsync("SalesInvoice", subDistributorId, cancellationToken);
		}
		catch (TemplateSelectionRequiredException ex)
		{
			// More than one template could apply and nothing has chosen one. Never guess: stop with a clear message.
			await FailPreparationAsync(batchId, ImportErrorTypes.TemplateSelectionRequired, ex.Message,
				new { ex.ImportType, SubDistributorId = subDistributorId, ex.CandidateNames }, cancellationToken);
			result.AddError(0, string.Empty, ex.Message);
			return result;
		}
		if (template is null)
		{
			await FailPreparationAsync(batchId, ImportErrorTypes.TemplateNotFound,
				"No active import template was found for this subdistributor and there is no global default.",
				new { ImportType = "SalesInvoice", SubDistributorId = subDistributorId }, cancellationToken);
			result.AddError(0, string.Empty,
				"No active import template was found for this subdistributor and there is no global default. Ask an admin to set one up under Import Templates.");
			return result;
		}

		// The template decides which sheets, which header rows and which columns.
		var openedWorkbook = await TryOpenWorkbookAsync(excelStream, fileName ?? string.Empty, batchId, result, cancellationToken);
		if (openedWorkbook is null)
			return result;
		using var workbook = openedWorkbook;

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

			// Every blocking reason becomes one error on this batch; the batch is closed as Failed once.
			await FailPreparationAsync(batchId,
				plan.ErrorDetails.Select(e => new PreparationError(
					MapPlanErrorKind(e.Kind), e.Message, Data: new { e.SheetIndex, e.SheetName, e.Detail })),
				cancellationToken);
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
			{
				await FailPreparationAsync(batchId, ImportErrorTypes.NoInvoiceRows,
					"No invoice rows were found in the template.",
					new { SheetsRead = plan.Sheets.Select(s => s.Inspection.Name).ToList() }, cancellationToken);
				result.AddError(0, string.Empty, "No invoice rows were found in the template.");
			}
			else
			{
				// Every row had an error. The file was read fine, so the batch is Prepared (then Cancelled when closed), not Failed.
				await RecordPreparedAsync(batchId, result, subDistributorId, cancellationToken);
			}
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
					result.AddError(invoiceRows[0].Source, invoiceNumber, groupConsistencyError, code: ImportErrorTypes.InvoiceInconsistent, value: groupConsistencyError);
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
					result.AddError(firstRow.Source, invoiceNumber, msg, "CustomerCode",
						code: string.IsNullOrWhiteSpace(firstRowCustomerValue) ? ImportErrorTypes.CustomerMissing : ImportErrorTypes.CustomerNotFound,
						value: firstRowCustomerValue,
						details: new Dictionary<string, string?> { ["CustomerCode"] = firstRow.CustomerCode?.Trim(), ["CustomerName"] = firstRow.CustomerName?.Trim() });
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
					result.AddError(firstRow.Source, invoiceNumber, msg, "OrderType", code: ImportErrorTypes.OrderTypeInvalid, value: firstRow.OrderType);
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
							result.AddError(row.Source, invoiceNumber, msg, "SkuCode", code: ImportErrorTypes.SkuNotFound, value: itemKey);
						}
						items.Clear();
						break;
					}

					if (row.ResolvedItemsUomId == 0 || !itemsUomById.TryGetValue(row.ResolvedItemsUomId, out var uom))
					{
						var itemNameSuffix = !string.IsNullOrWhiteSpace(subdItem.ItemName) ? $" ({subdItem.ItemName})" : string.Empty;
						var msg = $"UOM '{row.UOM}' was not found for SKU '{row.SkuCode}'{itemNameSuffix}.";
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(row.RowNumber, invoiceNumber, msg, string.Empty, BuildCustomerDisplayValue(row.CustomerCode, row.CustomerName), "UOM", SheetIndex: row.SheetIndex));
						result.AddError(row.Source, invoiceNumber, msg, "UOM", code: ImportErrorTypes.UomNotFound, value: row.UOM, context: row.SkuCode,
							details: UomDetails(row.SkuCode, subdItem.ItemName, row.ItemName, row.UOM));
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
						result.AddError(firstRow.Source, invoiceNumber, msg, code: ImportErrorTypes.InvoiceNoValidLines);
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
					// A blank salesman is reported on every affected row as SalesmanMissing, so the header check's own "Sales man name is
					// required." is not shown or logged again. Every other header error is unchanged: with no salesman problem, loggedErrors
					// is validationErrors, so the shown message, the logged code and the logged value are exactly what they were.
					var loggedErrors = validationErrors
						.Where(e => e.Key != SalesInvoiceValidation.Header.SalesManName.Key)
						.ToDictionary(e => e.Key, e => e.Value);
					if (loggedErrors.Count > 0)
					{
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
							firstRow.RowNumber,
							invoiceNumber,
							string.Join(" ", loggedErrors.Values),
							string.Empty,
							string.Empty,
							SheetIndex: firstRow.SheetIndex));
					}
					if (validationErrors.ContainsKey(SalesInvoiceValidation.Header.SalesManName.Key))
					{
						// The invoice must stay blocked (an invoice with any issue is not ready to commit). This is the same issue the row-level
						// check raised for this row (same row, column and message), so the results page lists it once. It is not logged here:
						// the row-level one already is.
						preparedInvoice.Issues.Add(new ImportSalesInvoiceIssue(
							firstRow.RowNumber,
							invoiceNumber,
							SalesmanMissingMessage,
							string.Empty,
							string.Empty,
							"SalesManName",
							SheetIndex: firstRow.SheetIndex,
							Code: ImportErrorTypes.SalesmanMissing));
					}
					if (loggedErrors.Count > 0)
					{
						var loggedMsg = string.Join(" ", loggedErrors.Values);
						// A duplicate invoice number on its own is the rule working as intended: shown in the results, never logged.
						var onlyDuplicate = loggedErrors.Count == 1
							&& loggedErrors.TryGetValue(SalesInvoiceValidation.Header.InvoiceNumber.Key, out var existsMessage)
							&& existsMessage == SalesInvoiceValidation.InvoiceNumberExistsMessage;
						result.AddError(firstRow.Source, invoiceNumber, loggedMsg,
							code: onlyDuplicate ? ImportRowErrorAggregator.InvoiceDuplicateCode : ImportErrorTypes.InvoiceHeaderInvalid, value: loggedMsg);
					}
					result.PreparedInvoices.Add(preparedInvoice);
					continue;
				}

				result.PreparedInvoices.Add(preparedInvoice);
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				_logger.LogError(ex, "Failed to prepare sales invoice {InvoiceNumber} from row {RowNumber}: {Message}", invoiceNumber, invoiceRows[0].RowNumber, baseMsg);
				result.AddError(invoiceRows[0].Source, invoiceNumber, $"Unexpected error while preparing invoice '{invoiceNumber}': {baseMsg}",
					code: ImportErrorTypes.UnexpectedPrepareError, value: baseMsg);
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

		// The file was read and prepared; the batch stays Prepared until it is committed, closed or fails.
		await RecordPreparedAsync(batchId, result, subDistributorId, cancellationToken);

		return result;
	}

	/// <summary>
	/// Call after each commit, with the result CommitPreparedInvoicesAsync returned. Moves the batch created at Check File to
	/// Committed (every invoice saved, no sheet skipped) or PartiallyCommitted (some saved). When nothing is saved and this commit
	/// tried invoices that all failed, the batch becomes Failed. A later commit that does save invoices moves it on from Failed.
	/// Nothing attempted leaves it as it was. Never creates a batch.
	/// </summary>
	public async Task RecordCommitOutcomeAsync(ImportSalesInvoiceResult? importResult, ImportSalesInvoiceResult? commitResult = null, CancellationToken cancellationToken = default)
	{
		if (importResult?.ImportBatchId is not int batchId) return;

		var saved = importResult.PreparedInvoices.Count(p => p.IsSaved);
		if (saved == 0)
		{
			if (commitResult is { ImportedInvoiceCount: 0 } && commitResult.Issues.Count > 0)
				await _importLog.CompleteBatchAsync(batchId, ImportBatchStatus.Failed,
					onlyIfCurrentStatus: new[] { ImportBatchStatus.Prepared }, cancellationToken: cancellationToken);
			return;
		}

		var everything = saved == importResult.PreparedInvoices.Count && !importResult.HasSkippedSheets;
		await _importLog.CompleteBatchAsync(
			batchId,
			everything ? ImportBatchStatus.Committed : ImportBatchStatus.PartiallyCommitted,
			onlyIfCurrentStatus: new[] { ImportBatchStatus.Prepared, ImportBatchStatus.PartiallyCommitted, ImportBatchStatus.Failed },
			cancellationToken: cancellationToken);
	}

	/// <summary>Call when the user closes the results screen. A batch that was never committed becomes Cancelled; any other status is left alone.</summary>
	public Task RecordResultsClosedAsync(ImportSalesInvoiceResult? importResult, CancellationToken cancellationToken = default)
		=> importResult?.ImportBatchId is int batchId
			? _importLog.CompleteBatchAsync(batchId, ImportBatchStatus.Cancelled,
				onlyIfCurrentStatus: new[] { ImportBatchStatus.Prepared }, cancellationToken: cancellationToken)
			: Task.CompletedTask;

	/// <summary>
	/// Call after each commit, with the result CommitPreparedInvoicesAsync returned. Saves that commit's own failures (duplicate invoice,
	/// save failed, unexpected exception) against the batch created at Check File. Only <paramref name="commitResult"/> is logged, never
	/// importResult.Issues, which still holds the preparation errors already saved at Check File, so nothing is logged twice.
	/// Best-effort: a logging problem is written to ILogger and never reaches the import.
	/// </summary>
	public async Task RecordCommitErrorsAsync(ImportSalesInvoiceResult? importResult, ImportSalesInvoiceResult? commitResult, CancellationToken cancellationToken = default)
	{
		if (importResult?.ImportBatchId is not int batchId || commitResult is null || commitResult.Issues.Count == 0) return;

		try
		{
			var subDistributorId = importResult.PreparedInvoices.Select(p => p.Invoice?.SubdistributorId ?? 0).FirstOrDefault(id => id > 0);
			var entries = ImportRowErrorAggregator.Aggregate(importResult, commitResult.Issues, subDistributorId);
			await _importLog.LogErrorsAsync(batchId, entries, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Import log: could not record commit errors for batch {ImportBatchId}.", batchId);
		}
	}

	// Opens the workbook. Only exceptions that mean "this isn't a readable Excel file" are handled here;
	// anything else (a bug, a database problem) keeps propagating so it is never mislabelled as FileUnreadable.
	private async Task<XLWorkbook?> TryOpenWorkbookAsync(
		Stream excelStream,
		string fileName,
		int? batchId,
		ImportSalesInvoiceResult result,
		CancellationToken cancellationToken)
	{
		try
		{
			return new XLWorkbook(excelStream);
		}
		catch (Exception ex) when (IsUnreadableWorkbookException(ex))
		{
			// The full exception stays in the server log; the database only gets a short diagnostic.
			_logger.LogError(ex, "Could not open uploaded workbook {FileName} ({ExceptionType}).", fileName, ex.GetType().FullName);

			await FailFileUnreadableAsync(batchId, fileName, "InvalidExcelFile",
				"The file content could not be parsed as a valid Excel workbook.", cancellationToken);

			// Same text the results screen showed before logging existed.
			result.AddError(0, string.Empty, $"Failed to open/import file: {ex.Message}.");
			return null;
		}
	}

	private static bool IsUnreadableWorkbookException(Exception ex)
	{
		// ClosedXML can wrap the underlying package error, so look through the inner exceptions too.
		for (Exception? current = ex; current is not null; current = current.InnerException)
		{
			if (current is InvalidDataException          // not a zip, truncated zip, legacy .xls / password-protected container
				|| current is FormatException            // includes System.IO.FileFormatException
				|| current is XmlException               // corrupt part inside the package
				|| current is DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
				return true;
		}
		return false;
	}

	private async Task FailFileUnreadableAsync(
		int? batchId,
		string fileName,
		string diagnosticCode,
		string diagnosticMessage,
		CancellationToken cancellationToken)
		=> await FailPreparationAsync(batchId, ImportErrorTypes.FileUnreadable,
			"The uploaded file could not be read.",
			new { FileName = fileName, DiagnosticCode = diagnosticCode, DiagnosticMessage = diagnosticMessage },
			cancellationToken);

	/// <summary>One blocking reason a file couldn't be prepared. Keep <c>Data</c> small and free of stack traces.</summary>
	private sealed record PreparationError(string Type, string Message, string? Field = null, object? Data = null);

	/// <summary>
	/// Logs every reason against the batch, then closes it as Failed, once. Does nothing without a batch
	/// (the Template Tester's preview, or logging unavailable). Only a batch still Prepared is changed.
	/// </summary>
	private async Task FailPreparationAsync(int? batchId, IEnumerable<PreparationError> errors, CancellationToken cancellationToken)
	{
		if (batchId is not int id) return;

		foreach (var e in errors)
			await _importLog.LogErrorAsync(id, e.Type, e.Message, e.Field, data: e.Data, cancellationToken: cancellationToken);

		await _importLog.CompleteBatchAsync(id, ImportBatchStatus.Failed,
			onlyIfCurrentStatus: new[] { ImportBatchStatus.Prepared }, cancellationToken: cancellationToken);
	}

	private Task FailPreparationAsync(int? batchId, string errorType, string message, object? data, CancellationToken cancellationToken)
		=> FailPreparationAsync(batchId, new[] { new PreparationError(errorType, message, Data: data) }, cancellationToken);

	/// <summary>
	/// The file was read and prepared: save its row-level errors, record how many rows were read, and leave the batch
	/// Prepared until it is committed, closed or fails. Row errors never fail a batch, even when nothing is committable.
	/// </summary>
	private async Task RecordPreparedAsync(int? batchId, ImportSalesInvoiceResult result, int subDistributorId, CancellationToken cancellationToken)
	{
		if (batchId is not int id) return;

		await LogRowErrorsAsync(id, result, subDistributorId, cancellationToken);

		await _importLog.CompleteBatchAsync(id, ImportBatchStatus.Prepared, result.TotalRowsProcessed,
			onlyIfCurrentStatus: new[] { ImportBatchStatus.Prepared }, cancellationToken: cancellationToken);
	}

	// Best-effort: whatever goes wrong while aggregating or saving is written to ILogger and never reaches the import.
	private async Task LogRowErrorsAsync(int batchId, ImportSalesInvoiceResult result, int subDistributorId, CancellationToken cancellationToken)
	{
		if (result.Issues.Count == 0) return;

		try
		{
			var entries = ImportRowErrorAggregator.Aggregate(result, subDistributorId);
			await _importLog.LogErrorsAsync(batchId, entries, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Import log: could not aggregate row errors for batch {ImportBatchId}.", batchId);
		}
	}

	private static string MapPlanErrorKind(PlanErrorKind kind) => kind switch
	{
		PlanErrorKind.RequiredSheetMissing => ImportErrorTypes.RequiredSheetMissing,
		PlanErrorKind.AmbiguousSheet => ImportErrorTypes.AmbiguousSheet,
		PlanErrorKind.TruncatedSheetMatch => ImportErrorTypes.TruncatedSheetMatch,
		PlanErrorKind.EmptySheet => ImportErrorTypes.EmptySheet,
		PlanErrorKind.HeaderRowNotFound => ImportErrorTypes.HeaderRowNotFound,
		PlanErrorKind.AmbiguousColumns => ImportErrorTypes.AmbiguousColumns,
		PlanErrorKind.MissingRequiredHeaders => ImportErrorTypes.MissingRequiredHeaders,
		PlanErrorKind.NoReadableSheet => ImportErrorTypes.NoReadableSheet,
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped plan error kind.")
	};

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

		void FailInvoice(PreparedInvoice p, string msg, string code, string value)
		{
			var firstItem = p.Items.FirstOrDefault();
			// A source row, or the file as a whole when the item has none. LineItemId is not a row number.
			var source = firstItem?.SourceRows.FirstOrDefault() ?? default;
			p.Issues.Add(new ImportSalesInvoiceIssue(source.RowNumber, p.InvoiceNumber, msg, string.Empty, string.Empty, SheetIndex: source.SheetIndex));
			result.AddError(source, p.InvoiceNumber, msg, code: code, value: value);
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
					FailInvoice(prepared, $"Sales invoice '{prepared.InvoiceNumber}' already exists for this order type, customer and salesman.",
						ImportRowErrorAggregator.InvoiceDuplicateCode, prepared.InvoiceNumber);
					continue;
				}
				if (!saveResult.IsSaved)
				{
					var failMessage = saveResult.ErrorMessage ?? "Unable to save invoice.";
					FailInvoice(prepared, failMessage, ImportErrorTypes.InvoiceSaveFailed, failMessage);
					continue;
				}

				prepared.IsSaved = true;
				result.ImportedInvoiceCount++;
				result.ImportedRowCount += prepared.Items?.Count ?? 0;
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				_logger.LogError(ex, "Failed to save sales invoice {InvoiceNumber}: {Message}", prepared.InvoiceNumber, baseMsg);
				FailInvoice(prepared, $"Unexpected error while saving invoice '{prepared.InvoiceNumber}': {baseMsg}",
					ImportErrorTypes.UnexpectedCommitError, baseMsg);
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

			// Every row with a blank salesman is reported on its own, whatever else is wrong with it. The invoice-level header check only
			// sees the first row of each invoice group, so it can never find the others. This is raised BEFORE issueStart is taken, so it
			// is not counted as one of the row's own issues: the row still parses, groups and blocks other rows exactly as before.
			// The error identity is the same for every row (no value, no context), so they all aggregate into one master error.
			if (!string.IsNullOrWhiteSpace(invoiceCode) && string.IsNullOrWhiteSpace(salesManName))
			{
				result.AddError(new SourceRowKey(sheetIndex, rowNumber), invoiceCode.Trim(), SalesmanMissingMessage,
					columnName: "SalesManName", code: ImportErrorTypes.SalesmanMissing);
			}

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

			void AddRowError(string message, string? columnName = null, int? suggestionCount = null, string? code = null, string? value = null, string? context = null, IReadOnlyDictionary<string, string?>? details = null)
			{
				result.AddError(new SourceRowKey(sheetIndex, rowNumber), invoiceCode, message, columnName, suggestionCount, code, value, context, details);
				rowHasErrors = true;
			}

			//-------Validate required fields------------
			if (string.IsNullOrWhiteSpace(invoiceCode))
				AddRowError("Invoice code is required.", "InvoiceCode", code: ImportErrorTypes.InvoiceCodeMissing);
			if (IsMissingOrEmpty(logicalRow.Cell("InvoiceDate")))
				AddRowError("Invoice date is required.", "InvoiceDate", code: ImportErrorTypes.InvoiceDateMissing);
			else if (!TryGetDateOnly(logicalRow.Cell("InvoiceDate")!, out invoiceDate, invoiceDateFormat))
				AddRowError("Invoice date is invalid.", "InvoiceDate", code: ImportErrorTypes.InvoiceDateInvalid, value: GetString(logicalRow.Cell("InvoiceDate")!));

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
					AddRowError("Customer code or name is required.", "CustomerCode", code: ImportErrorTypes.CustomerMissing);
				else
				{
					var label = BuildCustomerDisplayValue(customerCode, customerName);
					var colName = string.IsNullOrWhiteSpace(customerName) ? "CustomerCode" : "CustomerName";
					var message = customerSuggestions is { Count: > 0 }
						? $"Customer '{label}' was not found. Did you mean: {string.Join(", ", customerSuggestions.Select(s => BuildCustomerDisplayValue(s.CustomerCode, s.CustomerName)))}?"
						: $"Customer '{label}' was not found.";
					AddRowError(message, colName, customerSuggestions?.Count, ImportErrorTypes.CustomerNotFound, label,
						details: new Dictionary<string, string?> { ["CustomerCode"] = customerCode?.Trim(), ["CustomerName"] = customerName?.Trim() });
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
					AddRowError($"Order type '{orderType}' is invalid. Use Invoice or Credit.", "OrderType", code: ImportErrorTypes.OrderTypeInvalid, value: orderType);
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
					AddRowError("SKU code or Item name is required.", hasItemNameColumn ? "ItemName" : "SkuCode", code: ImportErrorTypes.SkuMissing);
				}
				else if (!string.IsNullOrWhiteSpace(skuCode) && !string.IsNullOrWhiteSpace(itemName))
				{
					var label = BuildCustomerDisplayValue(skuCode, itemName);
					var colName = "SkuCode";
					var message = itemSuggestions is { Count: > 0 }
						? $"SKU '{label}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => BuildCustomerDisplayValue(s.SubdItemCode, s.ItemName)))}?"
						: $"SKU '{label}' was not found.";
					AddRowError(message, colName, itemSuggestions?.Count, ImportErrorTypes.SkuNotFound, skuCode);
				}
				else if (!string.IsNullOrWhiteSpace(skuCode))
				{
					var message = itemSuggestions is { Count: > 0 }
						? $"SKU '{skuCode}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => BuildCustomerDisplayValue(s.SubdItemCode, s.ItemName)))}?"
						: $"SKU '{skuCode}' was not found.";
					AddRowError(message, "SkuCode", itemSuggestions?.Count, ImportErrorTypes.SkuNotFound, skuCode);
				}
				else
				{
					var message = itemSuggestions is { Count: > 0 }
						? $"Item name '{itemName}' was not found. Did you mean: {string.Join(", ", itemSuggestions.Select(s => BuildCustomerDisplayValue(s.SubdItemCode, s.ItemName)))}?"
						: $"Item name '{itemName}' was not found.";
					AddRowError(message, "ItemName", itemSuggestions?.Count, ImportErrorTypes.SkuNotFound, itemName);
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
						AddRowError("Quantity is required.", "Quantity", code: ImportErrorTypes.QuantityMissing);
					else if (rawQuantity is null)
						AddRowError("Quantity must be a whole number.", "Quantity", code: ImportErrorTypes.QuantityInvalid, value: GetString(quantityCell!));
					else if (InvoiceDataValidator.IsMissingUomValue(uom))
						AddRowError("UOM is required.", "UOM", code: ImportErrorTypes.UomMissing);
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
							AddRowError($"UOM '{uom}' was not found for SKU '{skuCode}'{itemNameSuffix}.", "UOM", code: ImportErrorTypes.UomNotFound, value: uom, context: skuCode,
								details: UomDetails(skuCode, resolvedItem.ItemName, itemName, uom));
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
							CustomerCode: customerCode ?? string.Empty,
							CustomerName: customerName ?? string.Empty,
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
						AddRowError($"{errorField} must be a whole number.", errorField, code: ImportErrorTypes.QuantityInvalid, value: rawValue);
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
							AddRowError($"UOM '{normalizedUomName}' was not found for SKU '{skuCode}'{itemNameSuffix}.", errorField, code: ImportErrorTypes.UomNotFound, value: normalizedUomName, context: skuCode,
								details: UomDetails(skuCode, resolvedItem.ItemName, itemName, normalizedUomName));
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
						CustomerCode: customerCode ?? string.Empty,
						CustomerName: customerName ?? string.Empty,
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
					AddRowError("At least one quantity column (Case, Dozen, Piece, or InBox) is required.", errorCol, code: ImportErrorTypes.NoQuantityColumnValue);
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

	private const string SalesmanMissingMessage = "Salesman name is missing.";

	/// <summary>
	/// Evidence-only facts for a "UOM not found" row: the item code, the item's name (Entriel's own name when the item was found, otherwise
	/// the file's) and the unit as uploaded. Never part of the error's identity: that stays the unit (Value) and the SKU (Context).
	/// </summary>
	private static IReadOnlyDictionary<string, string?> UomDetails(string? itemCode, string? masterItemName, string? uploadedItemName, string? uploadedUom) =>
		new Dictionary<string, string?>
		{
			["ItemCode"] = itemCode?.Trim(),
			["ItemName"] = !string.IsNullOrWhiteSpace(masterItemName) ? masterItemName.Trim() : uploadedItemName?.Trim(),
			["UploadedUom"] = uploadedUom?.Trim()
		};

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