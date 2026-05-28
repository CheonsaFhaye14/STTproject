using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Models;
using STTproject.Services;

namespace STTproject.Features.User.MapItem.Services;

public sealed class ImportMapItemService
{
	private const string BaseUomName = "PC";

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

		return await PrepareFromExcelAsync(excelStream, cancellationToken);
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
			"Conversion",
			"Price"
		};

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

		// Get subdistributor mapping
		var subdDistributors = await context.SubDistributors
			.AsNoTracking()
			.Where(s => s.IsActive)
			.ToDictionaryAsync(s => Normalize(s.SubdCode), cancellationToken);

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

		var existingBySubdAndCode = new Dictionary<(string, string), SubdItem>();
		foreach (var si in existingSubdItems)
		{
			var subdCode = subdDistributors.FirstOrDefault(k => k.Value.SubDistributorId == si.SubDistributorId).Key ?? string.Empty;
			var key = (Normalize(subdCode), Normalize(si.SubdItemCode));
			existingBySubdAndCode[key] = si;
		}

		// Group rows by the item identity shown in the UI.
		foreach (var rowGroup in parsedRows.GroupBy(row => new
		{
			SubDistributorCode = Normalize(row.SubDistributorCode),
			Principal = Normalize(row.Principal),
			CompanyItemCode = Normalize(row.CompanyItemCode),
			CompanyItemName = Normalize(row.CompanyItemName)
		}))
		{
			var groupRows = rowGroup.OrderBy(row => row.RowNumber).ToList();
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
			var rowErrors = ValidateGroupConsistency(groupRows);
			MergeRowErrors(rowErrors, groupRows, crossGroupIdentityErrors);

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
					rowResult.Issues.Add($"Company Item '{firstRow.CompanyItemCode}' not found.");
					result.Rows.Add(rowResult);
					createdRows.Add(rowResult);
				}

				var prepared = new PreparedItemGroup(createdRows) { Selected = false };
				prepared.Issues.Add(new ImportMapItemIssue(firstRow.RowNumber, firstRow.SubdItemCode, $"Company Item '{firstRow.CompanyItemCode}' not found."));
				result.PreparedGroups.Add(prepared);

				continue;
			}

			// Check if any rows conflict with an existing SubdItem code in the database
			var subdDistributorKey = (Normalize(firstRow.SubDistributorCode), Normalize(firstRow.SubdItemCode));
			var alreadyExistsByCode = existingBySubdAndCode.TryGetValue(subdDistributorKey, out var existingCode) && existingCode != null;

			if (alreadyExistsByCode)
			{
				foreach (var row in groupRows)
				{
					if (!rowErrors.TryGetValue(row.RowNumber, out var issues))
					{
						issues = new List<string>();
						rowErrors[row.RowNumber] = issues;
					}
					issues.Add($"SubdItem code '{firstRow.SubdItemCode}' is already mapped in the database for SubDistributor '{firstRow.SubDistributorCode}'.");
				}
			}

			var computedPricesByRow = ComputeMissingPrices(groupRows, rowErrors.Values.SelectMany(x => x).ToList());
			var hasAnyErrors = rowErrors.Any(kvp => kvp.Value.Count > 0);
			if (hasAnyErrors)
			{
				var createdRows = new List<ImportMapItemRowResult>();
				foreach (var row in groupRows)
				{
					var computedPrice = computedPricesByRow.TryGetValue(row.RowNumber, out var value)
						? value
						: (decimal?)null;
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
						Price = row.Price ?? computedPrice,
						IsSuccess = rowIssues.Count == 0
					};
					rowResult.Issues.AddRange(rowIssues);
					result.Rows.Add(rowResult);
					createdRows.Add(rowResult);
				}

				var prepared = new PreparedItemGroup(createdRows) { Selected = false };
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
				var computedPrice = computedPricesByRow.TryGetValue(row.RowNumber, out var value)
					? value
					: (decimal?)null;
				var effectivePrice = row.Price ?? computedPrice;

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
					IsSuccess = true
				};

				// Validate conversion
				if (row.Conversion <= 0)
				{
					rowResult.IsSuccess = false;
					rowResult.Issues.Add("Conversion must be a whole number greater than 0.");
				}

				// Validate price
				if (effectivePrice is null || effectivePrice <= 0)
				{
					rowResult.IsSuccess = false;
					rowResult.Issues.Add("Price must be provided or computable from another UOM row, and greater than 0.");
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
				Selected = uomResults.All(r => r.Issues.Count == 0),
				IsSaved = false
			};
			foreach (var rr in uomResults.SelectMany(r => r.Issues.Select(i => new ImportMapItemIssue(r.RowNumber, r.SubdItemCode, i))))
			{
				preparedSuccess.Issues.Add(rr);
			}
			result.PreparedGroups.Add(preparedSuccess);
		}

		return result;
	}

	private async Task<int> CommitMapItemRowsAsync(
		List<ImportMapItemRowResult> rows,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		if (rows.Count == 0)
			return 0;

		await using var context = _contextFactory.CreateDbContext();

		// Get all subdistributors for mapping
		var subdDistributors = await context.SubDistributors
			.AsNoTracking()
			.Where(s => s.IsActive && s.EncoderId == currentUserId)
			.ToDictionaryAsync(s => Normalize(s.SubdCode), cancellationToken);

		var companyItems = await context.CompanyItems
			.AsNoTracking()
			.Where(ci => ci.IsActive)
			.ToDictionaryAsync(ci => Normalize(ci.ItemCode), cancellationToken);

		// Group by SubDistributor + SubdItemCode to create SubdItems with their UOMs
		var groupedBySubdItem = rows
			.GroupBy(r => new
			{
				SubDistributorCode = Normalize(r.SubDistributorCode),
				SubdItemCode = Normalize(r.SubdItemCode)
			})
			.ToList();

		var committedGroups = 0;

		foreach (var subdItemGroup in groupedBySubdItem)
		{
			var subdItemCode = subdItemGroup.First().SubdItemCode.Trim();
			var subdItemRows = subdItemGroup.ToList();
			var firstRow = subdItemRows[0];

			// Find the subdistributor
			if (!subdDistributors.TryGetValue(Normalize(firstRow.SubDistributorCode), out var subdDistributor))
			{
				throw new InvalidOperationException($"SubDistributor '{firstRow.SubDistributorCode}' was not found during commit.");
			}

			// Find the company item
			if (!companyItems.TryGetValue(Normalize(firstRow.CompanyItemCode), out var companyItem) || companyItem is null)
			{
				throw new InvalidOperationException($"Company item '{firstRow.CompanyItemCode}' was not found during commit.");
			}

			var existingByCode = await context.SubdItems
				.FirstOrDefaultAsync(
					si => si.SubDistributorId == subdDistributor.SubDistributorId && si.SubdItemCode == subdItemCode,
					cancellationToken);

			if (existingByCode != null)
			{
				throw new InvalidOperationException($"Cannot commit: SubdItem code '{subdItemCode}' already exists for SubDistributor '{firstRow.SubDistributorCode}'. Please review the import and try again.");
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

			context.SubdItems.Add(subdItem);
			await context.SaveChangesAsync(cancellationToken);

			var uomEntries = new Dictionary<string, UomEntry>();
			foreach (var row in subdItemRows)
			{
				if (!string.IsNullOrWhiteSpace(row.UomName) && row.Conversion > 0 && row.Price > 0)
				{
					var canonicalUom = CanonicalizeUomName(row.UomName);
					uomEntries[canonicalUom] = new UomEntry
					{
						Conversion = (int)row.Conversion,
						Price = row.Price
					};
				}
			}

			EnsureBaseUnitUom(uomEntries);

			var uomSaved = await _mapItemService.SaveSubdItemUomPricesAsync(subdItem.SubdItemId, uomEntries, currentUserId, cancellationToken);
			if (!uomSaved)
			{
				throw new InvalidOperationException($"Failed to save UOM rows for SubdItem '{subdItem.SubdItemCode}'.");
			}

			committedGroups++;
		}

		return committedGroups;
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

			var blankRequiredColumns = new List<string>();

			if (string.IsNullOrWhiteSpace(subdItemCode)) blankRequiredColumns.Add("SubdItemCode");
			if (string.IsNullOrWhiteSpace(uom)) blankRequiredColumns.Add("UOM");
			if (row.Cell(headers["Conversion"]).IsEmpty()) blankRequiredColumns.Add("Conversion");

			// Skip completely empty rows
			if (string.IsNullOrWhiteSpace(subdItemCode) && string.IsNullOrWhiteSpace(uom) &&
				row.Cell(headers["Conversion"]).IsEmpty() && row.Cell(headers["Price"]).IsEmpty())
			{
				continue;
			}

			if (blankRequiredColumns.Count > 0)
			{
				continue; // Skip rows with missing required fields
			}

			if (!TryGetDecimal(row.Cell(headers["Conversion"]), out var conversion) || conversion <= 0)
			{
				continue; // Skip rows with invalid conversion
			}

			decimal? price = null;
			if (!row.Cell(headers["Price"]).IsEmpty())
			{
				if (!TryGetDecimal(row.Cell(headers["Price"]), out var parsedPrice) || parsedPrice <= 0)
				{
					continue; // Skip rows with invalid price
				}

				price = parsedPrice;
			}

			if (price.HasValue && price.Value <= 0)
			{
				continue; // Skip rows with invalid price
			}

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
				price));
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

			if (header is "price")
			{
				headers.TryAdd("Price", cell.Address.ColumnNumber);
			}
		}

		return headers;
	}

	private static Dictionary<int, List<string>> ValidateGroupConsistency(List<ImportedMapItemRow> rows)
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

			if (IsPieceUom(row.UOM) && row.Conversion != 1)
			{
				AddError(row.RowNumber, "UOM 'PC' must have conversion 1.");
			}

			if (!IsPieceUom(row.UOM) && row.Conversion == 1)
			{
				AddError(row.RowNumber, "Only UOM 'PC' can have conversion 1.");
			}
		}

		var duplicateUomGroups = rows
			.Where(row => !string.IsNullOrWhiteSpace(row.UOM))
			.GroupBy(row => NormalizeUomKey(row.UOM))
			.Where(group => group.Count() > 1)
			.ToList();

		foreach (var group in duplicateUomGroups)
		{
			var duplicateRows = group.OrderBy(row => row.RowNumber).Skip(1).ToList();
			foreach (var row in duplicateRows)
			{
				AddError(row.RowNumber, $"Duplicate UOM '{row.UOM}'.");
			}
		}

		var duplicateConversionGroups = rows
			.GroupBy(row => row.Conversion)
			.Where(group => group.Count() > 1)
			.ToList();

		foreach (var group in duplicateConversionGroups)
		{
			var duplicateRows = group.OrderBy(row => row.RowNumber).Skip(1).ToList();
			foreach (var row in duplicateRows)
			{
				AddError(row.RowNumber, $"Duplicate conversion '{row.Conversion}'.");
			}
		}

		return errors;
	}

	private static bool IsPieceUom(string? uom)
	{
		var normalized = Normalize(uom ?? string.Empty);
		return normalized is "piece" or "pcs" or "pc";
	}

	private static string NormalizeUomKey(string? uom)
	{
		return IsPieceUom(uom) ? BaseUomName : Normalize(uom ?? string.Empty);
	}

	private static string CanonicalizeUomName(string? uom)
	{
		return IsPieceUom(uom) ? BaseUomName : (uom ?? string.Empty).Trim();
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

	private static Dictionary<int, decimal> ComputeMissingPrices(List<ImportedMapItemRow> rows, List<string> errors)
	{
		var computedPricesByRow = new Dictionary<int, decimal>();
		var pricedRows = rows
			.Where(row => row.Price.HasValue && row.Price.Value > 0)
			.OrderBy(row => row.RowNumber)
			.ToList();

		var missingPriceRows = rows
			.Where(row => !row.Price.HasValue)
			.ToList();

		if (missingPriceRows.Count == 0)
		{
			return computedPricesByRow;
		}

		if (pricedRows.Count == 0)
		{
			errors.Add("At least one row in each item group must have a price to compute missing prices for other UOM rows.");
			return computedPricesByRow;
		}

		var sourceRow = pricedRows[0];
		var unitPrice = sourceRow.Price!.Value / sourceRow.Conversion;

		foreach (var row in missingPriceRows)
		{
			var computedPrice = Math.Round(unitPrice * row.Conversion, 2, MidpointRounding.AwayFromZero);
			if (computedPrice <= 0)
			{
				errors.Add($"Unable to compute a valid price for row {row.RowNumber} ({row.SubdItemCode}/{row.UOM}).");
				continue;
			}

			computedPricesByRow[row.RowNumber] = computedPrice;
		}

		return computedPricesByRow;
	}

	private static void EnsureBaseUnitUom(Dictionary<string, UomEntry> uomEntries)
	{
		if (uomEntries.Count == 0)
		{
			return;
		}

		var existingBaseKey = uomEntries.Keys.FirstOrDefault(IsPieceUom);
		if (!string.IsNullOrWhiteSpace(existingBaseKey) && uomEntries.TryGetValue(existingBaseKey, out var baseEntry))
		{
			baseEntry.Conversion = 1;
			if (!baseEntry.Price.HasValue || baseEntry.Price <= 0)
			{
				var pricedSource = uomEntries
					.Where(entry => !IsPieceUom(entry.Key))
					.Where(entry => entry.Value.Price.HasValue && entry.Value.Price > 0 && entry.Value.Conversion > 0)
					.OrderBy(entry => entry.Value.Conversion)
					.FirstOrDefault();

				if (pricedSource.Value != null && pricedSource.Value.Price.HasValue && pricedSource.Value.Conversion > 0)
				{
					baseEntry.Price = Math.Round(pricedSource.Value.Price.Value / pricedSource.Value.Conversion, 2, MidpointRounding.AwayFromZero);
					baseEntry.IsAutoCalculated = true;
				}
			}

			if (!string.Equals(existingBaseKey, BaseUomName, StringComparison.OrdinalIgnoreCase))
			{
				uomEntries.Remove(existingBaseKey);
			}

			uomEntries[BaseUomName] = baseEntry;
			return;
		}

		var sourceEntry = uomEntries
			.Where(entry => entry.Value.Price.HasValue && entry.Value.Price > 0 && entry.Value.Conversion > 0)
			.OrderBy(entry => entry.Key.Equals(BaseUomName, StringComparison.OrdinalIgnoreCase))
			.ThenBy(entry => entry.Value.Conversion)
			.FirstOrDefault();

		if (sourceEntry.Value == null || !sourceEntry.Value.Price.HasValue || sourceEntry.Value.Conversion <= 0)
		{
			return;
		}

		uomEntries[BaseUomName] = new UomEntry
		{
			Conversion = 1,
			Price = Math.Round(sourceEntry.Value.Price.Value / sourceEntry.Value.Conversion, 2, MidpointRounding.AwayFromZero),
			IsAutoCalculated = true
		};
	}

	private static IReadOnlyDictionary<int, List<string>> BuildSubdItemIdentityConflictsByRow(List<ImportedMapItemRow> rows)
	{
		var errorsByRow = new Dictionary<int, List<string>>();

		void AddError(int rowNumber, string message)
		{
			if (!errorsByRow.TryGetValue(rowNumber, out var list))
			{
				list = new List<string>();
				errorsByRow[rowNumber] = list;
			}

			if (!list.Contains(message, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(message);
			}
		}

		var itemGroups = rows
			.GroupBy(row => new
			{
				SubDistributorCode = Normalize(row.SubDistributorCode),
				CompanyItemCode = Normalize(row.CompanyItemCode),
				Principal = Normalize(row.Principal)
			})
			.Select(group => group.OrderBy(row => row.RowNumber).ToList())
			.ToList();

		// Allow the same CompanyItem to be mapped multiple times for the same SubDistributor
		// as long as the SubdItemCode differs. Older validations enforced that SubdItemCode
		// and SubdItemName must remain the same for a CompanyItem within the file; remove
		// those restrictions to permit multiple mappings with different SubdItem codes.

		var subdItemCodeGroups = rows
			.Where(row => !string.IsNullOrWhiteSpace(row.SubdItemCode))
			.GroupBy(row => new
			{
				SubDistributorCode = Normalize(row.SubDistributorCode),
				SubdItemCode = Normalize(row.SubdItemCode)
			})
			.Select(group => group.OrderBy(row => row.RowNumber).ToList())
			.Where(group => group.Count > 1)
			.ToList();

		foreach (var codeGroup in subdItemCodeGroups)
		{
			var firstRow = codeGroup[0];
			var rowNumbers = string.Join(", ", codeGroup.Select(row => row.RowNumber));
			var canonicalItemKey = string.Join("|",
				Normalize(firstRow.Principal),
				Normalize(firstRow.CompanyItemCode),
				Normalize(firstRow.CompanyItemName),
				Normalize(firstRow.SubdItemName));

			foreach (var row in codeGroup.Skip(1))
			{
				var rowItemKey = string.Join("|",
					Normalize(row.Principal),
					Normalize(row.CompanyItemCode),
					Normalize(row.CompanyItemName),
					Normalize(row.SubdItemName));

				if (!string.Equals(rowItemKey, canonicalItemKey, StringComparison.OrdinalIgnoreCase))
				{
					AddError(
						row.RowNumber,
						$"SubdItem code '{row.SubdItemCode}' Item is duplicated within the uploaded Excel for SubDistributor '{row.SubDistributorCode}'.");
				}
			}
		}

		return errorsByRow;
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

	private sealed record ImportedMapItemRow(
		int RowNumber,
		string SubDistributorCode,
		string Principal,
		string CompanyItemCode,
		string CompanyItemName,
		string SubdItemCode,
		string SubdItemName,
		string UOM,
		decimal Conversion,
		decimal? Price);
}

// ImportMapItemIssue moved to DTOs/ImportMapItemResult.cs
