using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using STTproject.Features.Admin.CompanyItem.DTOs;
using STTproject.Features.Admin.Subdistributor.DTOs;

namespace STTproject.Features.Admin.CompanyItem.Services;

// Unified import: one file, no mode selection. The rows and their effectivity dates decide
// what happens — create items, apply the current price, record history, schedule future
// prices, and backfill a missing Stock UOM.
public sealed class ImportCompanyItemsService
{
    private const int MaxHeaderScanRows = 10;
    private const string Kcpi = "KCPI";

    private readonly IAdminCompanyItemService _companyItemService;

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
            ["RegionalPrice"] = new[] { "Regional Price", "Regional Stock Price", "Stock Price Regional", "Sheet 1 Price" },
            ["NationalPrice"] = new[] { "National Price", "National Stock Price", "Stock Price National", "List Price" },
            ["EffectivityDate"] = new[] { "Effectivity Date", "Effective Date", "Effectivity" }
        };

    private static readonly IReadOnlyDictionary<string, string> AliasLookup = BuildAliasLookup();

    public ImportCompanyItemsService(IAdminCompanyItemService companyItemService)
    {
        _companyItemService = companyItemService;
    }

    private static IReadOnlyDictionary<string, string> BuildAliasLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in RequiredHeaderMap.Concat(OptionalHeaderMap))
            foreach (var alias in kvp.Value)
                lookup.TryAdd(NormalizeHeader(alias), kvp.Key);
        return lookup;
    }

    private sealed record RawPrices(string? Price, string? Regional, string? National);
    private sealed record ParsedRow(CompanyItemImportRowResult Row, RawPrices Raw, string? FilePrincipal);
    private sealed record PriceEntry(string? Type, string Label, DateTime Date, decimal Price,
        CompanyItemImportRowResult Row, bool DateWasBlank);

    // ═════════════════════════════════════════════════════════════════════════
    //  STEPS 1–5 — read, normalize, match, plan, validate. Nothing is saved here.
    // ═════════════════════════════════════════════════════════════════════════
    public async Task<CompanyItemImportResult> PrepareFromExcelAsync(
        Stream excelStream, string? principal, CancellationToken ct = default)
    {
        var result = new CompanyItemImportResult { Principal = principal };
        var principalPreSelected = !string.IsNullOrWhiteSpace(principal);

        if (excelStream is null || !excelStream.CanRead)
        {
            result.AddError(0, string.Empty, "Import file is missing or unreadable.");
            return result;
        }

        if (excelStream.CanSeek) excelStream.Position = 0;

        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            result.AddError(0, string.Empty, "The workbook does not contain any worksheets.");
            return result;
        }

        // Only the code is needed to find the header. Name, Stock UOM and Principal are required
        // per item below, and only when the item has to be created.
        var requiredKeys = new List<string> { "CompanyItem Code" };
        var detection = DetectHeaderRow(worksheet, requiredKeys);
        if (detection.HeaderRowNumber == -1)
        {
            AddHeaderDetectionError(result, detection, requiredKeys);
            return result;
        }

        var columnIndex = detection.ColumnIndex;
        if (!columnIndex.ContainsKey("Price") && !columnIndex.ContainsKey("RegionalPrice") && !columnIndex.ContainsKey("NationalPrice"))
        {
            result.AddError(detection.HeaderRowNumber, string.Empty,
                "A price column is required: Price (or List Price), or Regional Price and National Price for KCPI.");
            return result;
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? detection.HeaderRowNumber;
        result.OriginalHeaders = detection.AllHeaderColumns.Select(h => h.Header).ToList();

        // ── Step 2: read and normalize ──────────────────────────────────────
        var parsed = new List<ParsedRow>();

        for (int rowNumber = detection.HeaderRowNumber + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.CellsUsed().All(c => c.IsEmpty())) continue;

            string? Get(string column) =>
                columnIndex.TryGetValue(column, out var colNum) ? GetString(row, colNum) : null;

            var rowResult = new CompanyItemImportRowResult
            {
                RowNumber = rowNumber,
                CompanyItemCode = Get("CompanyItemCode") ?? string.Empty,
                CompanyItemName = Get("CompanyItemName") ?? string.Empty,
                PPG = Get("PPG"),
                Category = Get("Category"),
                StockUomName = Get("StockUOM")?.Trim() ?? string.Empty
            };

            foreach (var (col, header) in detection.AllHeaderColumns)
                rowResult.RawValues[header] = GetString(row, col);

            if (string.IsNullOrWhiteSpace(rowResult.CompanyItemCode) &&
                string.IsNullOrWhiteSpace(rowResult.CompanyItemName))
                continue;

            // Effectivity date is optional: a blank date means "effective today".
            if (columnIndex.TryGetValue("EffectivityDate", out var effCol) && !row.Cell(effCol).IsEmpty())
            {
                if (TryReadEffectivityDate(row.Cell(effCol), out var effDate))
                    rowResult.PriceChangeEffectivityDate = effDate;
                else
                    rowResult.Issues.Add($"Effectivity Date '{GetString(row, effCol)}' is not a valid date. Use MM/dd/yyyy or yyyy-MM-dd.");
            }

            if (string.IsNullOrWhiteSpace(rowResult.CompanyItemCode))
                rowResult.Issues.Add("CompanyItem Code is required.");

            var filePrincipal = principalPreSelected ? principal : Get("Principal");
            parsed.Add(new ParsedRow(rowResult, new RawPrices(Get("Price"), Get("RegionalPrice"), Get("NationalPrice")), filePrincipal));
        }

        // ── Steps 3–5: match, plan, validate — one group per item ───────────
        var groupKeyed = parsed.GroupBy(p =>
            string.IsNullOrWhiteSpace(p.Row.CompanyItemCode)
                ? "row:" + p.Row.RowNumber
                : p.Row.CompanyItemCode.Trim().ToUpperInvariant());

        foreach (var itemGroup in groupKeyed)
        {
            var items = itemGroup
                .OrderBy(p => p.Row.PriceChangeEffectivityDate ?? DateTime.MinValue)
                .ThenBy(p => p.Row.RowNumber)
                .ToList();

            var group = await PlanGroupAsync(items, principal, ct);

            foreach (var r in group.Rows)
            {
                result.Rows.Add(r);
                foreach (var issue in r.Issues)
                    group.Issues.Add(new CompanyItemImportIssue(r.RowNumber, r.CompanyItemCode, issue));
            }

            result.PreparedGroups.Add(group);
        }

        result.Rows.Sort((a, b) => a.RowNumber.CompareTo(b.RowNumber));

        if (result.Rows.Count == 0 && !result.HasIssues)
            result.AddError(0, string.Empty, "No company item rows were found in the file.");

        return result;
    }

    private async Task<PreparedCompanyItemGroup> PlanGroupAsync(
        List<ParsedRow> items, string? principal, CancellationToken ct)
    {
        var rows = items.Select(i => i.Row).ToList();
        var group = new PreparedCompanyItemGroup(rows) { Selected = false };
        var code = rows[0].CompanyItemCode.Trim();
        group.ItemCode = code;

        void Finish()
        {
            foreach (var r in rows) r.IsSuccess = r.Issues.Count == 0;
        }

        // ── Step 3: match the existing item ─────────────────────────────────
        CompanyItemListDto? item = code.Length > 0
            ? await _companyItemService.GetByItemCodeAsync(code, ct)
            : null;

        group.IsNewItem = item is null;
        group.ExistingCompanyItemId = item?.CompanyItemId;

        var itemPrincipal = item?.Principal;
        var isKcpi = string.Equals((item is null ? items[0].FilePrincipal : itemPrincipal)?.Trim(), Kcpi,
            StringComparison.OrdinalIgnoreCase);

        // ── Row-level checks and price interpretation ───────────────────────
        foreach (var p in items)
        {
            var r = p.Row;

            // Sheet 1 Price only means something for KCPI; otherwise it is ignored entirely.
            var price = ReadPrice(p.Raw.Price, "Price", r);
            var regional = isKcpi ? ReadPrice(p.Raw.Regional, "Sheet 1 Price", r) : null;
            var national = ReadPrice(p.Raw.National, "List Price", r);

            if (item is not null)
            {
                r.IsExistingItem = true;
                r.ExistingCompanyItemId = item.CompanyItemId;
                r.ExistingStockPrice = item.StockPrice;
                r.ExistingStockPriceRegional = item.StockPriceRegional;
                r.ExistingStockPriceNational = item.StockPriceNational;
                r.ExistingStockUomName = item.StockUomName;
                r.ExistingIsActive = item.IsActive;
                r.Principal = item.Principal;

                if (string.IsNullOrWhiteSpace(r.CompanyItemName)) r.CompanyItemName = item.ItemName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(r.PPG)) r.PPG = item.PPG;

                // An import never silently changes an item's principal.
                if (!string.IsNullOrWhiteSpace(p.FilePrincipal) &&
                    !string.Equals(p.FilePrincipal.Trim(), item.Principal?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    r.Issues.Add(
                        $"CompanyItem Code '{code}' already belongs to principal '{item.Principal}', but the " +
                        $"imported principal is '{p.FilePrincipal}'. These principals must match.");
                }
            }
            else
            {
                r.Principal = p.FilePrincipal;
            }

            if (isKcpi)
            {
                r.StockPrice = price;
                r.StockPriceRegional = regional;
                r.StockPriceNational = national;

                if (price.HasValue)
                    r.Issues.Add("KCPI uses Sheet 1 Price for Regional Price and List Price for National Price. Do not provide a separate Price/Stock Price.");
            }
            else
            {
                r.StockPrice = price ?? national;
                r.StockPriceRegional = null;
                r.StockPriceNational = null;

                if (price.HasValue && national.HasValue)
                    r.Issues.Add("Provide either Price or List Price, not both, for a non-KCPI principal.");
            }

            var hasAnyPrice = r.StockPrice.HasValue || r.StockPriceRegional.HasValue || r.StockPriceNational.HasValue;
            var uomCanBeFilled = item is not null
                && string.IsNullOrWhiteSpace(item.StockUomName)
                && !string.IsNullOrWhiteSpace(r.StockUomName);

            if (!hasAnyPrice && !uomCanBeFilled && r.Issues.Count == 0)
                r.Issues.Add("A price is required on every row.");

            if (isKcpi && item is null && hasAnyPrice && (!r.StockPriceRegional.HasValue || !r.StockPriceNational.HasValue))
                r.Issues.Add("A new KCPI item needs both Sheet 1 Price (Regional) and List Price (National).");

            // A new item can only be created from complete details.
            if (item is null && code.Length > 0)
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(r.CompanyItemName)) missing.Add("an item name");
                if (string.IsNullOrWhiteSpace(r.StockUomName)) missing.Add("a Stock UOM");
                if (string.IsNullOrWhiteSpace(r.Principal)) missing.Add("a principal");

                if (missing.Count > 0)
                    r.Issues.Add($"CompanyItem Code '{code}' was not found, and it can't be created because the row has no {string.Join(", ", missing)}.");
            }
        }

        // An item is imported all-or-nothing: one bad row holds the whole item back.
        if (rows.Any(r => r.Issues.Count > 0))
        {
            var failing = rows.Where(r => r.Issues.Count > 0).Select(r => r.RowNumber).ToList();
            foreach (var r in rows.Where(r => r.Issues.Count == 0))
            {
                r.Issues.Add(
                    $"On hold: {FormatRowsText(failing)} for this item {(failing.Count == 1 ? "has an error" : "have errors")}. " +
                    "An item is only imported when all of its rows are valid.");
            }
            Finish();
            return group;
        }

        // ── New item: one consistent set of details ─────────────────────────
        static string Norm(string? v) => (v ?? string.Empty).Trim().ToLowerInvariant();
        var today = DateTime.Today;

        if (group.IsNewItem)
        {
            var first = rows[0];
            var rowsText = FormatRowsText(rows.Select(r => r.RowNumber));

            if (rows.Select(r => Norm(r.StockUomName)).Distinct().Count() > 1 ||
                rows.Select(r => Norm(r.Principal)).Distinct().Count() > 1)
            {
                foreach (var r in rows)
                    r.Issues.Add($"CompanyItem Code '{code}' would be created as a new item, but its rows have different Stock UOMs or principals ({rowsText}). A new item needs one consistent set of details.");
                Finish();
                return group;
            }

            if (rows.Any(r => Norm(r.CompanyItemName) != Norm(first.CompanyItemName) || Norm(r.PPG) != Norm(first.PPG)))
            {
                foreach (var r in rows)
                    AddWarningOnce(r, $"Rows for '{code}' have different names or PPGs ({rowsText}). The new item will be created with the details from row {first.RowNumber}.");
            }

            // Rows are date-ordered, so the first row in the plan carries the item details.
            if (!rows.Any(r => (r.PriceChangeEffectivityDate ?? today).Date <= today))
            {
                foreach (var r in rows)
                    r.Issues.Add("A new item needs at least one price effective today or earlier, so it has a current price. Every date for this item is in the future.");
                Finish();
                return group;
            }

            group.Operations.Add(new CompanyItemPlannedOperation(
                CompanyItemOperationKind.CreateItem,
                $"Create company item '{code}' ({first.CompanyItemName}, {first.Principal}, {first.StockUomName})",
                first.RowNumber));
        }

        // ── Every price the file wants to record, per price list ────────────
        var entries = new List<PriceEntry>();
        foreach (var r in rows)
        {
            var blank = !r.PriceChangeEffectivityDate.HasValue;
            var date = (r.PriceChangeEffectivityDate ?? today).Date;
            if (r.StockPrice is decimal s) entries.Add(new PriceEntry(null, "Stock Price", date, s, r, blank));
            if (r.StockPriceRegional is decimal rg) entries.Add(new PriceEntry(PriceListTypes.Regional, "Regional Price", date, rg, r, blank));
            if (r.StockPriceNational is decimal n) entries.Add(new PriceEntry(PriceListTypes.National, "National Price", date, n, r, blank));
        }

        // Same price list + date more than once in the file.
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
                group.Operations.Add(new CompanyItemPlannedOperation(CompanyItemOperationKind.SkipDuplicate,
                    $"{first.Label} {first.Price:N2} on {first.Date:yyyy-MM-dd} is repeated in the file ({rowText}); imported once."));
            }
        }

        if (rows.Any(r => r.Issues.Count > 0)) { Finish(); return group; }

        var distinct = entries.GroupBy(e => (e.Type, e.Date)).Select(g => g.First()).ToList();

        // ── Compare with what is already saved ──────────────────────────────
        IReadOnlyList<ExistingPriceHistoryRow> history = group.IsNewItem
            ? Array.Empty<ExistingPriceHistoryRow>()
            : await _companyItemService.GetPriceHistoryRowsAsync(item!.CompanyItemId, ct);

        decimal? CurrentPrice(string? type) =>
            item is null ? null
            : string.IsNullOrEmpty(type) ? item.StockPrice
            : SameListType(type, PriceListTypes.Regional) ? item.StockPriceRegional
            : item.StockPriceNational;

        var importable = new List<PriceEntry>();

        foreach (var typeGroup in distinct.GroupBy(e => e.Type))
        {
            var label = typeGroup.First().Label;
            var existing = history.Where(h => SameListType(h.PriceListType, typeGroup.Key)).ToList();

            if (existing.Any(h => h.AppliedDate == null))
            {
                foreach (var r in typeGroup.Select(e => e.Row).Distinct())
                    r.Issues.Add($"{label}: this item already has a scheduled price change that has not taken effect yet. Resolve it before importing prices.");
                continue;
            }

            foreach (var e in typeGroup)
            {
                var clash = existing.FirstOrDefault(h => h.EffectivityDate.Date == e.Date);
                var current = CurrentPrice(typeGroup.Key);

                if (clash is not null && clash.NewPrice == e.Price)
                {
                    group.Operations.Add(new CompanyItemPlannedOperation(CompanyItemOperationKind.SkipDuplicate,
                        $"{label} {e.Price:N2} on {e.Date:yyyy-MM-dd} already exists.", e.Row.RowNumber));
                }
                else if (clash is not null)
                {
                    e.Row.Issues.Add($"{label} on {e.Date:yyyy-MM-dd}: the price history already records {clash.NewPrice:N2}, but the file has {e.Price:N2}.");
                }
                else if (e.DateWasBlank && !group.IsNewItem && current == e.Price)
                {
                    // No date and no change: nothing to record.
                    group.Operations.Add(new CompanyItemPlannedOperation(CompanyItemOperationKind.SkipDuplicate,
                        $"{label} {e.Price:N2} is already the current price.", e.Row.RowNumber));
                }
                else
                {
                    importable.Add(e);
                }
            }
        }

        if (rows.Any(r => r.Issues.Count > 0)) { Finish(); return group; }

        // ── Step 4: classify what each importable entry will do ─────────────
        foreach (var typeGroup in importable.GroupBy(e => e.Type))
        {
            var existing = history.Where(h => SameListType(h.PriceListType, typeGroup.Key)).ToList();
            DateTime? newestApplied = existing
                .Where(h => h.AppliedDate != null)
                .Select(h => (DateTime?)h.EffectivityDate.Date)
                .DefaultIfEmpty(null)
                .Max();

            // The live price changes only for the newest non-future entry, and only if it is
            // newer than everything already applied.
            var latestEffective = typeGroup.Where(e => e.Date <= today).OrderByDescending(e => e.Date).FirstOrDefault();
            var current = CurrentPrice(typeGroup.Key);

            foreach (var e in typeGroup.OrderBy(x => x.Date))
            {
                group.Entries.Add(new HistoricalPriceRow(e.Type, e.Date, e.Price));

                if (e.Date > today)
                {
                    group.Operations.Add(new CompanyItemPlannedOperation(CompanyItemOperationKind.SchedulePrice,
                        $"{e.Label} {e.Price:N2} will be scheduled for {e.Date:yyyy-MM-dd}.", e.Row.RowNumber));
                }
                else if (ReferenceEquals(e, latestEffective) && (newestApplied is null || e.Date > newestApplied))
                {
                    var from = current.HasValue ? current.Value.ToString("N2") : "(none)";
                    group.Operations.Add(new CompanyItemPlannedOperation(CompanyItemOperationKind.ApplyCurrentPrice,
                        group.IsNewItem
                            ? $"{e.Label} {e.Price:N2} effective {e.Date:yyyy-MM-dd} (initial price)."
                            : $"{e.Label} changes from {from} to {e.Price:N2} (effective {e.Date:yyyy-MM-dd}).",
                        e.Row.RowNumber));
                }
                else
                {
                    group.Operations.Add(new CompanyItemPlannedOperation(CompanyItemOperationKind.AddPriceHistory,
                        $"{e.Label} {e.Price:N2} effective {e.Date:yyyy-MM-dd} will be added to the price history.", e.Row.RowNumber));
                }
            }
        }

        // Stock UOM: only a blank one is filled in. An existing UOM is never overwritten, because
        // sub-distributor UOMs may already be mapped against it. This works even when every price
        // in the file already exists.
        if (item is not null)
        {
            var fileUom = rows.Select(r => r.StockUomName?.Trim() ?? string.Empty).FirstOrDefault(u => u.Length > 0) ?? string.Empty;
            var existingUom = item.StockUomName?.Trim() ?? string.Empty;

            if (existingUom.Length == 0 && fileUom.Length > 0)
            {
                group.BackfillStockUomName = fileUom;
                foreach (var r in rows) r.BackfillStockUom = true;
                group.Operations.Add(new CompanyItemPlannedOperation(CompanyItemOperationKind.BackfillStockUom,
                    $"Set the missing Stock UOM to '{fileUom}'."));
            }
            else if (existingUom.Length > 0 && fileUom.Length > 0 &&
                     !string.Equals(existingUom, fileUom, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var r in rows)
                    AddWarningOnce(r, $"The file's Stock UOM '{fileUom}' differs from the item's '{existingUom}' and will not be changed.");
            }
        }

        if (!group.HasWork)
        {
            foreach (var r in rows)
                r.Issues.Add("Nothing new to import: everything in the file for this item already exists.");
        }

        Finish();
        return group;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  STEP 6 — commit the selected groups. One transaction per item (inside the
    //  service calls); a failure in one item never blocks the others.
    // ═════════════════════════════════════════════════════════════════════════
    public async Task<CompanyItemCommitSummary> CommitPreparedGroupsAsync(
        IEnumerable<PreparedCompanyItemGroup> groups, int userId, CancellationToken ct = default)
    {
        var summary = new CompanyItemCommitSummary();
        if (userId <= 0 || groups is null) return summary;

        var today = DateTime.Today;

        foreach (var group in groups.Where(g => g.Selected && !g.IsSaved))
        {
            var code = group.ItemCode;

            if (group.HasIssues || group.Rows.Any(r => !r.IsSuccess))
            {
                summary.Outcomes.Add(new(code, CompanyItemCommitStatus.Skipped, "Skipped: the item has validation errors."));
                continue;
            }

            if (!group.HasWork)
            {
                summary.PricesSkipped += group.Operations.Count(o => o.Kind == CompanyItemOperationKind.SkipDuplicate);
                summary.Outcomes.Add(new(code, CompanyItemCommitStatus.Skipped, "Skipped: nothing new to import."));
                continue;
            }

            try
            {
                var entries = group.Entries.Distinct().OrderBy(e => e.EffectivityDate).ToList();
                int? itemId = group.ExistingCompanyItemId;
                string? error = null;

                if (group.IsNewItem)
                {
                    var first = group.Rows.OrderBy(r => r.PriceChangeEffectivityDate ?? DateTime.MinValue).ThenBy(r => r.RowNumber).First();

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

                    itemId = created.CompanyItemId;
                    if (!created.Result.Success) error = created.Result.Error;
                }
                else if (entries.Count > 0)
                {
                    var applied = await _companyItemService.ApplyHistoricalPricesAsync(itemId!.Value, entries, userId, ct);
                    if (!applied.Success) error = applied.Error;
                }

                if (error is not null || itemId is null)
                {
                    FailGroup(group, $"Save failed: {error ?? "the item was not saved."}");
                    summary.Failed++;
                    summary.Outcomes.Add(new(code, CompanyItemCommitStatus.Failed, error ?? "The item was not saved."));
                    continue;
                }

                if (group.BackfillStockUomName is not null)
                {
                    // Null fields on the DTO keep the current values; only the Stock UOM is set.
                    var filled = await _companyItemService.UpdateCompanyItemAsync(new CompanyItemUpdateDto
                    {
                        CompanyItemId = itemId.Value,
                        StockUomName = group.BackfillStockUomName,
                        IsActive = group.Rows[0].ExistingIsActive ?? true,
                        UpdatedBy = userId
                    });

                    if (filled == null)
                    {
                        FailGroup(group, "Prices were saved, but the Stock UOM could not be set.");
                        summary.Failed++;
                        summary.Outcomes.Add(new(code, CompanyItemCommitStatus.Failed, "Prices were saved, but the Stock UOM could not be set."));
                        continue;
                    }
                    summary.StockUomBackfilled++;
                }

                foreach (var r in group.Rows) r.CompanyItemId = itemId;
                group.IsSaved = true;

                summary.PricesScheduled += entries.Count(e => e.EffectivityDate.Date > today);
                summary.PricesApplied += entries.Count(e => e.EffectivityDate.Date <= today);
                summary.PricesSkipped += group.Operations.Count(o => o.Kind == CompanyItemOperationKind.SkipDuplicate);

                if (group.IsNewItem) summary.ItemsCreated++; else summary.ItemsUpdated++;

                summary.Outcomes.Add(new(code,
                    group.IsNewItem ? CompanyItemCommitStatus.Created : CompanyItemCommitStatus.Updated,
                    DescribeOutcome(group, entries, today)));
            }
            catch (Exception ex)
            {
                var msg = ex.GetBaseException().Message;
                FailGroup(group, $"Save failed: {msg}");
                summary.Failed++;
                summary.Outcomes.Add(new(code, CompanyItemCommitStatus.Failed, msg));
            }
        }

        return summary;
    }

    private static void FailGroup(PreparedCompanyItemGroup group, string message)
    {
        foreach (var r in group.Rows)
        {
            r.Issues.Add(message);
            r.IsSuccess = false;
        }
        group.Issues.Add(new CompanyItemImportIssue(group.Rows[0].RowNumber, group.ItemCode, message));
    }

    private static string DescribeOutcome(PreparedCompanyItemGroup group, List<HistoricalPriceRow> entries, DateTime today)
    {
        var parts = new List<string>();
        if (group.IsNewItem) parts.Add("item created");
        var applied = entries.Count(e => e.EffectivityDate.Date <= today);
        var scheduled = entries.Count - applied;
        if (applied > 0) parts.Add($"{applied} price{(applied == 1 ? "" : "s")} applied");
        if (scheduled > 0) parts.Add($"{scheduled} scheduled");
        if (group.BackfillStockUomName is not null) parts.Add("Stock UOM filled in");
        return parts.Count == 0 ? "No changes." : string.Join(", ", parts) + ".";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    // US month-first. Excel date cells are read directly and never go through text parsing,
    // and the server's culture is never used.
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
            // fall through to text parsing
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

    private static decimal? ReadPrice(string? text, string label, CompanyItemImportRowResult row)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) && value > 0)
            return value;

        row.Issues.Add($"{label} '{text}' is not a valid number.");
        return null;
    }

    private static bool SameListType(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static void AddWarningOnce(CompanyItemImportRowResult row, string message)
    {
        if (!row.Warnings.Contains(message)) row.Warnings.Add(message);
    }

    private static string FormatRowsText(IEnumerable<int> rowNumbers)
    {
        var rows = rowNumbers.Distinct().OrderBy(r => r).ToList();
        if (rows.Count == 1) return $"row {rows[0]}";
        if (rows.Count <= 5) return $"rows {string.Join(", ", rows.Take(rows.Count - 1))} & {rows[^1]}";
        return $"rows {string.Join(", ", rows.Take(5))} and {rows.Count - 5} more";
    }

    // ── Header detection ────────────────────────────────────────────────────

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

            result.AddError(detection.BestCandidateRowNumber, string.Empty,
                $"Header found at row {detection.BestCandidateRowNumber} Missing: {missingList} Columns found: {unmatchedColumns} {renameHints}");
        }
        else
        {
            result.AddError(0, string.Empty,
                $"Could not find a header row within the first {MaxHeaderScanRows} rows containing the required columns: " +
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
                        columnIndex[canonicalKey.Replace(" ", "")] = cell.Address.ColumnNumber;
                }
            }

            var missing = requiredKeys.Where(key => !foundCanonicalKeys.Contains(key)).ToList();

            if (missing.Count == 0)
            {
                return new HeaderDetectionResult(rowNumber, columnIndex, -1, Array.Empty<string>(),
                    new List<string>(), allHeaderColumns, new List<string>());
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

        return new HeaderDetectionResult(-1, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            bestCandidateRowNumber, bestCandidateHeaders, bestCandidateMissing,
            new List<(int, string)>(), bestCandidateUnmatchedHeaders);
    }

    private static string GetString(IXLRow row, int columnNumber)
    {
        var cell = row.Cell(columnNumber);
        if (cell.HasFormula) return cell.CachedValue.ToString()?.Trim() ?? string.Empty;
        return cell.GetString().Trim();
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s\.\#\/\-\,\:\(\)]+", " ").Trim();
    }

    private static string GetAcceptedAliasesText(string canonicalKey) =>
        RequiredHeaderMap.TryGetValue(canonicalKey, out var required) ? string.Join(", ", required)
        : OptionalHeaderMap.TryGetValue(canonicalKey, out var optional) ? string.Join(", ", optional)
        : "no known aliases";

    // Downloadable Excel report: original columns + an "Error" column, failed rows only.
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

        int excelRow = 2;
        foreach (var row in result.Rows.Where(r => !r.IsSuccess).OrderBy(r => r.RowNumber))
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
}