using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using STTproject.Features.Admin.CompanyItem.DTOs;
using STTproject.Features.Admin.PriceIncrease.DTOs;
using STTproject.Features.Admin.PriceIncrease.Services;
using STTproject.Features.Admin.Subdistributor.DTOs;

namespace STTproject.Features.Admin.CompanyItem.Services;

public sealed class ImportCompanyItemsService
{
    private const int MaxHeaderScanRows = 10;

    private readonly IAdminCompanyItemService _companyItemService;
    private readonly IAdminPriceIncreaseService _priceIncreaseService;

    private static readonly IReadOnlyDictionary<string, string[]> RequiredHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyItem Code"] = new[]
            {
                "SKU", "Sku Code", "CompanyItemCode", "CompanyItem Code",
                "code", "Item Code", "ItemCode", "item code"
            },
            ["CompanyItem Name"] = new[]
            {
                "SKU Description", "Item Description", "CompanyItemName", "CompanyItem Name",
                "name", "Item Name", "ItemName", "item name"
            },
            ["Stock UOM"] = new[]
            {
                "UOM", "StockUomName", "Stock UOM", "Standard UOM", "Company Stock UOM", "Stock Unit",
                "stock uom", "stock uom name"
            }
        };

    private static readonly IReadOnlyDictionary<string, string[]> OptionalHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["PPG"] = new[] { "PPG" },
            ["Category"] = new[] { "Category", "Categories" },
            ["Principal"] = new[] { "Principal", "Principals" },

            ["Price"] = new[] { "Price", "Stock Price", "SPrice" },
            ["RegionalPrice"] = new[]
            {
                "Regional Price", "Regional Stock Price", "Stock Price Regional", "Sheet 1 Price"
            },
            ["NationalPrice"] = new[]
            {
                "National Price", "National Stock Price", "Stock Price National","List Price"
            },

            ["EffectivityDate"] = new[]
            {
                "Effectivity Date", "Effective Date", "Effectivity"
            }
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

    // ═════════════════════════════════════════════════════════════════════════
    //  STANDARD IMPORT — create new items, update the current price of existing ones
    // ═════════════════════════════════════════════════════════════════════════

    // PHASE 1 — parse + validate directly from the uploaded Excel stream. No company items are created here.
    public async Task<CompanyItemImportResult> PrepareFromExcelAsync(Stream excelStream, string? principal, CancellationToken ct = default)
    {
        var result = new CompanyItemImportResult { Principal = principal, Mode = CompanyItemImportMode.Standard };

        if (excelStream is null || !excelStream.CanRead)
        {
            result.AddError(0, string.Empty, "Import file is missing or unreadable.");
            return result;
        }

        var principalPreSelected = !string.IsNullOrWhiteSpace(principal);

        if (excelStream.CanSeek)
        {
            excelStream.Position = 0;
        }

        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault();

        if (worksheet is null)
        {
            result.AddError(0, string.Empty, "The workbook does not contain any worksheets.");
            return result;
        }

        var requiredKeys = RequiredHeaderMap.Keys.ToList();
        if (!principalPreSelected) requiredKeys.Add("Principal");

        var detection = DetectHeaderRow(worksheet, requiredKeys);

        if (detection.HeaderRowNumber == -1)
        {
            AddHeaderDetectionError(result, detection, requiredKeys);
            return result;
        }

        var columnIndex = detection.ColumnIndex;
        var lastRow = worksheet.LastRowUsed()?.RowNumber()
            ?? detection.HeaderRowNumber;

        result.OriginalHeaders = detection.AllHeaderColumns
            .Select(h => h.Header)
            .ToList();

        // PASS 1: Parse, validate, and prepare each row.
        for (int rowNumber = detection.HeaderRowNumber + 1;
            rowNumber <= lastRow;
            rowNumber++)
        {
            var row = worksheet.Row(rowNumber);

            if (row.CellsUsed().All(cell => cell.IsEmpty()))
                continue;

            string? Get(string column) =>
                columnIndex.TryGetValue(column, out var colNum)
                    ? GetString(row, colNum)
                    : null;

            var rowPrincipal = principalPreSelected
                ? principal
                : Get("Principal");

            var rowResult = new CompanyItemImportRowResult
            {
                RowNumber = rowNumber,
                CompanyItemCode = Get("CompanyItemCode") ?? string.Empty,
                CompanyItemName = Get("CompanyItemName") ?? string.Empty,
                PPG = Get("PPG"),
                Category = Get("Category"),
                Principal = rowPrincipal
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

            decimal? ReadPrice(string key, string label)
            {
                var text = Get(key);

                if (string.IsNullOrWhiteSpace(text))
                    return null;

                if (decimal.TryParse(
                        text,
                        NumberStyles.Number,
                        CultureInfo.CurrentCulture,
                        out var value) &&
                    value > 0)
                {
                    return value;
                }

                rowResult.Issues.Add(
                    $"{label} '{text}' is not a valid number.");

                return null;
            }

            // Read each source column separately before interpreting its meaning.
            var importedPrice = ReadPrice("Price", "Price");
            // Sheet 1 Price only means something for KCPI. For every other principal the column is
            // ignored completely: it is not parsed, so a 0 or text in it can never fail the row.
            var regionalApplies = string.Equals(rowResult.Principal?.Trim(), "KCPI", StringComparison.OrdinalIgnoreCase);
            var importedRegionalPrice = regionalApplies ? ReadPrice("RegionalPrice", "Sheet 1 Price") : null;
            var importedNationalPrice = ReadPrice("NationalPrice", "List Price");

            rowResult.StockUomName = Get("StockUOM")?.Trim() ?? string.Empty;

            // Parse effectivity date.
            if (columnIndex.TryGetValue("EffectivityDate", out var effCol)
                && !row.Cell(effCol).IsEmpty())
            {
                var cell = row.Cell(effCol);

                if (cell.TryGetValue(out DateTime effDate))
                {
                    rowResult.PriceChangeEffectivityDate = effDate.Date;
                }
                else if (DateTime.TryParse(
                    GetString(row, effCol),
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
                {
                    rowResult.PriceChangeEffectivityDate = parsedDate.Date;
                }
                else
                {
                    rowResult.Issues.Add(
                        $"Effectivity Date '{GetString(row, effCol)}' is not a valid date.");
                }
            }

            // Validate required fields first.
            if (string.IsNullOrWhiteSpace(rowResult.CompanyItemCode))
                rowResult.Issues.Add("CompanyItem Code is required.");

            if (string.IsNullOrWhiteSpace(rowResult.CompanyItemName))
                rowResult.Issues.Add("CompanyItem Name is required.");

            if (string.IsNullOrWhiteSpace(rowResult.StockUomName))
                rowResult.Issues.Add("Stock UOM is required.");

            if (!principalPreSelected &&
                string.IsNullOrWhiteSpace(rowResult.Principal))
            {
                rowResult.Issues.Add(
                    "Principal is required when no principal is selected for the import.");
            }

            var isKcpi = string.Equals(
                rowResult.Principal?.Trim(),
                "KCPI",
                StringComparison.OrdinalIgnoreCase);

            // Interpret prices according to the selected principal.
            if (isKcpi)
            {
                rowResult.StockPrice = importedPrice;
                rowResult.StockPriceRegional = importedRegionalPrice;
                rowResult.StockPriceNational = importedNationalPrice;

                if (importedPrice.HasValue)
                {
                    rowResult.Issues.Add(
                        "KCPI uses Sheet 1 Price for Regional Price and List Price for National Price. " +
                        "Do not provide a separate Price/Stock Price.");
                }
            }
            else
            {
                // For non-KCPI principals, List Price is treated as Stock Price.
                rowResult.StockPrice = importedPrice ?? importedNationalPrice;
                rowResult.StockPriceRegional = null;
                rowResult.StockPriceNational = null;

                if (importedPrice.HasValue && importedNationalPrice.HasValue)
                {
                    rowResult.Issues.Add(
                        "Provide either Price or List Price, not both, for a non-KCPI principal.");
                }

                if (importedRegionalPrice.HasValue)
                {
                    rowResult.Issues.Add(
                        "Sheet 1 Price is only supported for KCPI. " +
                        "For other principals, provide List Price as the Stock Price.");
                }
            }

            // Look up the item only after basic field validation.
            if (!string.IsNullOrWhiteSpace(rowResult.CompanyItemCode)
                && rowResult.Issues.Count == 0)
            {
                var existingItem = await _companyItemService
                    .GetByItemCodeAsync(rowResult.CompanyItemCode, ct);

                if (existingItem is null)
                {
                    // New item: require a complete, valid price configuration.
                    ValidatePriceConfiguration(rowResult);
                    rowResult.Action = CompanyItemRowAction.Create;
                }
                else
                {
                    rowResult.IsExistingItem = true;
                    rowResult.ExistingCompanyItemId =
                        existingItem.CompanyItemId;

                    rowResult.ExistingStockPrice = existingItem.StockPrice;
                    rowResult.ExistingStockPriceRegional =
                        existingItem.StockPriceRegional;
                    rowResult.ExistingStockPriceNational =
                        existingItem.StockPriceNational;

                    // Do not allow an import to silently change an item's principal.
                    if (!string.Equals(
                        existingItem.Principal?.Trim(),
                        rowResult.Principal?.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        rowResult.Issues.Add(
                            $"CompanyItem Code '{rowResult.CompanyItemCode}' already belongs to " +
                            $"principal '{existingItem.Principal}'. The imported principal is " +
                            $"'{rowResult.Principal}'. These principals must match.");
                    }
                    else
                    {
                        // Validate the existing item's pricing structure.
                        ValidateExistingItemPrices(rowResult);

                        // Only calculate changes when validation succeeds.
                        if (rowResult.Issues.Count == 0)
                        {
                            rowResult.ExistingStockUomName = existingItem.StockUomName;
                            rowResult.ExistingIsActive = existingItem.IsActive;

                            var existingUom = existingItem.StockUomName?.Trim() ?? string.Empty;
                            var fileUom = rowResult.StockUomName?.Trim() ?? string.Empty;
                            var uomDiffers = existingUom.Length > 0 && fileUom.Length > 0
                                && !string.Equals(existingUom, fileUom, StringComparison.OrdinalIgnoreCase);

                            // Only a blank Stock UOM is filled in. An existing one is never overwritten by an
                            // import, because sub-distributor UOMs may already be mapped against it.
                            rowResult.BackfillStockUom = existingUom.Length == 0 && fileUom.Length > 0;

                            var uomNote = uomDiffers
                                ? $" The file's Stock UOM '{fileUom}' differs from the item's '{existingUom}' and will not be changed."
                                : string.Empty;

                            var changes = rowResult.GetPriceChanges().ToList();

                            if (changes.Count > 0)
                            {
                                var summary = string.Join("; ", changes.Select(c =>
                                    $"{c.Label} {(c.OldPrice ?? 0m):N2} → {c.NewPrice:N2}"));

                                rowResult.Warnings.Add(
                                    $"CompanyItem Code '{rowResult.CompanyItemCode}' already exists. " +
                                    $"Changes in file: {summary} — " +
                                    "choose whether to update the stock price directly " +
                                    "or schedule it as a formal price change." + uomNote);
                                rowResult.Action = CompanyItemRowAction.UpdatePrice;

                                if (rowResult.PriceChangeEffectivityDate is DateTime d
                                    && d > DateTime.Today)
                                {
                                    rowResult.PriceResolution =
                                        CompanyItemPriceResolutionMode.ScheduleIncrease;
                                }
                            }
                            else
                            {
                                if (rowResult.BackfillStockUom)
                                {
                                    // Nothing to decide: the item simply gets its missing Stock UOM.
                                    rowResult.Action = CompanyItemRowAction.FillStockUom;
                                }
                                else if (uomDiffers)
                                {
                                    rowResult.Issues.Add(
                                        $"CompanyItem Code '{rowResult.CompanyItemCode}' already exists. No price change was detected, " +
                                        $"and the file's Stock UOM '{fileUom}' differs from the item's '{existingUom}' — " +
                                        "change the Stock UOM from the Company Item page.");
                                }
                                else
                                {
                                    rowResult.Issues.Add(
                                        $"CompanyItem Code '{rowResult.CompanyItemCode}' " +
                                        "already exists and no price change was detected.");
                                }
                            }
                        }
                    }
                }
            }

            rowResult.IsSuccess = rowResult.Issues.Count == 0;
            result.Rows.Add(rowResult);
        }

        // PASS 2: Flag duplicate codes within the file.
        // These remain non-blocking warnings.
        FlagDuplicateCodesWithinFile(result.Rows);

        // PASS 3: Build prepared groups. Nothing starts selected; the user picks what to commit.
        foreach (var rowResult in result.Rows)
        {
            var group = new PreparedCompanyItemGroup(
                new List<CompanyItemImportRowResult> { rowResult })
            {
                Selected = false
            };

            foreach (var issue in rowResult.Issues)
            {
                group.Issues.Add(
                    new CompanyItemImportIssue(
                        rowResult.RowNumber,
                        rowResult.CompanyItemCode,
                        issue));
            }

            result.PreparedGroups.Add(group);
        }

        if (result.Rows.Count == 0 && !result.HasIssues)
        {
            result.AddError(
                0,
                string.Empty,
                "No company item rows were found in the file.");
        }

        return result;
    }

    // Looks at the file first and picks the import mode: a file with an Effectivity Date column is a
    // price-history file (items must already exist); anything else is a standard add/update file.
    public async Task<CompanyItemImportResult> PrepareAutoFromExcelAsync(Stream excelStream, string? principal, CancellationToken ct = default)
    {
        if (excelStream is null || !excelStream.CanRead)
        {
            var bad = new CompanyItemImportResult { Principal = principal };
            bad.AddError(0, string.Empty, "Import file is missing or unreadable.");
            return bad;
        }

        using var copy = new MemoryStream();
        await excelStream.CopyToAsync(copy, ct);
        var bytes = copy.ToArray();

        var historical = false;
        try
        {
            using var probeStream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(probeStream);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet is not null)
            {
                var probe = DetectHeaderRow(sheet, new[] { "CompanyItem Code" });
                historical = probe.HeaderRowNumber != -1 && probe.ColumnIndex.ContainsKey("EffectivityDate");
            }
        }
        catch
        {
            // An unreadable file is reported by the real prepare below.
        }

        using var importStream = new MemoryStream(bytes);
        return historical
            ? await PrepareHistoricalFromExcelAsync(importStream, principal, ct)
            : await PrepareFromExcelAsync(importStream, principal, ct);
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
                string.Equals(r.PPG ?? string.Empty, first.PPG ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                r.StockPrice == first.StockPrice &&
                r.StockPriceRegional == first.StockPriceRegional &&
                r.StockPriceNational == first.StockPriceNational &&
                string.Equals(r.StockUomName ?? string.Empty, first.StockUomName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            var message = isFullDuplicate
                ? $"CompanyItem Code '{first.CompanyItemCode}' is duplicated within the uploaded file with identical data (rows {rowNumbers}). Only one will be committed."
                : $"CompanyItem Code '{first.CompanyItemCode}' appears multiple times in the uploaded file with conflicting data (rows {rowNumbers}). Review and select only the correct row to commit.";

            foreach (var r in rowsForCode)
            {
                r.Warnings.Add(message);
            }
        }
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
            // Safety net: only process a given code once per batch.
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
                    var changes = row.GetPriceChanges().ToList();
                    if (changes.Count == 0 && !row.BackfillStockUom)
                    {
                        row.Issues.Add("A price is required to apply a price change to an existing company item.");
                        row.IsSuccess = false;
                        continue;
                    }

                    var effectivity = row.PriceChangeEffectivityDate ?? DateTime.Today;
                    decimal? directStock = null, directRegional = null, directNational = null;
                    var errors = new List<string>();

                    foreach (var change in changes)
                    {
                        // Scheduling is an increase from an existing price; with no current price
                        // there is nothing to increase from, so that price is set directly instead.
                        var canSchedule = row.PriceResolution == CompanyItemPriceResolutionMode.ScheduleIncrease
                                          && change.OldPrice.HasValue;

                        if (canSchedule)
                        {
                            var (ok, err) = await _priceIncreaseService.ScheduleIncreaseAsync(new AddPriceIncreaseDto
                            {
                                CompanyItemId = existingId,
                                PriceIncreaseAmount = change.NewPrice - change.OldPrice!.Value,
                                EffectivityDate = effectivity,
                                PriceListType = change.PriceListType,
                                CreatedBy = userId
                            });
                            if (!ok) errors.Add($"{change.Label}: {err ?? "Unable to schedule the price change."}");
                        }
                        else
                        {
                            switch (change.PriceListType)
                            {
                                case null: directStock = change.NewPrice; break;
                                case PriceListTypes.Regional: directRegional = change.NewPrice; break;
                                case PriceListTypes.National: directNational = change.NewPrice; break;
                            }
                        }
                    }

                    if (directStock.HasValue || directRegional.HasValue || directNational.HasValue)
                    {
                        // One call only. A second call would find the price already updated and add nothing,
                        // but it would be a wasted round trip and a confusing duplicate in the code.
                        var updated = await _companyItemService.UpdateStockPricesAsync(
                            existingId, directStock, directRegional, directNational, userId, ct);

                        if (!updated)
                            errors.Add("Unable to update the stock price for the existing company item.");
                    }

                    if (row.BackfillStockUom && !string.IsNullOrWhiteSpace(row.StockUomName))
                    {
                        // Null fields on the DTO keep the current values; only the Stock UOM is set.
                        var filled = await _companyItemService.UpdateCompanyItemAsync(new CompanyItemUpdateDto
                        {
                            CompanyItemId = existingId,
                            StockUomName = row.StockUomName.Trim(),
                            IsActive = row.ExistingIsActive ?? true,
                            UpdatedBy = userId
                        });
                        if (filled == null) errors.Add("Unable to set the Stock UOM for the existing company item.");
                    }

                    if (errors.Count > 0)
                    {
                        row.Issues.AddRange(errors);
                        row.IsSuccess = false;
                        continue;
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
                    PPG = row.PPG,
                    Principal = effectivePrincipal,
                    StockPrice = row.StockPrice,
                    StockPriceRegional = row.StockPriceRegional,
                    StockPriceNational = row.StockPriceNational,
                    Category = row.Category,
                    StockUomName = row.StockUomName?.Trim() ?? string.Empty,
                    IsActive = true,
                    CreatedBy = userId
                }, cancellationToken: ct);

                row.CompanyItemId = created?.CompanyItemId;

                if (created?.CompanyItemId is int newId)
                {
                    if (row.StockPrice is decimal s)
                        await _companyItemService.AddInitialPriceHistoryAsync(newId, s, userId, null, ct);
                    if (row.StockPriceRegional is decimal r)
                        await _companyItemService.AddInitialPriceHistoryAsync(newId, r, userId, PriceListTypes.Regional, ct);
                    if (row.StockPriceNational is decimal n)
                        await _companyItemService.AddInitialPriceHistoryAsync(newId, n, userId, PriceListTypes.National, ct);
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

    // ═════════════════════════════════════════════════════════════════════════
    //  HISTORICAL PRICE IMPORT — add past / scheduled prices to EXISTING items
    //  One file row = one effectivity date with its price(s). An item is one card.
    // ═════════════════════════════════════════════════════════════════════════

    private sealed record HistoricalEntry(
        string? Type, string Label, DateTime Date, decimal Price, CompanyItemImportRowResult Row);

    // PHASE 1 (historical) — read and validate. Nothing is saved here.
    public async Task<CompanyItemImportResult> PrepareHistoricalFromExcelAsync(Stream excelStream, string? principal, CancellationToken ct = default)
    {
        var result = new CompanyItemImportResult { Principal = principal, Mode = CompanyItemImportMode.PriceHistory };
        var principalPreSelected = !string.IsNullOrWhiteSpace(principal);

        if (excelStream is null || !excelStream.CanRead)
        {
            result.AddError(0, string.Empty, "Import file is missing or unreadable.");
            return result;
        }

        if (excelStream.CanSeek)
            excelStream.Position = 0;

        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault();

        if (worksheet is null)
        {
            result.AddError(0, string.Empty, "The workbook does not contain any worksheets.");
            return result;
        }

        var requiredKeys = new List<string> { "CompanyItem Code", "EffectivityDate" };
        var detection = DetectHeaderRow(worksheet, requiredKeys);

        if (detection.HeaderRowNumber == -1)
        {
            AddHeaderDetectionError(result, detection, requiredKeys);
            return result;
        }

        var columnIndex = detection.ColumnIndex;

        if (!columnIndex.ContainsKey("Price")
            && !columnIndex.ContainsKey("RegionalPrice")
            && !columnIndex.ContainsKey("NationalPrice"))
        {
            result.AddError(detection.HeaderRowNumber, string.Empty,
                "A price column is required: Price (or List Price), or Regional Price and National Price for KCPI.");
            return result;
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? detection.HeaderRowNumber;
        result.OriginalHeaders = detection.AllHeaderColumns.Select(h => h.Header).ToList();

        var itemCache = new Dictionary<string, CompanyItemListDto?>(StringComparer.OrdinalIgnoreCase);

        for (int rowNumber = detection.HeaderRowNumber + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.CellsUsed().All(cell => cell.IsEmpty()))
                continue;

            string? Get(string column) =>
                columnIndex.TryGetValue(column, out var colNum) ? GetString(row, colNum) : null;

            var rowResult = new CompanyItemImportRowResult
            {
                RowNumber = rowNumber,
                CompanyItemCode = Get("CompanyItemCode") ?? string.Empty,
                CompanyItemName = Get("CompanyItemName") ?? string.Empty,
                PPG = Get("PPG")
            };

            foreach (var (col, header) in detection.AllHeaderColumns)
                rowResult.RawValues[header] = GetString(row, col);

            // ── Effectivity date (required) ─────────────────────────────────────
            DateTime? effectivity = null;
            if (columnIndex.TryGetValue("EffectivityDate", out var effCol) && !row.Cell(effCol).IsEmpty())
            {
                if (TryReadEffectivityDate(row.Cell(effCol), out var effDate))
                    effectivity = effDate;
                else
                    rowResult.Issues.Add($"Effectivity Date '{GetString(row, effCol)}' is not a valid date. Use MM/dd/yyyy or yyyy-MM-dd.");
            }
            else
            {
                rowResult.Issues.Add("Effectivity Date is required.");
            }

            rowResult.PriceChangeEffectivityDate = effectivity;
            rowResult.Action = effectivity.HasValue && effectivity.Value > DateTime.Today
                ? CompanyItemRowAction.SchedulePrice
                : CompanyItemRowAction.AddPriceHistory;

            // ── Prices ──────────────────────────────────────────────────────────
            var priceText = Get("Price");
            var regionalText = Get("RegionalPrice");
            var nationalText = Get("NationalPrice");

            // ── Item (must already exist) ───────────────────────────────────────
            CompanyItemListDto? item = null;
            var code = rowResult.CompanyItemCode.Trim();

            if (code.Length == 0)
            {
                rowResult.Issues.Add("CompanyItem Code is required.");
            }
            else
            {
                if (!itemCache.TryGetValue(code, out item))
                {
                    item = await _companyItemService.GetByItemCodeAsync(code, ct);
                    itemCache[code] = item;
                }

                if (item is null)
                {
                    // Not found: handled below as a new item to create.
                }
            }

            // The saved item's principal wins; a new item takes the assigned or file principal.
            var newItemPrincipal = principalPreSelected ? principal : Get("Principal");
            var rowIsKcpi = string.Equals((item?.Principal ?? newItemPrincipal)?.Trim(), "KCPI", StringComparison.OrdinalIgnoreCase);

            var importedPrice = ReadHistoricalPrice(priceText, "Price", rowResult);
            // Sheet 1 Price is ignored completely unless the principal is KCPI.
            var importedRegionalPrice = rowIsKcpi ? ReadHistoricalPrice(regionalText, "Sheet 1 Price", rowResult) : null;
            var importedNationalPrice = ReadHistoricalPrice(nationalText, "List Price", rowResult);

            // ── Item not found: it is created, but only with complete, consistent data ──
            if (item is null && code.Length > 0)
            {
                rowResult.Principal = newItemPrincipal;
                rowResult.StockUomName = Get("StockUOM")?.Trim() ?? string.Empty;
                rowResult.Category = Get("Category");

                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(rowResult.CompanyItemName)) missing.Add("an item name");
                if (string.IsNullOrWhiteSpace(rowResult.StockUomName)) missing.Add("a Stock UOM");
                if (string.IsNullOrWhiteSpace(rowResult.Principal)) missing.Add("a principal");

                if (missing.Count > 0)
                {
                    rowResult.Issues.Add(
                        $"CompanyItem Code '{code}' was not found, and it can't be created because the row has no {string.Join(", ", missing)}.");
                }
                else
                {
                    if (rowIsKcpi)
                    {
                        rowResult.StockPrice = importedPrice;
                        rowResult.StockPriceRegional = importedRegionalPrice;
                        rowResult.StockPriceNational = importedNationalPrice;
                    }
                    else
                    {
                        rowResult.StockPrice = importedPrice ?? importedNationalPrice;
                        if (importedPrice.HasValue && importedNationalPrice.HasValue)
                            rowResult.Issues.Add("Provide either Price or List Price, not both, for a non-KCPI principal.");
                    }

                    if (rowResult.Issues.Count == 0)
                        ValidatePriceConfiguration(rowResult);

                    rowResult.Action = CompanyItemRowAction.CreateWithHistory;
                }
            }

            if (item is not null)
            {
                rowResult.IsExistingItem = true;
                rowResult.ExistingCompanyItemId = item.CompanyItemId;
                rowResult.ExistingStockPrice = item.StockPrice;
                rowResult.ExistingStockPriceRegional = item.StockPriceRegional;
                rowResult.ExistingStockPriceNational = item.StockPriceNational;
                rowResult.Principal = item.Principal;

                if (string.IsNullOrWhiteSpace(rowResult.CompanyItemName)) rowResult.CompanyItemName = item.ItemName;
                if (string.IsNullOrWhiteSpace(rowResult.PPG)) rowResult.PPG = item.PPG;

                // The principal always comes from the saved item. A different one in the file is a mistake.
                var filePrincipal = Get("Principal");
                if (!string.IsNullOrWhiteSpace(filePrincipal) &&
                    !string.Equals(filePrincipal.Trim(), item.Principal?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    rowResult.Issues.Add(
                        $"CompanyItem Code '{code}' belongs to principal '{item.Principal}', but the file says " +
                        $"'{filePrincipal}'. These principals must match.");
                }

                var isKcpi = string.Equals(item.Principal?.Trim(), "KCPI", StringComparison.OrdinalIgnoreCase);

                if (isKcpi)
                {
                    rowResult.StockPrice = importedPrice;
                    rowResult.StockPriceRegional = importedRegionalPrice;
                    rowResult.StockPriceNational = importedNationalPrice;

                    if (importedPrice.HasValue)
                    {
                        rowResult.Issues.Add(
                            "KCPI uses Sheet 1 Price for Regional Price and List Price for National Price. " +
                            "Do not provide a separate Price/Stock Price.");
                    }
                }
                else
                {
                    rowResult.StockPrice = importedPrice ?? importedNationalPrice;
                    rowResult.StockPriceRegional = null;
                    rowResult.StockPriceNational = null;

                    if (importedPrice.HasValue && importedNationalPrice.HasValue)
                        rowResult.Issues.Add("Provide either Price or List Price, not both, for a non-KCPI principal.");

                    if (importedRegionalPrice.HasValue)
                        rowResult.Issues.Add("Sheet 1 Price is only supported for KCPI. For other principals, provide List Price as the Stock Price.");
                }

                if (rowResult.Issues.Count == 0 &&
                    !rowResult.StockPrice.HasValue && !rowResult.StockPriceRegional.HasValue && !rowResult.StockPriceNational.HasValue)
                {
                    rowResult.Issues.Add("A price is required on every row.");
                }

                // Only what the file writes is checked. The saved item's other price lists are not: a history
                // import must not be blocked because the item is missing a current price or has an extra one.
                if (rowResult.Issues.Count == 0)
                    ValidateHistoryRowPrices(rowResult, isKcpi);
            }

            rowResult.IsSuccess = rowResult.Issues.Count == 0;
            result.Rows.Add(rowResult);
        }

        // One card per item, rows in date order.
        foreach (var itemGroup in result.Rows.GroupBy(r => (r.CompanyItemCode ?? string.Empty).Trim().ToUpperInvariant()))
        {
            var groupRows = itemGroup
                .OrderBy(r => r.PriceChangeEffectivityDate ?? DateTime.MaxValue)
                .ThenBy(r => r.RowNumber)
                .ToList();

            await ValidateHistoricalGroupAsync(groupRows, ct);

            var group = new PreparedCompanyItemGroup(groupRows) { Selected = false };

            foreach (var r in groupRows)
                foreach (var issue in r.Issues)
                    group.Issues.Add(new CompanyItemImportIssue(r.RowNumber, r.CompanyItemCode, issue));

            result.PreparedGroups.Add(group);
        }

        if (result.Rows.Count == 0 && !result.HasIssues)
            result.AddError(0, string.Empty, "No price rows were found in the file.");

        return result;
    }

    // US month-first, as documented in the error message. Excel date cells are read directly and never
    // go through text parsing, and the server's culture is never used.
    private static readonly string[] EffectivityDateFormats =
    {
        "MM/dd/yyyy HH:mm:ss", "M/d/yyyy H:mm:ss", "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt", "M/d/yyyy H:mm",
        "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy/MM/dd",
        "MMM d, yyyy", "MMMM d, yyyy", "d-MMM-yyyy", "dd-MMM-yy"
    };

    private static bool TryReadEffectivityDate(IXLCell cell, out DateTime date)
    {
        date = default;

        try
        {
            if (cell.DataType == XLDataType.DateTime)
            {
                date = cell.GetDateTime().Date;
                return true;
            }

            if (cell.DataType == XLDataType.Number)
            {
                var serial = cell.GetDouble();
                if (serial > 20000 && serial < 80000)
                {
                    date = DateTime.FromOADate(serial).Date;
                    return true;
                }
                return false;
            }
        }
        catch
        {
            // fall through to the text parsing below
        }

        var text = cell.HasFormula ? cell.CachedValue.ToString()?.Trim() : cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (DateTime.TryParseExact(text, EffectivityDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
        {
            date = exact.Date;
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var loose))
        {
            date = loose.Date;
            return true;
        }

        return false;
    }

    private static decimal? ReadHistoricalPrice(string? text, string label, CompanyItemImportRowResult row)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) && value > 0)
            return value;

        row.Issues.Add($"{label} '{text}' is not a valid number.");
        return null;
    }

    private static bool SameListType(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static void AddWarningOnce(CompanyItemImportRowResult row, string message)
    {
        if (!row.Warnings.Contains(message))
            row.Warnings.Add(message);
    }

    // Item-level checks. An item is imported all-or-nothing, so one bad row holds the whole item back.
    private async Task ValidateHistoricalGroupAsync(List<CompanyItemImportRowResult> rows, CancellationToken ct)
    {
        void Finish()
        {
            foreach (var r in rows) r.IsSuccess = r.Issues.Count == 0;
        }

        // 1) A row with its own error blocks the rest of that item.
        if (rows.Any(r => r.Issues.Count > 0))
        {
            var failing = rows.Where(r => r.Issues.Count > 0).Select(r => r.RowNumber).ToList();
            foreach (var r in rows.Where(r => r.Issues.Count == 0))
            {
                r.Issues.Add(
                    $"On hold: {FormatRowsText(failing)} for this item {(failing.Count == 1 ? "has an error" : "have errors")}. " +
                    "An item's price history is only imported when all of its rows are valid.");
            }
            Finish();
            return;
        }

        var isNew = !rows[0].ExistingCompanyItemId.HasValue;
        var itemId = isNew ? 0 : rows[0].ExistingCompanyItemId!.Value;

        if (isNew)
        {
            // A new item is created once, from one consistent set of details.
            static string Norm(string? v) => (v ?? string.Empty).Trim().ToLowerInvariant();
            var newRowsText = FormatRowsText(rows.Select(r => r.RowNumber));

            if (rows.Select(r => Norm(r.StockUomName)).Distinct().Count() > 1 ||
                rows.Select(r => Norm(r.Principal)).Distinct().Count() > 1)
            {
                foreach (var r in rows)
                {
                    r.Issues.Add(
                        $"CompanyItem Code '{rows[0].CompanyItemCode}' would be created as a new item, but its rows have different " +
                        $"Stock UOMs or principals ({newRowsText}). A new item needs one consistent set of details.");
                }
                Finish();
                return;
            }

            var firstNew = rows[0];
            if (rows.Any(r => Norm(r.CompanyItemName) != Norm(firstNew.CompanyItemName) || Norm(r.PPG) != Norm(firstNew.PPG)))
            {
                foreach (var r in rows)
                {
                    AddWarningOnce(r,
                        $"Rows for '{firstNew.CompanyItemCode}' have different names or PPGs ({newRowsText}). " +
                        $"The new item will be created with the details from row {firstNew.RowNumber}.");
                }
            }

            var todayNew = DateTime.Today;
            if (!rows.Any(r => r.PriceChangeEffectivityDate!.Value.Date <= todayNew))
            {
                foreach (var r in rows)
                {
                    r.Issues.Add(
                        "A new item needs at least one price effective today or earlier, so it has a current price. " +
                        "Every date for this item is in the future.");
                }
                Finish();
                return;
            }
        }

        // 2) Every price this file wants to record, per price list.
        var entries = new List<HistoricalEntry>();
        foreach (var r in rows)
        {
            var date = r.PriceChangeEffectivityDate!.Value;
            if (r.StockPrice is decimal s) entries.Add(new HistoricalEntry(null, "Stock Price", date, s, r));
            if (r.StockPriceRegional is decimal rg) entries.Add(new HistoricalEntry(PriceListTypes.Regional, "Regional Price", date, rg, r));
            if (r.StockPriceNational is decimal n) entries.Add(new HistoricalEntry(PriceListTypes.National, "National Price", date, n, r));
        }

        // 3) The same price list + date more than once in the file.
        foreach (var dup in entries.GroupBy(e => (e.Type, e.Date)).Where(g => g.Count() > 1))
        {
            var first = dup.First();
            var rowText = FormatRowsText(dup.Select(e => e.Row.RowNumber));
            var dupRows = dup.Select(e => e.Row).Distinct().ToList();

            if (dup.Select(e => e.Price).Distinct().Count() > 1)
            {
                foreach (var r in dupRows)
                    r.Issues.Add($"{first.Label} has conflicting prices for {first.Date:yyyy-MM-dd} ({rowText}).");
            }
            else
            {
                foreach (var r in dupRows)
                    AddWarningOnce(r, $"{first.Label} for {first.Date:yyyy-MM-dd} is repeated with the same price ({rowText}). It will be imported once.");
            }
        }

        if (rows.Any(r => r.Issues.Count > 0)) { Finish(); return; }

        var distinct = entries.GroupBy(e => (e.Type, e.Date)).Select(g => g.First()).ToList();

        // 4) Compare with what is already saved for this item.
        IReadOnlyList<ExistingPriceHistoryRow> history = isNew
            ? Array.Empty<ExistingPriceHistoryRow>()
            : await _companyItemService.GetPriceHistoryRowsAsync(itemId, ct);
        var today = DateTime.Today;
        var importable = new List<HistoricalEntry>();

        foreach (var typeGroup in distinct.GroupBy(e => e.Type))
        {
            var label = typeGroup.First().Label;
            var existing = history.Where(h => SameListType(h.PriceListType, typeGroup.Key)).ToList();

            if (existing.Any(h => h.AppliedDate == null))
            {
                foreach (var r in typeGroup.Select(e => e.Row).Distinct())
                {
                    r.Issues.Add(
                        $"{label}: this item already has a scheduled price change that has not taken effect yet. " +
                        "Resolve it before importing price history.");
                }
                continue;
            }

            foreach (var e in typeGroup)
            {
                var clash = existing.FirstOrDefault(h => h.EffectivityDate.Date == e.Date);

                if (clash is null)
                {
                    importable.Add(e);
                }
                else if (clash.NewPrice == e.Price)
                {
                    AddWarningOnce(e.Row, $"{label} {e.Price:N2} on {e.Date:yyyy-MM-dd} is already in the price history and will be skipped.");
                }
                else
                {
                    e.Row.Issues.Add(
                        $"{label} on {e.Date:yyyy-MM-dd}: the price history already records {clash.NewPrice:N2}, " +
                        $"but the file has {e.Price:N2}.");
                }
            }
        }

        if (rows.Any(r => r.Issues.Count > 0)) { Finish(); return; }

        if (importable.Count == 0)
        {
            foreach (var r in rows)
                r.Issues.Add("Every price in the file is already in this item's price history. Nothing to import.");
            Finish();
            return;
        }

        // 5) Things the user should see before committing.
        decimal? CurrentPrice(string? type) =>
            string.IsNullOrEmpty(type) ? rows[0].ExistingStockPrice
            : SameListType(type, PriceListTypes.Regional) ? rows[0].ExistingStockPriceRegional
            : rows[0].ExistingStockPriceNational;

        foreach (var typeGroup in importable.GroupBy(e => e.Type))
        {
            var existing = history.Where(h => SameListType(h.PriceListType, typeGroup.Key)).ToList();
            DateTime? newestExistingApplied = existing
                .Where(h => h.AppliedDate != null)
                .Select(h => (DateTime?)h.EffectivityDate.Date)
                .DefaultIfEmpty(null)
                .Max();

            // The live price changes only when a file row is newer than everything already applied.
            var latestNew = typeGroup.Where(e => e.Date <= today).OrderByDescending(e => e.Date).FirstOrDefault();
            if (latestNew is not null && (newestExistingApplied is null || latestNew.Date > newestExistingApplied))
            {
                var current = CurrentPrice(typeGroup.Key);
                if (!isNew && current != latestNew.Price)
                {
                    AddWarningOnce(latestNew.Row,
                        $"{latestNew.Label} will change from {(current.HasValue ? current.Value.ToString("N2") : "(none)")} " +
                        $"to {latestNew.Price:N2} (effective {latestNew.Date:yyyy-MM-dd}).");
                }
            }

            // Future dates are scheduled, not applied.
            foreach (var future in typeGroup.Where(e => e.Date > today))
            {
                AddWarningOnce(future.Row,
                    $"{future.Label} {future.Price:N2} effective {future.Date:yyyy-MM-dd} is in the future. " +
                    "It will be scheduled and will not take effect until that date.");
            }
        }

        Finish();
    }

    // PHASE 2 (historical) — commit the selected items. One transaction per item.
    // An item that does not exist yet is created together with its prices in that same transaction.
    public async Task<int> CommitHistoricalRowsAsync(IEnumerable<CompanyItemImportRowResult> rows, int userId, CancellationToken ct = default)
    {
        if (userId <= 0 || rows is null)
            return 0;

        var validRows = rows
            .Where(r => r.IsSuccess && r.Issues.Count == 0)
            .ToList();

        var committed = 0;

        // Existing items group by id; items that don't exist yet group by code.
        foreach (var itemRows in validRows.GroupBy(
                     r => r.ExistingCompanyItemId?.ToString() ?? "new:" + r.CompanyItemCode.Trim().ToUpperInvariant(),
                     StringComparer.OrdinalIgnoreCase))
        {
            var list = itemRows.ToList();

            var entries = new List<HistoricalPriceRow>();
            foreach (var r in list)
            {
                if (r.PriceChangeEffectivityDate is not DateTime date) continue;

                if (r.StockPrice is decimal s) entries.Add(new HistoricalPriceRow(null, date, s));
                if (r.StockPriceRegional is decimal rg) entries.Add(new HistoricalPriceRow(PriceListTypes.Regional, date, rg));
                if (r.StockPriceNational is decimal n) entries.Add(new HistoricalPriceRow(PriceListTypes.National, date, n));
            }

            entries = entries.Distinct().OrderBy(e => e.EffectivityDate).ToList();

            try
            {
                var first = list
                    .OrderBy(r => r.PriceChangeEffectivityDate ?? DateTime.MaxValue)
                    .ThenBy(r => r.RowNumber)
                    .First();

                int? itemId = first.ExistingCompanyItemId;
                HistoricalPriceApplyResult applied;

                if (itemId.HasValue)
                {
                    applied = await _companyItemService.ApplyHistoricalPricesAsync(itemId.Value, entries, userId, ct);
                }
                else
                {
                    var created = await _companyItemService.CreateItemWithHistoricalPricesAsync(new CompanyItemCreateDto
                    {
                        ItemCode = first.CompanyItemCode,
                        ItemName = first.CompanyItemName,
                        PPG = first.PPG,
                        Category = first.Category,
                        Principal = first.Principal,
                        StockUomName = first.StockUomName?.Trim() ?? string.Empty,
                        IsActive = true,
                        CreatedBy = userId
                    }, entries, userId, ct);

                    applied = created.Result;
                    itemId = created.CompanyItemId;
                }

                if (!applied.Success || itemId is null)
                {
                    foreach (var r in list)
                    {
                        r.Issues.Add($"Save failed: {applied.Error ?? "the item was not saved."}");
                        r.IsSuccess = false;
                    }
                    continue;
                }

                foreach (var r in list)
                    r.CompanyItemId = itemId;

                committed++;
            }
            catch (Exception ex)
            {
                foreach (var r in list)
                {
                    r.Issues.Add($"Save failed: {ex.GetBaseException().Message}");
                    r.IsSuccess = false;
                }
            }
        }

        return committed;
    }

    private static string FormatRowsText(IEnumerable<int> rowNumbers)
    {
        var rows = rowNumbers.Distinct().OrderBy(r => r).ToList();
        if (rows.Count == 1) return $"row {rows[0]}";
        if (rows.Count <= 5) return $"rows {string.Join(", ", rows.Take(rows.Count - 1))} & {rows[^1]}";
        return $"rows {string.Join(", ", rows.Take(5))} and {rows.Count - 5} more";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Shared: header detection, error report, validation helpers
    // ═════════════════════════════════════════════════════════════════════════

    private sealed record HeaderDetectionResult(
        int HeaderRowNumber,
        Dictionary<string, int> ColumnIndex,
        int BestCandidateRowNumber,
        string[] BestCandidateHeaders,
        List<string> BestCandidateMissing,
        List<(int Column, string Header)> AllHeaderColumns,
        List<string> BestCandidateUnmatchedHeaders);

    private static void AddHeaderDetectionError(
        CompanyItemImportResult result, HeaderDetectionResult detection, IReadOnlyList<string> requiredKeys)
    {
        if (detection.BestCandidateRowNumber > 0)
        {
            var missingList = string.Join(", ", detection.BestCandidateMissing);

            var unmatchedColumns = detection.BestCandidateUnmatchedHeaders.Count > 0
                ? string.Join(", ", detection.BestCandidateUnmatchedHeaders.Select(h => $"\"{h}\""))
                : "none";

            var renameHints = detection.BestCandidateMissing.Count == 1
                ? $"\nRename if found: {GetAcceptedAliasesText(detection.BestCandidateMissing[0])}"
                : "\nRename if found:\n" + string.Join("\n",
                    detection.BestCandidateMissing.Select(m => $"{m}: {GetAcceptedAliasesText(m)}"));

            var message =
                $"Header found at row {detection.BestCandidateRowNumber} " +
                $"Missing: {missingList} " +
                $"Columns found: {unmatchedColumns} " +
                renameHints;

            result.AddError(detection.BestCandidateRowNumber, string.Empty, message);
        }
        else
        {
            result.AddError(
                0,
                string.Empty,
                $"Could not find a header row within the first {MaxHeaderScanRows} rows " +
                "containing the required columns: " +
                string.Join(", ", requiredKeys));
        }
    }

    private static HeaderDetectionResult DetectHeaderRow(IXLWorksheet worksheet, IReadOnlyList<string> requiredKeys)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var scanLimit = Math.Min(MaxHeaderScanRows, lastRow);

        int bestMissingCount = int.MaxValue;
        int bestCandidateRowNumber = -1;
        string[] bestCandidateHeaders = Array.Empty<string>();
        List<string> bestCandidateMissing = new();
        List<string> bestCandidateUnmatchedHeaders = new();

        for (int rowNumber = 1; rowNumber <= scanLimit; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var usedCells = row.CellsUsed().ToList();
            if (usedCells.Count == 0) continue;

            var candidateHeaders = usedCells.Select(c => c.GetString().Trim()).ToArray();
            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var foundCanonicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchedHeaderTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var allHeaderColumns = usedCells
                .Select(c => (Column: c.Address.ColumnNumber, Header: c.GetString().Trim()))
                .Where(h => !string.IsNullOrWhiteSpace(h.Header))
                .ToList();

            foreach (var cell in usedCells)
            {
                var normalized = NormalizeHeader(cell.GetString());
                if (string.IsNullOrWhiteSpace(normalized)) continue;

                if (AliasLookup.TryGetValue(normalized, out var canonicalKey))
                {
                    matchedHeaderTexts.Add(cell.GetString().Trim());
                    if (foundCanonicalKeys.Add(canonicalKey))
                    {
                        columnIndex[canonicalKey.Replace(" ", "")] = cell.Address.ColumnNumber;
                    }
                }
            }

            var missing = requiredKeys.Where(key => !foundCanonicalKeys.Contains(key)).ToList();

            if (missing.Count == 0)
            {
                return new HeaderDetectionResult(
                    rowNumber, columnIndex, -1, Array.Empty<string>(), new List<string>(),
                    allHeaderColumns, new List<string>());
            }

            if (missing.Count < bestMissingCount)
            {
                bestMissingCount = missing.Count;
                bestCandidateRowNumber = rowNumber;
                bestCandidateHeaders = candidateHeaders;
                bestCandidateMissing = missing;
                bestCandidateUnmatchedHeaders = candidateHeaders
                    .Where(h => !string.IsNullOrWhiteSpace(h) && !matchedHeaderTexts.Contains(h))
                    .ToList();
            }
        }

        return new HeaderDetectionResult(
            -1, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            bestCandidateRowNumber, bestCandidateHeaders, bestCandidateMissing,
            new List<(int, string)>(), bestCandidateUnmatchedHeaders);
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

    // PHASE 3 — build a downloadable Excel report: original columns + an "Error" column, failed rows only.
    public byte[] GenerateErrorReportExcel(CompanyItemImportResult result)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Errors");

        var headers = result.OriginalHeaders.Count > 0
            ? result.OriginalHeaders
            : new List<string>
            {
                "CompanyItem Code", "CompanyItem Name", "Principal", "PPG",
                "Price", "Regional Price", "National Price", "Effectivity Date"
            };

        for (int i = 0; i < headers.Count; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        var errorColumn = headers.Count + 1;
        sheet.Cell(1, errorColumn).Value = "Error";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEA");

        var failedRows = result.Rows.Where(r => !r.IsSuccess).OrderBy(r => r.RowNumber).ToList();

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

    private static string GetAcceptedAliasesText(string canonicalKey) =>
        RequiredHeaderMap.TryGetValue(canonicalKey, out var required) ? string.Join(", ", required)
        : OptionalHeaderMap.TryGetValue(canonicalKey, out var optional) ? string.Join(", ", optional)
        : "no known aliases";

    // Price history: KCPI reads both Sheet 1 Price (Regional) and List Price (National);
    // every other principal reads List Price (or Price) only.
    private static void ValidateHistoryRowPrices(CompanyItemImportRowResult row, bool isKcpi)
    {
        if (isKcpi)
        {
            if (!row.StockPriceRegional.HasValue || !row.StockPriceNational.HasValue)
                row.Issues.Add("KCPI price history needs both Sheet 1 Price (Regional) and List Price (National) on every row.");
        }
        else if (!row.StockPrice.HasValue)
        {
            row.Issues.Add("A List Price (or Price) is required on every row.");
        }
    }

    private static void ValidatePriceConfiguration(CompanyItemImportRowResult rowResult)
    {
        var isKcpi = string.Equals(
            rowResult.Principal?.Trim(),
            "KCPI",
            StringComparison.OrdinalIgnoreCase);

        var hasStockPrice = rowResult.StockPrice.HasValue;
        var hasRegionalPrice = rowResult.StockPriceRegional.HasValue;
        var hasNationalPrice = rowResult.StockPriceNational.HasValue;

        if (isKcpi)
        {
            if (!hasStockPrice && hasRegionalPrice && hasNationalPrice)
                return;

            rowResult.Issues.Add(
                "KCPI requires both Regional Price (Sheet 1 Price) and " +
                "National Price (List Price). Do not provide a separate Stock Price.");
            return;
        }

        if (hasStockPrice && !hasRegionalPrice && !hasNationalPrice)
            return;

        rowResult.Issues.Add(
            "This principal requires one Stock Price. Provide List Price " +
            "(or Price) only.");
    }

    private static void ValidateExistingItemPrices(CompanyItemImportRowResult rowResult)
    {
        var isKcpi = string.Equals(
            rowResult.Principal?.Trim(),
            "KCPI",
            StringComparison.OrdinalIgnoreCase);

        var hasStockPrice = rowResult.StockPrice.HasValue;
        var hasRegionalPrice = rowResult.StockPriceRegional.HasValue;
        var hasNationalPrice = rowResult.StockPriceNational.HasValue;

        var existingStock = rowResult.ExistingStockPrice.HasValue;
        var existingRegional = rowResult.ExistingStockPriceRegional.HasValue;
        var existingNational = rowResult.ExistingStockPriceNational.HasValue;

        if (isKcpi)
        {
            if (existingStock || !existingRegional || !existingNational)
            {
                rowResult.Issues.Add(
                    "This KCPI item does not have the expected dual-price configuration. " +
                    "Correct its existing prices before importing.");
                return;
            }

            if (hasStockPrice)
            {
                rowResult.Issues.Add(
                    "KCPI uses Regional Price and National Price, not Stock Price.");
            }

            return;
        }

        if (!existingStock || existingRegional || existingNational)
        {
            rowResult.Issues.Add(
                "This item does not have the expected single-price configuration " +
                "for its principal. Correct its existing prices before importing.");
            return;
        }

        if (hasRegionalPrice || hasNationalPrice)
        {
            rowResult.Issues.Add(
                "This principal supports Stock Price only. Provide List Price " +
                "(or Price), not Regional Price or National Price.");
        }
    }
}