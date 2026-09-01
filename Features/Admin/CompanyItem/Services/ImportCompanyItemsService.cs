using System.Text.RegularExpressions;
using ClosedXML.Excel;
using STTproject.Features.Admin.CompanyItem.DTOs;
using STTproject.Features.Admin.PriceIncrease.DTOs;
using STTproject.Features.Admin.PriceIncrease.Services;

namespace STTproject.Features.Admin.CompanyItem.Services;

public sealed class ImportCompanyItemsService
{
    private const int MaxHeaderScanRows = 10;

    private readonly IAdminCompanyItemService _companyItemService;
    private readonly IAdminPriceIncreaseService _priceIncreaseService;

    private static readonly IReadOnlyDictionary<string, string[]> RequiredHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyItem Code"] = new[] { "SKU", "CompanyItemCode", "CompanyItem Code", "code", "Item Code", "ItemCode", "item code" },
            ["CompanyItem Name"] = new[] { "SKU Description", "CompanyItemName", "CompanyItem Name", "name", "Item Name", "ItemName", "item name" },
        };

    private static readonly IReadOnlyDictionary<string, string[]> OptionalHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Category"] = new[] { "Category", "category", "ppg" },
            ["Principal"] = new[] { "Principal", "Principals", "principal" },
            ["Price"] = new[] { "Price", "Stock Price" },
        };

    private static readonly IReadOnlyDictionary<string, string> AliasLookup = BuildAliasLookup();

    public ImportCompanyItemsService(IAdminCompanyItemService companyItemService, IAdminPriceIncreaseService priceIncreaseService)
    {
        _companyItemService = companyItemService;
        _priceIncreaseService = priceIncreaseService;
    }

    private static IReadOnlyDictionary<string, string> BuildAliasLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in RequiredHeaderMap.Concat(OptionalHeaderMap))
        {
            foreach (var alias in kvp.Value)
            {
                lookup.TryAdd(NormalizeHeader(alias), kvp.Key);
            }
        }
        return lookup;
    }

    // PHASE 1 — parse + validate directly from the uploaded Excel stream. No company items are created here.
    public async Task<CompanyItemImportResult> PrepareFromExcelAsync(Stream excelStream, string? principal, CancellationToken ct = default)
    {
        var result = new CompanyItemImportResult { Principal = principal };

        if (excelStream is null || !excelStream.CanRead)
        {
            result.AddError(0, string.Empty, "Import file is missing or unreadable.");
            return result;
        }

        var principalPreSelected = !string.IsNullOrWhiteSpace(principal);

        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            result.AddError(0, string.Empty, "The workbook does not contain any worksheets.");
            return result;
        }

        var detection = DetectHeaderRow(worksheet, principalPreSelected);

        if (detection.HeaderRowNumber == -1)
        {
            if (detection.BestCandidateRowNumber > 0)
            {
                var rawHeadersShown = string.Join(" | ", detection.BestCandidateHeaders.Select(h => $"\"{h}\""));
                result.AddError(detection.BestCandidateRowNumber, string.Empty,
                    $"Closest header row found at row {detection.BestCandidateRowNumber}, but it's missing: {string.Join(", ", detection.BestCandidateMissing)}. "
                    + $"Columns actually found on that row: {rawHeadersShown}");
            }
            else
            {
                var required = RequiredHeaderMap.Keys.ToList();
                if (!principalPreSelected) required.Add("Principal");

                result.AddError(0, string.Empty,
                    $"Could not find a header row within the first {MaxHeaderScanRows} rows containing the required columns: "
                    + string.Join(", ", required));
            }
            return result;
        }

        var columnIndex = detection.ColumnIndex;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? detection.HeaderRowNumber;

        result.OriginalHeaders = detection.AllHeaderColumns.Select(h => h.Header).ToList();

        // PASS 1 — parse and validate each row on its own merits (required fields, price format,
        // principal presence). If the code already exists in the database, this checks whether
        // the file's price differs and, if so, marks the row for a price-resolution decision
        // instead of blocking it outright.
        for (int rowNumber = detection.HeaderRowNumber + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.CellsUsed().All(cell => cell.IsEmpty()))
                continue;

            string? Get(string column) =>
                columnIndex.TryGetValue(column, out var colNum)
                    ? GetString(row, colNum)
                    : null;

            var priceText = Get("Price");
            decimal? price = !string.IsNullOrWhiteSpace(priceText) && decimal.TryParse(priceText, out var p) ? p : null;

            var rowPrincipal = principalPreSelected ? principal : Get("Principal");

            var rowResult = new CompanyItemImportRowResult
            {
                RowNumber = rowNumber,
                CompanyItemCode = Get("CompanyItemCode") ?? string.Empty,
                CompanyItemName = Get("CompanyItemName") ?? string.Empty,
                Category = Get("Category"),
                Principal = rowPrincipal,
                StockPrice = price
            };

            foreach (var (col, header) in detection.AllHeaderColumns)
            {
                rowResult.RawValues[header] = GetString(row, col);
            }

            if (string.IsNullOrWhiteSpace(rowResult.CompanyItemCode) &&
                string.IsNullOrWhiteSpace(rowResult.CompanyItemName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rowResult.CompanyItemCode))
                rowResult.Issues.Add("CompanyItem Code is required.");

            if (string.IsNullOrWhiteSpace(rowResult.CompanyItemName))
                rowResult.Issues.Add("CompanyItem Name is required.");

            if (!principalPreSelected && string.IsNullOrWhiteSpace(rowResult.Principal))
                rowResult.Issues.Add("Principal is required when no principal is selected for the import.");

            if (!string.IsNullOrWhiteSpace(priceText) && price is null)
                rowResult.Issues.Add($"Price '{priceText}' is not a valid number.");

            if (!string.IsNullOrWhiteSpace(rowResult.CompanyItemCode) && rowResult.Issues.Count == 0)
            {
                var existing = await _companyItemService.GetByItemCodeAsync(rowResult.CompanyItemCode, ct);
                if (existing != null)
                {
                    rowResult.IsExistingItem = true;
                    rowResult.ExistingCompanyItemId = existing.CompanyItemId;
                    rowResult.ExistingStockPrice = existing.StockPrice;

                    var priceDiffers = rowResult.StockPrice.HasValue &&
                        rowResult.StockPrice.Value != (existing.StockPrice ?? 0m);

                    if (priceDiffers)
                    {
                        rowResult.Warnings.Add(
                            $"CompanyItem Code '{rowResult.CompanyItemCode}' already exists with stock price " +
                            $"{(existing.StockPrice ?? 0m):N2}. The file specifies {rowResult.StockPrice:N2} — " +
                            "choose whether to update the stock price directly or schedule it as a formal price change.");
                    }
                    else
                    {
                        rowResult.Issues.Add($"CompanyItem Code '{rowResult.CompanyItemCode}' already exists and no price change was detected.");
                    }
                }
            }

            rowResult.IsSuccess = rowResult.Issues.Count == 0;
            result.Rows.Add(rowResult);
        }

        // PASS 2 — cross-row duplicate detection within the file. Duplicates (identical or
        // conflicting) are always warnings, never blocking — the user selects which row(s) to
        // actually commit.
        FlagDuplicateCodesWithinFile(result.Rows);

        // PASS 3 — build the prepared groups (one group per row for company items).
        foreach (var rowResult in result.Rows)
        {
            var group = new PreparedCompanyItemGroup(new List<CompanyItemImportRowResult> { rowResult })
            {
                // Rows needing a decision (duplicates or existing-item price changes) are never
                // auto-selected — the user must explicitly review and opt in.
                Selected = rowResult.IsSuccess && rowResult.Warnings.Count == 0
            };

            foreach (var issue in rowResult.Issues)
                group.Issues.Add(new CompanyItemImportIssue(rowResult.RowNumber, rowResult.CompanyItemCode, issue));

            result.PreparedGroups.Add(group);
        }

        if (result.Rows.Count == 0 && !result.HasIssues)
        {
            result.AddError(0, string.Empty, "No company item rows were found in the file.");
        }

        return result;
    }

    private static void FlagDuplicateCodesWithinFile(List<CompanyItemImportRowResult> rows)
    {
        var groupsByCode = rows
            .Where(r => r.IsSuccess && !string.IsNullOrWhiteSpace(r.CompanyItemCode))
            .GroupBy(r => r.CompanyItemCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var codeGroup in groupsByCode)
        {
            var rowsForCode = codeGroup.OrderBy(r => r.RowNumber).ToList();
            var first = rowsForCode[0];
            var rowNumbers = string.Join(" & ", rowsForCode.Select(r => r.RowNumber));

            var isFullDuplicate = rowsForCode.Skip(1).All(r =>
                string.Equals(r.CompanyItemName, first.CompanyItemName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Principal ?? string.Empty, first.Principal ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Category ?? string.Empty, first.Category ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                r.StockPrice == first.StockPrice);

            // Both cases are non-blocking warnings now — conflicting duplicates just need the
            // user to pick which row is correct, rather than being rejected outright.
            var message = isFullDuplicate
                ? $"CompanyItem Code '{first.CompanyItemCode}' is duplicated within the uploaded file with identical data (rows {rowNumbers}). Only one will be committed."
                : $"CompanyItem Code '{first.CompanyItemCode}' appears multiple times in the uploaded file with conflicting data (rows {rowNumbers}). Review and select only the correct row to commit.";

            foreach (var r in rowsForCode)
            {
                r.Warnings.Add(message);
            }
        }
    }

    private sealed record HeaderDetectionResult(
        int HeaderRowNumber,
        Dictionary<string, int> ColumnIndex,
        int BestCandidateRowNumber,
        string[] BestCandidateHeaders,
        List<string> BestCandidateMissing,
        List<(int Column, string Header)> AllHeaderColumns);

    private static HeaderDetectionResult DetectHeaderRow(IXLWorksheet worksheet, bool principalPreSelected)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var scanLimit = Math.Min(MaxHeaderScanRows, lastRow);

        int bestMissingCount = int.MaxValue;
        int bestCandidateRowNumber = -1;
        string[] bestCandidateHeaders = Array.Empty<string>();
        List<string> bestCandidateMissing = new();

        var requiredKeys = RequiredHeaderMap.Keys.ToList();
        if (!principalPreSelected) requiredKeys.Add("Principal");

        for (int rowNumber = 1; rowNumber <= scanLimit; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var usedCells = row.CellsUsed().ToList();
            if (usedCells.Count == 0) continue;

            var candidateHeaders = usedCells.Select(c => c.GetString().Trim()).ToArray();
            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var foundCanonicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var allHeaderColumns = usedCells
                .Select(c => (Column: c.Address.ColumnNumber, Header: c.GetString().Trim()))
                .Where(h => !string.IsNullOrWhiteSpace(h.Header))
                .ToList();

            foreach (var cell in usedCells)
            {
                var normalized = NormalizeHeader(cell.GetString());
                if (string.IsNullOrWhiteSpace(normalized)) continue;

                if (AliasLookup.TryGetValue(normalized, out var canonicalKey) && foundCanonicalKeys.Add(canonicalKey))
                {
                    columnIndex[canonicalKey.Replace(" ", "")] = cell.Address.ColumnNumber;
                }
            }

            var missing = requiredKeys.Where(key => !foundCanonicalKeys.Contains(key)).ToList();

            if (missing.Count == 0)
            {
                return new HeaderDetectionResult(rowNumber, columnIndex, -1, Array.Empty<string>(), new List<string>(), allHeaderColumns);
            }

            if (missing.Count < bestMissingCount)
            {
                bestMissingCount = missing.Count;
                bestCandidateRowNumber = rowNumber;
                bestCandidateHeaders = candidateHeaders;
                bestCandidateMissing = missing;
            }
        }

        return new HeaderDetectionResult(-1, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            bestCandidateRowNumber, bestCandidateHeaders, bestCandidateMissing, new List<(int, string)>());
    }

    private static string GetString(IXLRow row, int columnNumber)
    {
        var cell = row.Cell(columnNumber);
        if (cell.HasFormula)
            return cell.CachedValue.ToString()?.Trim() ?? string.Empty;
        return cell.GetString().Trim();
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s\.\#\/\-\,\:\(\)]+", " ").Trim();
    }

    // PHASE 2 — commit only the rows the user selected
    public async Task<int> CommitPreparedRowsAsync(IEnumerable<CompanyItemImportRowResult> rows, string? principal, int userId, CancellationToken ct = default)
    {
        if (userId <= 0 || rows is null)
            return 0;

        var validRows = rows.Where(r => r.IsSuccess && r.Issues.Count == 0).ToList();
        if (validRows.Count == 0) return 0;

        var committedCodesThisBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var committed = 0;

        foreach (var row in validRows)
        {
            // Safety net: even if the user selected more than one row sharing a code (e.g.
            // overriding a duplicate warning), only process the code once per batch.
            if (!string.IsNullOrWhiteSpace(row.CompanyItemCode) && !committedCodesThisBatch.Add(row.CompanyItemCode.Trim()))
            {
                row.Issues.Add($"CompanyItem Code '{row.CompanyItemCode}' was already committed earlier in this batch — skipped to avoid a duplicate item or double price change.");
                row.IsSuccess = false;
                continue;
            }

            try
            {
                if (row.IsExistingItem && row.ExistingCompanyItemId is int existingId)
                {
                    if (!row.StockPrice.HasValue)
                    {
                        row.Issues.Add("A price is required to apply a price change to an existing company item.");
                        row.IsSuccess = false;
                        continue;
                    }

                    if (row.PriceResolution == CompanyItemPriceResolutionMode.ScheduleIncrease)
                    {
                        var increaseAmount = row.StockPrice.Value - (row.ExistingStockPrice ?? 0m);
                        var (success, error) = await _priceIncreaseService.ScheduleIncreaseAsync(new AddPriceIncreaseDto
                        {
                            CompanyItemId = existingId,
                            PriceIncreaseAmount = increaseAmount,
                            EffectivityDate = row.PriceChangeEffectivityDate ?? DateTime.Today,
                            CreatedBy = userId
                        });

                        if (!success)
                        {
                            row.Issues.Add(error ?? "Unable to schedule the price change.");
                            row.IsSuccess = false;
                            continue;
                        }
                    }
                    else
                    {
                        var updated = await _companyItemService.UpdateStockPriceOnlyAsync(existingId, row.StockPrice.Value, userId, ct);
                        if (!updated)
                        {
                            row.Issues.Add("Unable to update the stock price for the existing company item.");
                            row.IsSuccess = false;
                            continue;
                        }
                    }

                    row.CompanyItemId = existingId;
                    committed++;
                    continue;
                }

                var effectivePrincipal = !string.IsNullOrWhiteSpace(principal) ? principal : row.Principal;

                var created = await _companyItemService.CreateCompanyItemAsync(new CompanyItemCreateDto
                {
                    ItemCode = row.CompanyItemCode,
                    ItemName = row.CompanyItemName,
                    Category = row.Category,
                    Principal = effectivePrincipal,
                    StockPrice = row.StockPrice,
                    IsActive = true,
                    CreatedBy = userId
                }, cancellationToken: ct);

                row.CompanyItemId = created?.CompanyItemId;

                if (created?.CompanyItemId is int newId && row.StockPrice is decimal initialPrice)
                {
                    await _companyItemService.AddInitialPriceHistoryAsync(newId, initialPrice, userId, cancellationToken: ct);
                }

                committed++;
            }
            catch (Exception ex)
            {
                row.Issues.Add($"Save failed: {ex.Message}");
                row.IsSuccess = false;
            }
        }

        return committed;
    }

    // PHASE 3 — build a downloadable Excel report: original columns + an "Error" column, failed rows only.
    public byte[] GenerateErrorReportExcel(CompanyItemImportResult result)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Errors");

        var headers = result.OriginalHeaders.Count > 0
            ? result.OriginalHeaders
            : new List<string> { "CompanyItem Code", "CompanyItem Name", "Principal", "Category", "Price" };

        for (int i = 0; i < headers.Count; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        var errorColumn = headers.Count + 1;
        sheet.Cell(1, errorColumn).Value = "Error";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEA");

        var failedRows = result.Rows.Where(r => !r.IsSuccess).ToList();

        int excelRow = 2;
        foreach (var row in failedRows)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                row.RawValues.TryGetValue(headers[i], out var value);
                sheet.Cell(excelRow, i + 1).Value = value ?? string.Empty;
            }

            var errorCell = sheet.Cell(excelRow, errorColumn);
            errorCell.Value = string.Join("; ", row.Issues);
            errorCell.Style.Font.FontColor = XLColor.FromHtml("#A32D2D");
            errorCell.Style.Font.Bold = true;

            excelRow++;
        }

        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static readonly string[] TemplateHeaders =
    {
        "CompanyItem Code", "CompanyItem Name", "Principal", "Category", "Price"
    };

    public async Task<byte[]> GenerateTemplateExcelAsync()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("CompanyItems");

        for (int i = 0; i < TemplateHeaders.Length; i++)
            sheet.Cell(1, i + 1).Value = TemplateHeaders[i];

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EDE9FB");

        sheet.Cell(2, 1).Value = "ITEM-0001";
        sheet.Cell(2, 2).Value = "Sample Item Name";
        sheet.Cell(2, 3).Value = "Sample Principal";
        sheet.Cell(2, 4).Value = "General";
        sheet.Cell(2, 5).Value = 100.00;
        sheet.Row(2).Style.Font.Italic = true;
        sheet.Row(2).Style.Font.FontColor = XLColor.FromHtml("#A09ABF");

        var principals = (await _companyItemService.GetAllPrincipalsAsync())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        const int lastDataRow = 500;

        if (principals.Count > 0)
        {
            var refSheet = workbook.Worksheets.Add("RefData");
            refSheet.Visibility = XLWorksheetVisibility.Hidden;

            for (int i = 0; i < principals.Count; i++)
                refSheet.Cell(i + 1, 1).Value = principals[i];

            var principalRange = refSheet.Range(1, 1, principals.Count, 1);
            workbook.NamedRanges.Add("PrincipalList", principalRange);

            var principalValidation = sheet.Range($"C2:C{lastDataRow}").CreateDataValidation();
            principalValidation.List(principalRange);
            principalValidation.IgnoreBlanks = true;
            principalValidation.ShowInputMessage = true;
            principalValidation.InputTitle = "Principal";
            principalValidation.InputMessage = "Select a principal, or leave blank if one is pre-selected for this import.";
            principalValidation.ShowErrorMessage = false;
        }

        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}