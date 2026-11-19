using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Models;
using STTproject.Services;
using PricingStructure = STTproject.Features.Admin.PriceIncrease.DTOs.PricingStructure;
using PricingStructureResolver = STTproject.Features.Admin.PriceIncrease.DTOs.PricingStructureResolver;
using PriceListTypes = STTproject.Features.Admin.Subdistributor.DTOs.PriceListTypes;

namespace STTproject.Features.User.MapItem.Services;

public sealed class ImportMapItemService
{
	private readonly IDbContextFactory<EntrielContext> _contextFactory;
	private readonly IMapItemService _mapItemService;
	private readonly ILogger<ImportMapItemService> _logger;

	public ImportMapItemService(
		IDbContextFactory<EntrielContext> contextFactory,
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
		if (currentUserId <= 0)
		{
			var errorResult = new ImportMapItemResult();
			errorResult.Rows.Add(new ImportMapItemRowResult
			{
				RowNumber = 0,
				IsSuccess = false,
				Message = "Unable to identify the current user. Please sign in again."
			});
			errorResult.Rows[0].Issues.Add("Unable to identify the current user. Please sign in again.");
			return errorResult;
		}

		return await PrepareFromExcelAsync(excelStream, currentUserId, cancellationToken);
	}

	public async Task<int> CommitPreparedRowsAsync(
		IEnumerable<ImportMapItemRowResult> rows,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		if (currentUserId <= 0 || rows is null)
		{
			return 0;
		}

		var validRows = rows
			.Where(row => row.Issues.Count == 0 && row.IsSuccess)
			.ToList();

		if (validRows.Count == 0)
		{
			return 0;
		}

		return await CommitMapItemRowsAsync(validRows, currentUserId, cancellationToken);
	}

	public async Task<ImportMapItemResult> PrepareFromExcelAsync(
		Stream excelStream,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		var result = new ImportMapItemResult();

		if (excelStream is null || !excelStream.CanRead)
		{
			var errorRow = new ImportMapItemRowResult
			{
				RowNumber = 0,
				IsSuccess = false,
				Message = "Import file is missing or unreadable."
			};
			errorRow.Issues.Add("Import file is missing or unreadable.");
			result.Rows.Add(errorRow);
			return result;
		}

		using var workbook = new XLWorkbook(excelStream);
		var worksheet = workbook.Worksheets.FirstOrDefault() ?? workbook.Worksheets.First();

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
			"Conversion"
		};
		result.OriginalHeaders = headers
			.OrderBy(kvp => kvp.Value)
			.Select(kvp => kvp.Key)
			.ToList();
		var missingHeaders = requiredHeaders
			.Where(header => !headers.ContainsKey(header))
			.ToList();

		if (missingHeaders.Count > 0)
		{
			var errorRow = new ImportMapItemRowResult
			{
				RowNumber = 0,
				IsSuccess = false,
				Message = $"Missing required column(s): {string.Join(", ", missingHeaders)}."
			};
			errorRow.Issues.Add($"Missing required column(s): {string.Join(", ", missingHeaders)}.");
			result.Rows.Add(errorRow);
			return result;
		}

		var parsedRows = ReadMapItemRows(worksheet, headers, result);
		if (parsedRows.Count == 0)
		{
			var errorRow = new ImportMapItemRowResult
			{
				RowNumber = 0,
				IsSuccess = false,
				Message = "No mapped item rows were found in the template."
			};
			errorRow.Issues.Add("No mapped item rows were found in the template.");
			result.Rows.Add(errorRow);
			return result;
		}

		var crossGroupIdentityErrors = BuildSubdItemIdentityConflictsByRow(parsedRows);

		// Load reference data from database
		await using var context = _contextFactory.CreateDbContext();

		var isAdmin = await IsAdminAsync(context, currentUserId, cancellationToken);

		// Get subdistributor mapping — admins see all active subdistributors,
		// non-admins only see the ones they encoded themselves.
		var subdDistributorsQuery = context.SubDistributors
			.AsNoTracking()
			.Where(s => s.IsActive);

		if (!isAdmin)
		{
			subdDistributorsQuery = subdDistributorsQuery.Where(s => s.EncoderId == currentUserId);
		}

		var subdDistributors = await subdDistributorsQuery
			.ToDictionaryAsync(s => Normalize(s.SubdCode), cancellationToken);

		// Stock UOM = company StockUomName, matched by name, built-in spellings and the subd's UOM synonyms.
		var uomMatcher = await UomNameMatcher.LoadAsync(
			context, subdDistributors.Values.Select(sd => sd.SubDistributorId), cancellationToken);

		var companyItems = await context.CompanyItems
			.AsNoTracking()
			.Where(ci => ci.IsActive)
			.ToDictionaryAsync(ci => Normalize(ci.ItemCode), cancellationToken);

		// Preload any existing SubdItems for the subdistributors found in the file
		var parsedSubdCodes = parsedRows.Select(r => Normalize(r.SubDistributorCode)).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
		var subdIds = subdDistributors
			.Where(kvp => parsedSubdCodes.Contains(kvp.Key))
			.Select(kvp => kvp.Value.SubDistributorId)
			.ToList();

		var existingSubdItems = new List<SubdItem>();
		if (subdIds.Count > 0)
		{
			existingSubdItems = await context.SubdItems
				.AsNoTracking()
				.Where(si => si.IsActive && subdIds.Contains(si.SubDistributorId))
				.ToListAsync(cancellationToken);
		}

		var existingBySubdCodeCompanyAndItem = new HashSet<(string, string, string, string)>();
		var companyItemCodesById = companyItems.Values.ToDictionary(ci => ci.CompanyItemId, ci => Normalize(ci.ItemCode));
		foreach (var si in existingSubdItems)
		{
			var subdCode = subdDistributors.FirstOrDefault(k => k.Value.SubDistributorId == si.SubDistributorId).Key ?? string.Empty;
			if (!companyItemCodesById.TryGetValue(si.CompanyItemId, out var companyItemCode))
			{
				continue;
			}

			var key = (Normalize(subdCode), Normalize(si.SubdItemCode), companyItemCode, Normalize(si.ItemName));
			existingBySubdCodeCompanyAndItem.Add(key);
		}

		// Group rows by the item identity shown in the UI.
		foreach (var rowGroup in parsedRows.GroupBy(row => new
		{
			SubDistributorCode = Normalize(row.SubDistributorCode),
			Principal = Normalize(row.Principal),
			CompanyItemCode = Normalize(row.CompanyItemCode),
			CompanyItemName = Normalize(row.CompanyItemName),
			SubdItemCode = Normalize(row.SubdItemCode),
			SubdItemName = Normalize(row.SubdItemName)
		}))
		{
			var groupRows = ResolveConversionsForGroup(rowGroup.OrderBy(row => row.RowNumber).ToList());
			var firstRow = groupRows[0];
			if (!subdDistributors.TryGetValue(Normalize(firstRow.SubDistributorCode), out var subdDistributor) || subdDistributor is null)
			{
				var createdRows = new List<ImportMapItemRowResult>();
				foreach (var row in groupRows)
				{
					var rowResult = new ImportMapItemRowResult
					{
						RowNumber = row.RowNumber,
						SubDistributorCode = row.SubDistributorCode,
						SubDistributorName = string.Empty,
						Principal = row.Principal,
						CompanyItemCode = row.CompanyItemCode,
						CompanyItemName = row.CompanyItemName,
						SubdItemCode = row.SubdItemCode,
						SubdItemName = row.SubdItemName,
						IsSuccess = false,
						Message = $"SubDistributor '{firstRow.SubDistributorCode}' not found."
					};
					ApplyRawValues(rowResult, row.RawValues);
					rowResult.Issues.Add($"SubDistributor '{firstRow.SubDistributorCode}' not found.");
					result.Rows.Add(rowResult);
					createdRows.Add(rowResult);
				}

				var prepared = new PreparedItemGroup(createdRows) { Selected = false };
				prepared.Issues.Add(new ImportMapItemIssue(firstRow.RowNumber, firstRow.SubdItemCode, $"SubDistributor '{firstRow.SubDistributorCode}' not found."));
				result.PreparedGroups.Add(prepared);

				continue;
			}

			// Validate row consistency against the first row in the code group.
			var groupWarnings = new Dictionary<int, List<string>>();
			var rowErrors = ValidateGroupConsistency(groupRows, groupWarnings);
			MergeRowErrors(rowErrors, groupRows, crossGroupIdentityErrors);

			// A unit counted in itself (PC = 2 PC) can't be worked out: block it so the preview and the save can't disagree.
			foreach (var selfRow in groupRows.Where(r => r.Conversion is not null && r.Conversion != 1
														 && !string.IsNullOrWhiteSpace(r.ConversionBasedOn)
														 && Normalize(r.ConversionBasedOn) == Normalize(r.UOM)))
			{
				if (!rowErrors.TryGetValue(selfRow.RowNumber, out var list)) rowErrors[selfRow.RowNumber] = list = new List<string>();
				list.Add($"'{selfRow.UOM}' is counted in itself ({selfRow.UOM} = {selfRow.Conversion} {selfRow.UOM}). " +
						 $"Count it in another unit, or use 1 if it is the base unit.");
			}

			// Validate company item exists
			if (!companyItems.TryGetValue(Normalize(firstRow.CompanyItemCode), out var companyItem))
			{
				var createdRows = new List<ImportMapItemRowResult>();
				foreach (var row in groupRows)
				{
					var rowResult = new ImportMapItemRowResult
					{
						RowNumber = row.RowNumber,
						SubDistributorCode = row.SubDistributorCode,
						SubDistributorName = subdDistributor.SubdName,
						Principal = row.Principal,
						CompanyItemCode = row.CompanyItemCode,
						CompanyItemName = row.CompanyItemName,
						SubdItemCode = row.SubdItemCode,
						SubdItemName = row.SubdItemName,
						IsSuccess = false,
						Message = $"Company Item '{firstRow.CompanyItemCode}' not found."
					};
					ApplyRawValues(rowResult, row.RawValues);
					rowResult.Issues.Add($"Company Item '{firstRow.CompanyItemCode}' not found.");
					result.Rows.Add(rowResult);
					createdRows.Add(rowResult);
				}

				var prepared = new PreparedItemGroup(createdRows) { Selected = false };
				prepared.Issues.Add(new ImportMapItemIssue(firstRow.RowNumber, firstRow.SubdItemCode, $"Company Item '{firstRow.CompanyItemCode}' not found."));
				result.PreparedGroups.Add(prepared);

				continue;
			}

			// A company item without a Stock UOM (e.g. free goods) needs no stock UOM and is priced at 0.
			// Prices are not imported any more: each UOM is the company stock price × conversion.
			var requiresStock = !string.IsNullOrWhiteSpace(companyItem.StockUomName);

			var subdDistributorKey = (
				Normalize(firstRow.SubDistributorCode),
				Normalize(firstRow.SubdItemCode),
				Normalize(firstRow.CompanyItemCode),
				Normalize(firstRow.SubdItemName));
			var alreadyExistsByCode = existingBySubdCodeCompanyAndItem.Contains(subdDistributorKey);

			if (alreadyExistsByCode)
			{
				foreach (var row in groupRows)
				{
					if (!rowErrors.TryGetValue(row.RowNumber, out var issues))
					{
						issues = new List<string>();
						rowErrors[row.RowNumber] = issues;
					}
					issues.Add($"SubdItem code '{firstRow.SubdItemCode}' is already mapped in the database for SubDistributor '{firstRow.SubDistributorCode}' with Company Item '{firstRow.CompanyItemCode}' and description '{firstRow.SubdItemName}'.");
				}
			}

			// File conversions are piece counts. The company stock UOM row gives the pieces per stock UOM,
			// and every conversion is stored as pieces ÷ that (stock UOM = 1).
			bool IsStock(string? uom) => uomMatcher.IsStockUom(subdDistributor.SubDistributorId, uom, companyItem.StockUomName);
			// When the company stock UOM is a piece, a stock-named row of more than 1 piece is the subd's bundle
			// (buy 1 take 1: PC = 2 pieces), not the stock UOM.
			var stockIsPiece = StockUomSuggester.Canon(companyItem.StockUomName) == "PIECE";
			bool IsStockRow(ImportedMapItemRow r) => IsStock(r.UOM) && (!stockIsPiece || r.Conversion is null || r.Conversion == 1);
			var stock = ResolveStockUom(groupRows, companyItem.StockUomName, IsStock, rowErrors, groupWarnings,
					out var fileStockUom, out var suggestedStockUom, out var suggestedStockUomReason);
			var applicablePrice = ResolveApplicablePrice(companyItem, subdDistributor.PriceListType);

			// Shown for reference only (company price × conversion); nothing from the file's Price column is used.
			decimal? ReferencePrice(ImportedMapItemRow r) =>
				!requiresStock ? 0m
				: applicablePrice.Price is decimal p && r.Conversion is int c && c > 0 && stock.StockPieces is decimal sp && sp > 0
					? Math.Round(p * c / sp, 2, MidpointRounding.AwayFromZero)
					: null;

			var hasAnyErrors = rowErrors.Any(kvp => kvp.Value.Count > 0);
			if (hasAnyErrors)
			{
				var createdRows = new List<ImportMapItemRowResult>();
				foreach (var row in groupRows)
				{
					var effectivePrice = ReferencePrice(row);
					var rowIssues = rowErrors.TryGetValue(row.RowNumber, out var issues)
						? issues
						: new List<string>();
					var rowResult = new ImportMapItemRowResult
					{
						RowNumber = row.RowNumber,
						SubDistributorCode = row.SubDistributorCode,
						SubDistributorName = subdDistributor.SubdName,
						Principal = row.Principal,
						CompanyItemCode = row.CompanyItemCode,
						CompanyItemName = row.CompanyItemName,
						SubdItemCode = row.SubdItemCode,
						SubdItemName = row.SubdItemName,
						UomName = row.UOM,
						Conversion = row.Conversion,
						Price = effectivePrice,
						ImportedPrice = effectivePrice,
						PriceFromFile = false,
						StockUom = row.StockUom,
						IsStockUom = IsStockRow(row),
						IsSuccess = rowIssues.Count == 0
					};
					ApplyRawValues(rowResult, row.RawValues);
					rowResult.Issues.AddRange(rowIssues);
					result.Rows.Add(rowResult);
					createdRows.Add(rowResult);
				}

				var prepared = new PreparedItemGroup(createdRows)
				{
					Selected = false,
					StockUomName = stock.Name,
					StockUomSource = stock.Source,
					StockUomReason = stock.Reason,
					FileStockUom = fileStockUom,
					SuggestedStockUom = suggestedStockUom,
					SuggestedStockUomReason = suggestedStockUomReason,
					CompanyStockUomName = companyItem.StockUomName,
					ApplicableStockPrice = applicablePrice.Price,
					PriceListLabel = applicablePrice.Label
				};
				foreach (var kvp in rowErrors)
				{
					foreach (var msg in kvp.Value.Distinct(StringComparer.OrdinalIgnoreCase))
					{
						prepared.Issues.Add(new ImportMapItemIssue(kvp.Key, firstRow.SubdItemCode, msg));
					}
				}
				result.PreparedGroups.Add(prepared);

				continue;
			}

			// Validate all UOM rows
			var uomResults = new List<ImportMapItemRowResult>();

			foreach (var row in groupRows)
			{
				var effectivePrice = ReferencePrice(row);

				var rowResult = new ImportMapItemRowResult
				{
					RowNumber = row.RowNumber,
					SubDistributorCode = row.SubDistributorCode,
					SubDistributorName = subdDistributor.SubdName,
					Principal = row.Principal,
					CompanyItemCode = row.CompanyItemCode,
					CompanyItemName = row.CompanyItemName,
					SubdItemCode = row.SubdItemCode,
					SubdItemName = row.SubdItemName,
					UomName = row.UOM,
					Conversion = row.Conversion,
					Price = effectivePrice,
					ImportedPrice = effectivePrice,
					PriceFromFile = false,
					StockUom = row.StockUom,
					IsStockUom = IsStockRow(row),
					IsSuccess = true
				};
				ApplyRawValues(rowResult, row.RawValues);

				// Carry over non-blocking duplicate-map warnings
				if (groupWarnings.TryGetValue(row.RowNumber, out var rowWarnings))
				{
					rowResult.Warnings.AddRange(rowWarnings);
				}

				// Conversion is a piece count; prices are worked out from it, so priced items need one.
				if (row.Conversion.HasValue && row.Conversion.Value <= 0)
				{
					rowResult.IsSuccess = false;
					rowResult.Issues.Add("Conversion must be a whole number greater than 0 (pieces in one unit).");
				}
				else if (!row.Conversion.HasValue && requiresStock)
				{
					rowResult.IsSuccess = false;
					rowResult.Issues.Add("Conversion is required: enter how many pieces are in one unit.");
				}

				// Validate UOM is not empty
				if (string.IsNullOrWhiteSpace(row.UOM))
				{
					rowResult.IsSuccess = false;
					rowResult.Issues.Add("UOM is required.");
				}

				uomResults.Add(rowResult);
			}

			foreach (var rowResult in uomResults)
			{
				result.Rows.Add(rowResult);
			}

			var preparedSuccess = new PreparedItemGroup(uomResults)
			{
				Selected = false,
				IsSaved = false,
				StockUomName = stock.Name,
				StockUomSource = stock.Source,
				StockUomReason = stock.Reason,
				FileStockUom = fileStockUom,
				SuggestedStockUom = suggestedStockUom,
				SuggestedStockUomReason = suggestedStockUomReason,
				CompanyStockUomName = companyItem.StockUomName,
				ApplicableStockPrice = applicablePrice.Price,
				PriceListLabel = applicablePrice.Label
			};
			// Prices are no longer imported, so there are no price decisions to make.
			preparedSuccess.Selected = uomResults.All(r => r.Issues.Count == 0)
				&& preparedSuccess.StockUomReady;
			foreach (var rr in uomResults.SelectMany(r => r.Issues.Select(i => new ImportMapItemIssue(r.RowNumber, r.SubdItemCode, i))))
			{
				preparedSuccess.Issues.Add(rr);
			}
			result.PreparedGroups.Add(preparedSuccess);
		}

		return result;
	}

	private async Task<int> CommitMapItemRowsAsync(List<ImportMapItemRowResult> rows, int currentUserId, CancellationToken cancellationToken = default)
	{
		if (rows.Count == 0)
			return 0;

		await using var context = _contextFactory.CreateDbContext();

		var isAdmin = await IsAdminAsync(context, currentUserId, cancellationToken);

		// Get all subdistributors for mapping — admins see all active subdistributors,
		// non-admins only see the ones they encoded themselves.
		var subdDistributorsQuery = context.SubDistributors
			.AsNoTracking()
			.Where(s => s.IsActive);

		if (!isAdmin)
		{
			subdDistributorsQuery = subdDistributorsQuery.Where(s => s.EncoderId == currentUserId);
		}

		var subdDistributors = await subdDistributorsQuery
			.ToDictionaryAsync(s => Normalize(s.SubdCode), cancellationToken);

		var companyItems = await context.CompanyItems
			.AsNoTracking()
			.Where(ci => ci.IsActive)
			.ToDictionaryAsync(ci => Normalize(ci.ItemCode), cancellationToken);

		// Group by SubDistributor + SubdItemCode + CompanyItemCode + ItemName (description)
		// to create SubdItems with their UOMs. Including ItemName here is what keeps two
		// rows with the same code/company but a different description as separate SubdItems
		// — each gets its own SubdItemId, so their UOM rows never collide or get merged.
		var groupedBySubdItem = rows
			.GroupBy(r => new
			{
				SubDistributorCode = Normalize(r.SubDistributorCode),
				SubdItemCode = Normalize(r.SubdItemCode),
				CompanyItemCode = Normalize(r.CompanyItemCode),
				ItemName = Normalize(r.SubdItemName)
			})
			.ToList();

		var committedGroups = 0;

		foreach (var subdItemGroup in groupedBySubdItem)
		{
			var subdItemRows = subdItemGroup.ToList();
			var firstRow = subdItemRows[0];
			var subdItemCode = firstRow.SubdItemCode.Trim();
			int? createdSubdItemId = null;

			try
			{
				if (!subdDistributors.TryGetValue(Normalize(firstRow.SubDistributorCode), out var subdDistributor))
				{
					throw new InvalidOperationException($"SubDistributor '{firstRow.SubDistributorCode}' was not found during commit.");
				}

				if (!companyItems.TryGetValue(Normalize(firstRow.CompanyItemCode), out var companyItem) || companyItem is null)
				{
					throw new InvalidOperationException($"Company item '{firstRow.CompanyItemCode}' was not found during commit.");
				}

				var requiresStock = !string.IsNullOrWhiteSpace(companyItem.StockUomName);

				await using var groupContext = _contextFactory.CreateDbContext();

				var existingByCode = await groupContext.SubdItems
					.FirstOrDefaultAsync(
						si => si.SubDistributorId == subdDistributor.SubDistributorId
							&& si.SubdItemCode == subdItemCode
							&& si.CompanyItemId == companyItem.CompanyItemId
							&& si.ItemName == firstRow.SubdItemName,
						cancellationToken);

				if (existingByCode != null)
				{
					throw new InvalidOperationException($"SubdItem code '{subdItemCode}' with description '{firstRow.SubdItemName}' already exists for SubDistributor '{firstRow.SubDistributorCode}' with Company Item '{firstRow.CompanyItemCode}'.");
				}

				var subdItem = new SubdItem
				{
					SubDistributorId = subdDistributor.SubDistributorId,
					CompanyItemId = companyItem.CompanyItemId,
					SubdItemCode = subdItemCode,
					ItemName = firstRow.SubdItemName,
					IsActive = true,
					CreatedDate = DateTime.UtcNow,
					UpdatedDate = DateTime.UtcNow,
					CreatedBy = currentUserId,
					UpdatedBy = currentUserId
				};

				groupContext.SubdItems.Add(subdItem);
				await groupContext.SaveChangesAsync(cancellationToken);
				createdSubdItemId = subdItem.SubdItemId;

				// File conversions are pieces; store them relative to the company stock UOM (stock UOM = 1).
				decimal? stockPieces = null;
				var stockIsPieceUom = StockUomSuggester.Canon(companyItem.StockUomName) == "PIECE";
				var stockRow = requiresStock
					? subdItemRows.FirstOrDefault(r => r.IsStockUom && r.Conversion is > 0 && (!stockIsPieceUom || r.Conversion == 1))
					: null;
				if (requiresStock)
				{
					if (stockIsPieceUom)
					{
						stockPieces = 1m;                          // stock UOM is a piece: piece counts already are stock units (no row needed)
					}
					else if (stockRow is not null)
					{
						stockPieces = stockRow.Conversion is { } sc ? (decimal)sc : null;
					}
					else
					{
						throw new InvalidOperationException(
							$"Add a row for the company stock UOM '{companyItem.StockUomName}' with its piece count before committing (the file's conversions are piece counts).");
					}
				}

				var uomEntries = new Dictionary<string, UomEntry>(StringComparer.OrdinalIgnoreCase);
				foreach (var row in subdItemRows)
				{
					if (string.IsNullOrWhiteSpace(row.UomName))
					{
						continue;
					}

					decimal? pieces = row.Conversion is { } cv ? (decimal)cv : null;
					if (requiresStock && pieces is not > 0)
					{
						continue;   // blocked at Check File; never saved without a conversion
					}

					// Only the one row used as the stock UOM is 1. Another stock-named row (e.g. Pack 66 next to PK 33,
					// a buy 1 take 1 bundle) keeps its own size: 66 ÷ 33 = 2 PK.
					var isStockRow = requiresStock && ReferenceEquals(row, stockRow);
					uomEntries[CanonicalizeUomName(row.UomName)] = new UomEntry
					{
						Conversion = requiresStock
							? (isStockRow ? 1m : Math.Round(pieces!.Value / stockPieces!.Value, 10, MidpointRounding.AwayFromZero))
							: pieces,
						IsStockUom = isStockRow,
						IsActive = true
					};
				}

				var uomSaved = await _mapItemService.SaveSubdItemUomPricesAsync(
					subdItem.SubdItemId, uomEntries, currentUserId, cancellationToken,
					requireStockUom: requiresStock);
				if (!uomSaved)
				{
					throw new InvalidOperationException($"Failed to save UOM rows for SubdItem '{subdItem.SubdItemCode}'.");
				}

				foreach (var row in subdItemRows)
				{
					row.SubdItemId = subdItem.SubdItemId;
				}

				committedGroups++;
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				_logger.LogError(ex, "Failed to commit SubdItem '{SubdItemCode}' for SubDistributor '{SubDistributorCode}': {Message}",
					subdItemCode, firstRow.SubDistributorCode, baseMsg);

				if (createdSubdItemId.HasValue)
				{
					await TryRemoveSubdItemAsync(createdSubdItemId.Value);
				}

				foreach (var row in subdItemRows)
				{
					row.Issues.Add($"Save failed: {baseMsg}");
					row.IsSuccess = false;
				}
			}
		}

		return committedGroups;
	}

	private async Task TryRemoveSubdItemAsync(int subdItemId)
	{
		try
		{
			await using var cleanupContext = _contextFactory.CreateDbContext();
			var entity = await cleanupContext.SubdItems.FindAsync(subdItemId);
			if (entity != null)
			{
				cleanupContext.SubdItems.Remove(entity);
				await cleanupContext.SaveChangesAsync();
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Could not remove the incomplete SubdItem {SubdItemId} after a failed commit.", subdItemId);
		}
	}

	private static async Task<bool> IsAdminAsync(
		EntrielContext context,
		int currentUserId,
		CancellationToken cancellationToken)
	{
		return await context.Users
			.AsNoTracking()
			.Where(u => u.UserId == currentUserId)
			.Select(u => u.Role == "Admin")
			.FirstOrDefaultAsync(cancellationToken);
	}

	private static string CanonicalizeUomName(string? uom)
	{
		return (uom ?? string.Empty).Trim();
	}

	private static List<ImportedMapItemRow> ReadMapItemRows(
		IXLWorksheet worksheet,
		IReadOnlyDictionary<string, int> headers,
		ImportMapItemResult result)
	{
		var rows = new List<ImportedMapItemRow>();
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

		for (int rowNumber = 2; rowNumber <= lastRow; rowNumber++)
		{
			var row = worksheet.Row(rowNumber);
			if (row.CellsUsed().All(cell => cell.IsEmpty()))
			{
				continue;
			}

			var subdDistributorCode = GetString(row, headers["SubDistributorCode"]);
			var principal = GetString(row, headers["Principal"]);
			var companyItemCode = GetString(row, headers["CompanyItemCode"]);
			var companyItemName = GetString(row, headers["CompanyItemName"]);
			var subdItemCode = GetString(row, headers["SubdItemCode"]);
			var subdItemName = GetString(row, headers["SubdItemName"]);
			var uom = GetString(row, headers["UOM"]);
			string? conversionBasedOn = headers.TryGetValue("ConversionBasedOn", out var conversionBasedOnCol)
				? GetString(row, conversionBasedOnCol)
				: null;

			string? stockUom = headers.TryGetValue("StockUom", out var stockUomCol)
				? GetString(row, stockUomCol)
				: null;

			var blankRequiredColumns = new List<string>();

			if (string.IsNullOrWhiteSpace(subdItemCode)) blankRequiredColumns.Add("SubdItemCode");
			if (string.IsNullOrWhiteSpace(uom)) blankRequiredColumns.Add("UOM");

			// Skip completely empty rows
			if (string.IsNullOrWhiteSpace(subdItemCode) && string.IsNullOrWhiteSpace(uom) &&
				row.Cell(headers["Conversion"]).IsEmpty()
				&& (!headers.TryGetValue("Price", out var priceColForEmpty) || row.Cell(priceColForEmpty).IsEmpty()))
			{
				continue;
			}

			if (blankRequiredColumns.Count > 0)
			{
				continue;
			}

			int? conversion = null;
			if (!row.Cell(headers["Conversion"]).IsEmpty())
			{
				if (TryGetInt(row.Cell(headers["Conversion"]), out var parsedConversion) && parsedConversion > 0)
				{
					conversion = parsedConversion;
				}
			}
			// Price is no longer used (prices come from the company price); an old template's column is simply ignored.
			decimal? price = null;

			var rawValues = headers.ToDictionary(
				kvp => kvp.Key,
				kvp => (string?)GetString(row, kvp.Value),
				StringComparer.OrdinalIgnoreCase);

			rows.Add(new ImportedMapItemRow(
				rowNumber,
				subdDistributorCode,
				principal,
				companyItemCode,
				companyItemName,
				subdItemCode,
				subdItemName,
				uom,
				conversion,
				conversionBasedOn,
				price,
				string.IsNullOrWhiteSpace(stockUom) ? null : stockUom,
				rawValues));
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

			if (header is "subdistributorcode")
			{
				headers.TryAdd("SubDistributorCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "principal")
			{
				headers.TryAdd("Principal", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "companyitemcode" or "itemcode")
			{
				headers.TryAdd("CompanyItemCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "companyitemname" or "itemname")
			{
				headers.TryAdd("CompanyItemName", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "subditemcode" or "skucode")
			{
				headers.TryAdd("SubdItemCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "subditemname" or "itemname" or "description")
			{
				headers.TryAdd("SubdItemName", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "uom" or "unitofmeasure")
			{
				headers.TryAdd("UOM", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "conversion")
			{
				headers.TryAdd("Conversion", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "conversionbasedon" or "basedon" or "conversionbase")
			{
				headers.TryAdd("ConversionBasedOn", cell.Address.ColumnNumber);
				continue;
			}

			// Optional column. "Stock UOM", "stock_uom" and "StockUom" all land here.
			if (header.Replace(" ", "").Replace("_", "") is "stockuom" or "stockunit" or "stockuomname")
			{
				headers.TryAdd("StockUom", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "price")
			{
				headers.TryAdd("Price", cell.Address.ColumnNumber);
			}
		}

		return headers;
	}

	private static Dictionary<int, List<string>> ValidateGroupConsistency(
		List<ImportedMapItemRow> rows,
		Dictionary<int, List<string>> warnings)
	{
		var errors = new Dictionary<int, List<string>>();

		void AddError(int rowNumber, string message)
		{
			if (!errors.TryGetValue(rowNumber, out var list))
			{
				list = new List<string>();
				errors[rowNumber] = list;
			}

			if (!list.Contains(message, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(message);
			}
		}

		void AddWarning(int rowNumber, string message)
		{
			if (!warnings.TryGetValue(rowNumber, out var list))
			{
				list = new List<string>();
				warnings[rowNumber] = list;
			}

			if (!list.Contains(message, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(message);
			}
		}

		var firstRow = rows[0];

		foreach (var row in rows)
		{
			if (!string.Equals(Normalize(row.SubDistributorCode), Normalize(firstRow.SubDistributorCode), StringComparison.OrdinalIgnoreCase))
			{
				AddError(row.RowNumber, "SubDistributor code must match the first row in the item group.");
			}

			if (!string.Equals(Normalize(row.Principal), Normalize(firstRow.Principal), StringComparison.OrdinalIgnoreCase))
			{
				AddError(row.RowNumber, "Principal must match the first row in the item group.");
			}

			if (!string.Equals(Normalize(row.CompanyItemCode), Normalize(firstRow.CompanyItemCode), StringComparison.OrdinalIgnoreCase))
			{
				AddError(row.RowNumber, "Company Item code must match the first row in the item group.");
			}

			if (!string.Equals(Normalize(row.CompanyItemName), Normalize(firstRow.CompanyItemName), StringComparison.OrdinalIgnoreCase))
			{
				AddError(row.RowNumber, "Company Item name must match the first row in the item group.");
			}
		}

		var conflictingNameGroups = rows
			.Where(row => !string.IsNullOrWhiteSpace(row.UOM))
			.GroupBy(row => new
			{
				Subd = Normalize(row.SubdItemCode),
				ItemName = Normalize(row.SubdItemName),
				Uom = Normalize(row.UOM)
			})
			.Where(g => g.Count() > 1 && g.Select(r => r.Conversion).Distinct().Count() > 1);

		foreach (var group in conflictingNameGroups)
		{
			var rowNums = FormatRowNumbers(group.Select(r => r.RowNumber));
			foreach (var row in group)
			{
				AddError(row.RowNumber, $"UOM '{row.UOM}' is entered with conflicting conversion values for this item (rows {rowNums}). Each UOM name must map to a single conversion.");
			}
		}

		var duplicateUomConvGroups = rows
			.Where(row => !string.IsNullOrWhiteSpace(row.UOM))
			.GroupBy(row => new
			{
				Company = Normalize(row.CompanyItemCode),
				Subd = Normalize(row.SubdItemCode),
				ItemName = Normalize(row.SubdItemName),
				Uom = Normalize(row.UOM),
				Conv = row.Conversion
			})
			.Where(group => group.Count() > 1)
			.ToList();

		foreach (var group in duplicateUomConvGroups)
		{
			var groupRowsList = group.OrderBy(row => row.RowNumber).ToList();
			var first = groupRowsList[0];

			var isFullDuplicate = true;   // prices are no longer imported, so same UOM + conversion is a plain duplicate

			var rowNumbers = FormatRowNumbers(groupRowsList.Select(r => r.RowNumber));

			if (isFullDuplicate)
			{
				var message = $"Duplicate map item — rows {rowNumbers} are identical mappings for SubdItem '{first.SubdItemCode}' (UOM '{first.UOM}', conversion '{first.Conversion}'). Only one will be committed.";
				foreach (var row in groupRowsList)
				{
					AddWarning(row.RowNumber, message);
				}
			}
			else
			{
				foreach (var row in groupRowsList.Skip(1))
				{
					AddError(row.RowNumber, $"Duplicate UOM '{row.UOM}' with conversion '{row.Conversion}' but conflicting price for Company Item '{row.CompanyItemCode}', SubdItem '{row.SubdItemCode}', description '{row.SubdItemName}' (rows {rowNumbers}).");
				}
			}
		}

		return errors;
	}

	private static string FormatRowNumbers(IEnumerable<int> rowNumbers)
	{
		var nums = rowNumbers.Distinct().OrderBy(n => n).ToList();
		if (nums.Count == 0) return string.Empty;
		if (nums.Count == 1) return nums[0].ToString();
		return string.Join(", ", nums.Take(nums.Count - 1)) + " & " + nums[^1];
	}

	private static void MergeRowErrors(
		Dictionary<int, List<string>> rowErrors,
		IEnumerable<ImportedMapItemRow> rows,
		IReadOnlyDictionary<int, List<string>> additionalErrors)
	{
		foreach (var row in rows)
		{
			if (!additionalErrors.TryGetValue(row.RowNumber, out var incoming) || incoming.Count == 0)
			{
				continue;
			}

			if (!rowErrors.TryGetValue(row.RowNumber, out var current))
			{
				current = new List<string>();
				rowErrors[row.RowNumber] = current;
			}

			foreach (var message in incoming)
			{
				if (!current.Contains(message, StringComparer.OrdinalIgnoreCase))
				{
					current.Add(message);
				}
			}
		}
	}

	// The company price this sub-distributor's stock price is compared with.
	// Same rule as the Add UOM modal: KCPI items with Regional and National prices use the price list
	// assigned to the sub-distributor; everything else uses the single stock price.
	private static (decimal? Price, string? Label) ResolveApplicablePrice(CompanyItem ci, string? priceListType)
	{
		var structure = PricingStructureResolver.Resolve(ci.StockPrice, ci.StockPriceRegional, ci.StockPriceNational);

		return structure switch
		{
			PricingStructure.Dual when priceListType == PriceListTypes.Regional => (ci.StockPriceRegional, "Regional"),
			PricingStructure.Dual when priceListType == PriceListTypes.National => (ci.StockPriceNational, "National"),
			PricingStructure.Single => (ci.StockPrice, null),
			_ => (null, null)
		};
	}

	/// <summary>
	/// The item's stock UOM is always the company stock UOM, found by name (CS = CASE and so on). Its row's conversion
	/// (a piece count) is the number every other conversion is divided by. A company stock UOM that is a piece needs no row.
	/// A "Stock UOM" column in the file is only checked against it.
	/// </summary>
	private static (string? Name, StockUomSource Source, string? Reason, decimal? StockPieces) ResolveStockUom(
		List<ImportedMapItemRow> groupRows,
		string? companyStockUomName,
		Func<string?, bool> isStock,
		Dictionary<int, List<string>> rowErrors,
		Dictionary<int, List<string>> rowWarnings,
		out string? fileValue,
		out string? suggestedName,
		out string? suggestedReason)
	{
		static void Add(Dictionary<int, List<string>> target, int rowNumber, string message)
		{
			if (!target.TryGetValue(rowNumber, out var list))
			{
				list = new List<string>();
				target[rowNumber] = list;
			}

			if (!list.Contains(message, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(message);
			}
		}

		var explicitRows = groupRows.Where(r => !string.IsNullOrWhiteSpace(r.StockUom)).ToList();
		var explicitValues = explicitRows
			.Select(r => r.StockUom!.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		fileValue = explicitValues.Count == 0 ? null : string.Join(", ", explicitValues);
		suggestedName = null;
		suggestedReason = null;

		// Free company item: no stock UOM, conversions are not used for prices.
		if (string.IsNullOrWhiteSpace(companyStockUomName))
		{
			return (null, StockUomSource.None, null, null);
		}

		var companyStock = companyStockUomName.Trim();
		var stockCanon = StockUomSuggester.Canon(companyStock);

		if (explicitValues.Any(v => !isStock(v)))
		{
			Add(rowWarnings, explicitRows[0].RowNumber,
				$"The Stock UOM column ('{fileValue}') is ignored: conversions are always based on the company stock UOM '{companyStock}'.");
		}

		// Company stock UOM is a piece: the file's piece counts already are stock units, so no stock row is needed,
		// and a stock-named row of more than 1 piece is saved as a bundle (PC = 2 pieces → PC ×2).
		if (stockCanon == "PIECE")
		{
			foreach (var bundle in groupRows.Where(r => !string.IsNullOrWhiteSpace(r.UOM) && isStock(r.UOM) && r.Conversion is > 1))
			{
				Add(rowWarnings, bundle.RowNumber,
					$"'{bundle.UOM.Trim()}' is {bundle.Conversion} pieces, so it is saved as a bundle: 1 {bundle.UOM.Trim()} = {bundle.Conversion} {companyStock} " +
					$"(e.g. buy 1 take 1). The company {companyStock} stays 1.");
			}

			suggestedName = companyStock;
			suggestedReason = "the company stock UOM is a piece, so piece counts are used as they are";
			return (companyStock, StockUomSource.Explicit, suggestedReason, 1m);
		}

		var stockRows = groupRows
			.Where(r => !string.IsNullOrWhiteSpace(r.UOM) && isStock(r.UOM))
			.ToList();

		if (stockRows.Count > 1)
		{
			var names = string.Join(", ", stockRows.Select(r => $"'{r.UOM.Trim()}'"));
			foreach (var r in stockRows)
			{
				Add(rowErrors, r.RowNumber, $"More than one row is the company stock UOM '{companyStock}' ({names}). Keep one.");
			}
			return (null, StockUomSource.None, null, null);
		}

		if (stockRows.Count == 1)
		{
			var row = stockRows[0];
			suggestedName = row.UOM.Trim();
			suggestedReason = $"matches the company stock UOM ({companyStock}) by name or UOM synonym";

			if (row.Conversion is not > 0)
			{
				Add(rowErrors, row.RowNumber,
					$"Stock UOM '{row.UOM.Trim()}' needs its piece count in Conversion (for example 12 for a case of 12).");
				return (suggestedName, StockUomSource.Explicit, suggestedReason, null);
			}

			return (suggestedName, StockUomSource.Explicit, suggestedReason, row.Conversion.Value);
		}

		// No row for the stock UOM: fine when it is a piece (piece counts already are stock units).
		if (stockCanon == "PIECE")
		{
			suggestedName = companyStock;
			suggestedReason = "the company stock UOM is a piece, so it needs no row (always 1)";
			return (companyStock, StockUomSource.Explicit, suggestedReason, 1m);
		}

		Add(rowErrors, groupRows[0].RowNumber,
			$"The file's conversions are piece counts: add a row for the company stock UOM '{companyStock}' with its piece count so they can be turned into {companyStock} units. (Once mapped, the item doesn't need to keep that row.)");
		return (null, StockUomSource.None, null, null);
	}

	private static List<ImportedMapItemRow> ResolveConversionsForGroup(List<ImportedMapItemRow> groupRows)
	{
		var result = new List<ImportedMapItemRow>(groupRows.Count);

		foreach (var subdItemRows in groupRows.GroupBy(r => Normalize(r.SubdItemCode)))
		{
			var rowsList = subdItemRows.ToList();
			var byUom = new Dictionary<string, ImportedMapItemRow>(StringComparer.OrdinalIgnoreCase);
			foreach (var row in rowsList)
			{
				byUom[Normalize(row.UOM)] = row;
			}

			decimal? Resolve(string uomKey, HashSet<string> visiting)
			{
				if (!byUom.TryGetValue(uomKey, out var row))
				{
					return null;
				}

				if (!row.Conversion.HasValue)
				{
					return null;
				}

				if (string.IsNullOrWhiteSpace(row.ConversionBasedOn))
				{
					return row.Conversion;
				}

				var basisKey = Normalize(row.ConversionBasedOn);

				// Counted in itself: nothing to follow (flagged as an error at Check File when it isn't 1).
				if (basisKey == uomKey)
				{
					return row.Conversion;
				}

				if (!visiting.Add(uomKey) || !byUom.ContainsKey(basisKey))
				{
					return row.Conversion;
				}

				var basisResolved = Resolve(basisKey, visiting);
				return basisResolved.HasValue ? row.Conversion.Value * basisResolved.Value : row.Conversion;
			}

			foreach (var row in rowsList)
			{
				// A 1 with no basis (or counted in itself) is the base unit; a 1 counted in another unit follows that unit.
				if (!row.Conversion.HasValue
					|| (row.Conversion == 1 && (string.IsNullOrWhiteSpace(row.ConversionBasedOn)
												|| Normalize(row.ConversionBasedOn) == Normalize(row.UOM))))
				{
					result.Add(row);
					continue;
				}

				var resolved = Resolve(Normalize(row.UOM), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				var resolvedInt = resolved.HasValue
					? (int?)Math.Round(resolved.Value, MidpointRounding.AwayFromZero)
					: null;
				result.Add(row with { Conversion = resolvedInt ?? row.Conversion });
			}
		}

		return result;
	}

	private static IReadOnlyDictionary<int, List<string>> BuildSubdItemIdentityConflictsByRow(List<ImportedMapItemRow> rows)
	{
		return new Dictionary<int, List<string>>();
	}

	private static string GetString(IXLRow row, int columnNumber)
	{
		return row.Cell(columnNumber).GetString().Trim();
	}

	private static bool TryGetInt(IXLCell cell, out int value)
	{
		if (cell.DataType == XLDataType.Number)
		{
			value = (int)Math.Round(cell.GetDouble(), MidpointRounding.AwayFromZero);
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
			value = (decimal)cell.GetDouble();
			return true;
		}

		if (decimal.TryParse(cell.GetString().Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
		{
			return true;
		}

		return decimal.TryParse(cell.GetString().Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);
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
		return value?.Trim().ToLowerInvariant() ?? string.Empty;
	}

	private static void ApplyRawValues(ImportMapItemRowResult target, IReadOnlyDictionary<string, string?> source)
	{
		foreach (var kvp in source)
		{
			target.RawValues[kvp.Key] = kvp.Value;
		}
	}

	private sealed record ImportedMapItemRow(
		int RowNumber,
		string SubDistributorCode,
		string Principal,
		string CompanyItemCode,
		string CompanyItemName,
		string SubdItemCode,
		string SubdItemName,
		string UOM,
		int? Conversion,
		string? ConversionBasedOn,
		decimal? Price,
		string? StockUom,
		IReadOnlyDictionary<string, string?> RawValues);
}