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
			"Conversion",
			"Price"
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

		// Identity now includes ItemName (description) so two rows that share
		// SubDistributor/Code/CompanyItem but differ in description are treated
		// as distinct items rather than a duplicate.
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
			CompanyItemName = Normalize(row.CompanyItemName)
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

			// Check if any rows conflict with an existing exact SubdItem mapping in the database.
			// Includes description (ItemName) — a matching code/company with a different
			// description is treated as a new/distinct item, not a conflict.
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
					ApplyRawValues(rowResult, row.RawValues);
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
				ApplyRawValues(rowResult, row.RawValues);

				// NEW — carry over non-blocking duplicate-map warnings
				if (groupWarnings.TryGetValue(row.RowNumber, out var rowWarnings))
				{
					rowResult.Warnings.AddRange(rowWarnings);
				}

				// Conversion is optional only when the row carries its own price.
				if (row.Conversion.HasValue && row.Conversion.Value <= 0)
				{
					rowResult.IsSuccess = false;
					rowResult.Issues.Add("Conversion must be a whole number greater than 0 when provided.");
				}
				else if (!row.Conversion.HasValue && (!row.Price.HasValue || row.Price.Value <= 0))
				{
					rowResult.IsSuccess = false;
					rowResult.Issues.Add("Conversion is required unless a price is entered directly for this row.");
				}
				else if (!row.Conversion.HasValue && row.Price.HasValue && row.Price.Value > 0 && !IsPieceUom(row.UOM))
				{
					// Valid, but worth flagging — could be an intentional price-only row,
					// or a forgotten conversion. Non-blocking.
					rowResult.Warnings.Add($"Row {row.RowNumber} ({row.UOM}) has no conversion — only a price was provided. Double-check this was intentional.");
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
					si => si.SubDistributorId == subdDistributor.SubDistributorId
						&& si.SubdItemCode == subdItemCode
						&& si.CompanyItemId == companyItem.CompanyItemId
						&& si.ItemName == firstRow.SubdItemName,
					cancellationToken);

			if (existingByCode != null)
			{
				throw new InvalidOperationException($"Cannot commit: SubdItem code '{subdItemCode}' with description '{firstRow.SubdItemName}' already exists for SubDistributor '{firstRow.SubDistributorCode}' with Company Item '{firstRow.CompanyItemCode}'. Please review the import and try again.");
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
				if (string.IsNullOrWhiteSpace(row.UomName) || row.Price is not > 0)
				{
					continue;
				}

				var canonicalUom = CanonicalizeUomName(row.UomName);
				uomEntries[canonicalUom] = new UomEntry
				{
					Conversion = row.Conversion, // decimal? — null is valid now, price is what's required
					Price = row.Price
				};
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

	private static async Task<bool> IsAdminAsync(
		SttprojectContext context,
		int currentUserId,
		CancellationToken cancellationToken)
	{
		// TODO: adjust to your actual Users/role schema (e.g. RoleId FK, enum,
		// or a claims-based role that's passed in from the controller instead
		// of being looked up here).
		return await context.Users
			.AsNoTracking()
			.Where(u => u.UserId == currentUserId)
			.Select(u => u.Role == "Admin")
			.FirstOrDefaultAsync(cancellationToken);
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

			var blankRequiredColumns = new List<string>();

			if (string.IsNullOrWhiteSpace(subdItemCode)) blankRequiredColumns.Add("SubdItemCode");
			if (string.IsNullOrWhiteSpace(uom)) blankRequiredColumns.Add("UOM");

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

			decimal? conversion = null;
			if (!row.Cell(headers["Conversion"]).IsEmpty())
			{
				if (!TryGetDecimal(row.Cell(headers["Conversion"]), out var parsedConversion) || parsedConversion <= 0)
				{
					continue; // Skip rows with an invalid (non-blank) conversion value
				}

				conversion = parsedConversion;
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

			if (IsPieceUom(row.UOM) && row.Conversion.HasValue && row.Conversion.Value != 1)
			{
				AddError(row.RowNumber, "UOM 'PC' must have conversion 1.");
			}

			if (!IsPieceUom(row.UOM) && row.Conversion.HasValue && row.Conversion.Value == 1)
			{
				AddError(row.RowNumber, "Only UOM 'PC' can have conversion 1.");
			}
		}

		// Duplicate UOM+conversion within the same SubdItemCode+description:
		// if every other field also matches (price included), the rows describe
		// the exact same mapping entered twice — that's a "duplicate map item"
		// warning, not a blocking error, and only one copy gets committed.
		// If fields differ (e.g. conflicting price), it stays a blocking error
		// since we can't tell which value is correct.
		var duplicateUomConvGroups = rows
			.Where(row => !string.IsNullOrWhiteSpace(row.UOM))
			.GroupBy(row => new
			{
				Company = Normalize(row.CompanyItemCode),
				Subd = Normalize(row.SubdItemCode),
				ItemName = Normalize(row.SubdItemName),
				Uom = NormalizeUomKey(row.UOM),
				Conv = row.Conversion
			})
			.Where(group => group.Count() > 1)
			.ToList();

		foreach (var group in duplicateUomConvGroups)
		{
			var groupRowsList = group.OrderBy(row => row.RowNumber).ToList();
			var first = groupRowsList[0];

			var isFullDuplicate = groupRowsList.Skip(1).All(row => row.Price == first.Price);

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

		// Only a row with BOTH a price and a conversion can serve as the basis
		// for deriving other rows' prices.
		var pricedRows = rows
			.Where(row => row.Price.HasValue && row.Price.Value > 0 && row.Conversion.HasValue && row.Conversion.Value > 0)
			.OrderBy(row => row.RowNumber)
			.ToList();

		var missingPriceRows = rows
			.Where(row => !row.Price.HasValue)
			.ToList();

		if (missingPriceRows.Count == 0)
		{
			return computedPricesByRow;
		}

		// A row with neither a price nor a conversion can't be resolved at all.
		var unresolvable = missingPriceRows.Where(row => !row.Conversion.HasValue).ToList();
		foreach (var row in unresolvable)
		{
			errors.Add($"Row {row.RowNumber} ({row.SubdItemCode}/{row.UOM}) needs either a price or a conversion.");
		}

		var resolvableMissingPriceRows = missingPriceRows.Except(unresolvable).ToList();
		if (resolvableMissingPriceRows.Count == 0)
		{
			return computedPricesByRow;
		}

		if (pricedRows.Count == 0)
		{
			errors.Add("At least one row in each item group must have both a price and a conversion to compute missing prices for other UOM rows.");
			return computedPricesByRow;
		}

		var sourceRow = pricedRows[0];
		var unitPrice = sourceRow.Price!.Value / sourceRow.Conversion!.Value;

		foreach (var row in resolvableMissingPriceRows)
		{
			var computedPrice = Math.Round(unitPrice * row.Conversion!.Value, 2, MidpointRounding.AwayFromZero);
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
					// Conversion filtered to > 0 above, so it's guaranteed non-null here — unwrap
					// with .Value since Math.Round requires a non-nullable decimal argument.
					baseEntry.Price = Math.Round(pricedSource.Value.Price.Value / pricedSource.Value.Conversion!.Value, 2, MidpointRounding.AwayFromZero);
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
			// Same unwrap here — Conversion > 0 was already checked above.
			Price = Math.Round(sourceEntry.Value.Price.Value / sourceEntry.Value.Conversion!.Value, 2, MidpointRounding.AwayFromZero),
			IsAutoCalculated = true
		};
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
				byUom[NormalizeUomKey(row.UOM)] = row;
			}

			decimal? Resolve(string uomKey, HashSet<string> visiting)
			{
				if (IsPieceUom(uomKey))
				{
					return 1m;
				}

				if (!byUom.TryGetValue(uomKey, out var row) || !row.Conversion.HasValue)
				{
					return null;
				}

				var basisKey = string.IsNullOrWhiteSpace(row.ConversionBasedOn)
					? Normalize(BaseUomName)
					: NormalizeUomKey(row.ConversionBasedOn);

				if (IsPieceUom(basisKey) || !visiting.Add(uomKey) || !byUom.ContainsKey(basisKey))
				{
					return row.Conversion;
				}

				var basisResolved = Resolve(basisKey, visiting);
				return basisResolved.HasValue ? row.Conversion.Value * basisResolved.Value : row.Conversion;
			}

			foreach (var row in rowsList)
			{
				if (IsPieceUom(row.UOM))
				{
					result.Add(row with { Conversion = 1m });
					continue;
				}

				if (!row.Conversion.HasValue)
				{
					// No conversion supplied for this row — leave it null; it must
					// carry its own price instead (enforced later).
					result.Add(row);
					continue;
				}

				var resolved = Resolve(NormalizeUomKey(row.UOM), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				result.Add(row with { Conversion = resolved ?? row.Conversion });
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
    decimal? Conversion,
	string? ConversionBasedOn,
    decimal? Price,
    IReadOnlyDictionary<string, string?> RawValues);
	
}