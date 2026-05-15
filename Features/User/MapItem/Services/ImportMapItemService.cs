using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using STTproject.Data;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Services;

namespace STTproject.Features.User.MapItem.Services;

public sealed class ImportMapItemService
{
	private readonly IDbContextFactory<SttprojectContext> _contextFactory;
	private readonly ILogger<ImportMapItemService> _logger;

	public ImportMapItemService(
		IDbContextFactory<SttprojectContext> contextFactory,
		ILogger<ImportMapItemService> logger)
	{
		_contextFactory = contextFactory;
		_logger = logger;
	}

	public async Task<ImportMapItemResult> ImportFromExcelAsync(
		Stream excelStream,
		int subDistributorId,
		int currentUserId,
		CancellationToken cancellationToken = default)
	{
		var result = new ImportMapItemResult();

		if (excelStream is null || !excelStream.CanRead)
		{
			result.Rows.Add(CreateErrorRow(0, "Import file is missing or unreadable."));
			return result;
		}

		if (subDistributorId <= 0)
		{
			result.Rows.Add(CreateErrorRow(0, "A valid sub-distributor must be selected before importing."));
			return result;
		}

		if (currentUserId <= 0)
		{
			result.Rows.Add(CreateErrorRow(0, "Unable to identify the current user. Please sign in again."));
			return result;
		}

		using var workbook = new XLWorkbook(excelStream);
		var worksheet = workbook.Worksheets.FirstOrDefault(IsTemplateWorksheet) ?? workbook.Worksheets.First();
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

		var missingHeaders = requiredHeaders.Where(header => !headers.ContainsKey(header)).ToList();
		if (missingHeaders.Count > 0)
		{
			result.Rows.Add(CreateErrorRow(0, $"Missing required column(s): {string.Join(", ", missingHeaders)}."));
			return result;
		}

		var parsedRows = ReadRows(worksheet, headers);
		if (parsedRows.Count == 0)
		{
			result.Rows.Add(CreateErrorRow(0, "No import rows were found in the template."));
			return result;
		}

		var templateSubdCodes = parsedRows
			.Select(row => Normalize(row.SubDistributorCode))
			.Where(code => !string.IsNullOrWhiteSpace(code))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (templateSubdCodes.Count == 0)
		{
			result.Rows.Add(CreateErrorRow(0, "SubDistributorCode is required in the template."));
			return result;
		}

		if (templateSubdCodes.Count > 1)
		{
			result.Rows.Add(CreateErrorRow(0, "All rows in the template must use the same SubDistributorCode."));
			return result;
		}

		await using var context = _contextFactory.CreateDbContext();
		var templateSubdCode = templateSubdCodes[0];
		var subDistributor = await context.SubDistributors
			.AsNoTracking()
			.FirstOrDefaultAsync(subd => subd.IsActive && (
				Normalize(subd.SubdCode) == templateSubdCode ||
				Normalize(subd.CompanySubdCode) == templateSubdCode), cancellationToken);

		if (subDistributor is null)
		{
			result.Rows.Add(CreateErrorRow(0, $"Sub-distributor code '{templateSubdCode}' was not found."));
			return result;
		}

		subDistributorId = subDistributor.SubDistributorId;

		var companyItems = await context.CompanyItems
			.AsNoTracking()
			.Where(item => item.IsActive)
			.ToListAsync(cancellationToken);

		var companyByCode = companyItems.ToDictionary(item => Normalize(item.ItemCode));

		var subdItems = await context.SubdItems
			.AsNoTracking()
			.Where(item => item.SubDistributorId == subDistributorId && item.IsActive)
			.ToListAsync(cancellationToken);

		var subdItemByCode = subdItems.ToDictionary(item => Normalize(item.SubdItemCode));

		foreach (var row in parsedRows)
		{
			var rowResult = new ImportMapItemRowResult
			{
				RowNumber = row.RowNumber,
				SubDistributorCode = row.SubDistributorCode,
				Principal = row.Principal,
				CompanyItemCode = row.CompanyItemCode,
				CompanyItemName = row.CompanyItemName,
				SubdItemCode = row.SubdItemCode,
				SubdItemName = row.SubdItemName,
				UomName = row.UomName,
				Conversion = row.Conversion,
				Price = row.Price
			};

			try
			{
				ValidateRow(row, subDistributor, companyByCode, rowResult);

				if (rowResult.Issues.Count > 0)
				{
					rowResult.Message = string.Join(" ", rowResult.Issues);
					result.Rows.Add(rowResult);
					continue;
				}

				var companyItem = companyByCode[Normalize(row.CompanyItemCode)];
				await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

				if (!subdItemByCode.TryGetValue(Normalize(row.SubdItemCode), out var subdItem))
				{
					subdItem = new SubdItem
					{
						SubdItemCode = row.SubdItemCode.Trim(),
						ItemName = row.SubdItemName.Trim(),
						SubDistributorId = subDistributorId,
						IsActive = true,
						CreatedDate = DateTime.UtcNow,
						CreatedBy = currentUserId,
						CompanyItemId = companyItem.CompanyItemId
					};

					context.SubdItems.Add(subdItem);
					await context.SaveChangesAsync(cancellationToken);
					subdItemByCode[Normalize(subdItem.SubdItemCode)] = subdItem;
				}
				else
				{
					subdItem.CompanyItemId = companyItem.CompanyItemId;
					subdItem.ItemName = row.SubdItemName.Trim();
					subdItem.UpdatedBy = currentUserId;
					subdItem.UpdatedDate = DateTime.UtcNow;
				}

				var existingUom = await context.ItemsUoms.FirstOrDefaultAsync(uom =>
					uom.SubdItemId == subdItem.SubdItemId &&
					Normalize(uom.UomName) == Normalize(row.UomName), cancellationToken);

				if (existingUom is null)
				{
					existingUom = new ItemsUom
					{
						SubdItemId = subdItem.SubdItemId,
						UomName = row.UomName.Trim(),
						ConversionToBase = row.Conversion ?? 0m,
						IsBaseUnit = row.Conversion == 1,
						Price = row.Price ?? 0m,
						CreatedDate = DateTime.UtcNow,
						CreatedBy = currentUserId
					};

					context.ItemsUoms.Add(existingUom);
				}
				else
				{
					existingUom.ConversionToBase = row.Conversion ?? existingUom.ConversionToBase;
					existingUom.IsBaseUnit = row.Conversion == 1;
					existingUom.Price = row.Price ?? existingUom.Price;
					existingUom.UpdatedBy = currentUserId;
					existingUom.UpdatedDate = DateTime.UtcNow;
				}

				await context.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);

				rowResult.IsSuccess = true;
				rowResult.SubdItemId = subdItem.SubdItemId;
				rowResult.ItemsUomId = existingUom.ItemsUomId;
				rowResult.Message = $"Mapped {row.SubdItemCode} successfully.";
				result.Rows.Add(rowResult);
			}
			catch (Exception ex)
			{
				var baseMsg = ex.GetBaseException()?.Message ?? ex.Message;
				_logger.LogError(ex, "Failed to import map item row {RowNumber}: {Message}", row.RowNumber, baseMsg);
				rowResult.Issues.Add($"Unexpected error while importing row {row.RowNumber}: {baseMsg}");
				rowResult.Message = string.Join(" ", rowResult.Issues);
				result.Rows.Add(rowResult);
			}
		}

		return result;
	}

	private static void ValidateRow(
		ImportedMapItemRow row,
		SubDistributor subDistributor,
		IReadOnlyDictionary<string, CompanyItem> companyByCode,
		ImportMapItemRowResult rowResult)
	{
		if (!string.Equals(Normalize(row.SubDistributorCode), Normalize(subDistributor.SubdCode), StringComparison.OrdinalIgnoreCase))
		{
			rowResult.Issues.Add($"SubDistributorCode '{row.SubDistributorCode}' does not match the selected sub-distributor.");
		}

		if (string.IsNullOrWhiteSpace(row.Principal))
		{
			rowResult.Issues.Add("Principal is required.");
		}

		if (string.IsNullOrWhiteSpace(row.CompanyItemCode))
		{
			rowResult.Issues.Add("Company item code is required.");
		}
		else if (!companyByCode.TryGetValue(Normalize(row.CompanyItemCode), out var companyItem))
		{
			rowResult.Issues.Add($"Company item code '{row.CompanyItemCode}' was not found.");
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(row.CompanyItemName) && !string.Equals(Normalize(row.CompanyItemName), Normalize(companyItem.ItemName), StringComparison.OrdinalIgnoreCase))
			{
				rowResult.Issues.Add($"Company item name '{row.CompanyItemName}' does not match '{companyItem.ItemName}'.");
			}

			if (!string.Equals(Normalize(row.Principal), Normalize(companyItem.Principal), StringComparison.OrdinalIgnoreCase))
			{
				rowResult.Issues.Add($"Principal '{row.Principal}' does not match the company item principal '{companyItem.Principal}'.");
			}
		}

		if (string.IsNullOrWhiteSpace(row.SubdItemCode))
		{
			rowResult.Issues.Add("Subd item code is required.");
		}

		if (string.IsNullOrWhiteSpace(row.SubdItemName))
		{
			rowResult.Issues.Add("Subd item name is required.");
		}

		if (string.IsNullOrWhiteSpace(row.UomName))
		{
			rowResult.Issues.Add("UOM is required.");
		}

		if (!row.Conversion.HasValue || row.Conversion <= 0)
		{
			rowResult.Issues.Add("Conversion must be greater than 0.");
		}

		if (!row.Price.HasValue || row.Price <= 0)
		{
			rowResult.Issues.Add("Price must be greater than 0.");
		}
	}

	private static List<ImportedMapItemRow> ReadRows(IXLWorksheet worksheet, IReadOnlyDictionary<string, int> headers)
	{
		var rows = new List<ImportedMapItemRow>();
		var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

		for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
		{
			var row = worksheet.Row(rowNumber);
			if (row.CellsUsed().All(cell => cell.IsEmpty()))
			{
				continue;
			}

			rows.Add(new ImportedMapItemRow(
				rowNumber,
				GetString(row, headers["SubDistributorCode"]),
				GetString(row, headers["Principal"]),
				GetString(row, headers["CompanyItemCode"]),
				GetString(row, headers["CompanyItemName"]),
				GetString(row, headers["SubdItemCode"]),
				GetString(row, headers["SubdItemName"]),
				GetString(row, headers["UOM"]),
				TryGetDecimal(row.Cell(headers["Conversion"]), out var conversion) ? conversion : null,
				TryGetDecimal(row.Cell(headers["Price"]), out var price) ? price : null));
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

			if (header is "subdistributorcode" or "subdcode")
			{
				headers.TryAdd("SubDistributorCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "principal")
			{
				headers.TryAdd("Principal", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "companyitemcode")
			{
				headers.TryAdd("CompanyItemCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "companyitemname")
			{
				headers.TryAdd("CompanyItemName", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "subditemcode")
			{
				headers.TryAdd("SubdItemCode", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "subditemname")
			{
				headers.TryAdd("SubdItemName", cell.Address.ColumnNumber);
				continue;
			}

			if (header is "uom")
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

	private static bool IsTemplateWorksheet(IXLWorksheet worksheet)
	{
		return BuildHeaderMap(worksheet).ContainsKey("SubDistributorCode");
	}

	private static bool TryGetDecimal(IXLCell cell, out decimal value)
	{
		if (cell.DataType == XLDataType.Number)
		{
			value = (decimal)cell.GetDouble();
			return true;
		}

		return decimal.TryParse(cell.GetString().Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value)
			|| decimal.TryParse(cell.GetString().Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);
	}

	private static string GetString(IXLRow row, int columnNumber)
	{
		return row.Cell(columnNumber).GetString().Trim();
	}

	private static string Normalize(string value)
	{
		return value.Trim().ToLowerInvariant();
	}

	private static ImportMapItemRowResult CreateErrorRow(int rowNumber, string message)
	{
		return new ImportMapItemRowResult
		{
			RowNumber = rowNumber,
			Message = message,
			IsSuccess = false
		};
	}

	private sealed record ImportedMapItemRow(
		int RowNumber,
		string SubDistributorCode,
		string Principal,
		string CompanyItemCode,
		string CompanyItemName,
		string SubdItemCode,
		string SubdItemName,
		string UomName,
		decimal? Conversion,
		decimal? Price);
}