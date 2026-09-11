using System.Text.RegularExpressions;
using ClosedXML.Excel;
using STTproject.Data;
using STTproject.Features.Admin.Customers.DTOs;
using STTproject.Features.Admin.Customers.Validators;

namespace STTproject.Features.Admin.Customers.Services;

public sealed class ImportCustomersService
{
    private const int MaxHeaderScanRows = 10;

    private readonly IAdminCustomerService _customerService;
    private readonly IGeographicDataService _geoDataService;

    private static readonly IReadOnlyDictionary<string, string[]> RequiredHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Customer Code"]  = new[] { "CustomerCode", "Customer Code", "code", "SHIPTOCODE", "Ship To Code" },
            ["Customer Name"]  = new[] { "CustomerName", "Customer Name", "name", "SHIPTONAME", "Ship To Name","BILLTONAME" },
            ["Province"]       = new[] { "Province", "Subd Address (Province)" },
            ["City"]           = new[] { "City", "CITY/MUNICIPALITY", "municipality", "SUBD ADDRESS (CITY)", "City / Municipality" },
        };

    private static readonly IReadOnlyDictionary<string, string[]> OptionalHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Subd Cust Code"] = new[] { "SubdCustCode", "Subd Cust Code", "Subd Customer Code" },
            ["Subd Cust Name"] = new[] { "SubdCustName", "Subd Cust Name", "Subd Store Name" },
            ["Address Line"]   = new[] { "AddressLine", "Address Line", "barangay", "SUBD ADDRESS (STREET/BRGY)", "Subd Address (Street/Brgy)", "SUBD ADDRESS (BRGY)" },
            ["Zip Code"]       = new[] { "ZipCode", "Zip Code", "zip" , "ZIP CODE (OPTIONAL)", "Zip Code (Optional)" },
            ["Customer Type"]  = new[] { "CustomerType", "Customer Type", "type", "CUSTOMER TYPE (OPTIONAL)", "Customer Type (Optional)" }, 
        };

    private static readonly IReadOnlyDictionary<string, string> AliasLookup = BuildAliasLookup();

    public ImportCustomersService(IAdminCustomerService customerService, IGeographicDataService geoDataService)
    {
        _customerService = customerService;
        _geoDataService = geoDataService;
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

    // PHASE 1 — parse + validate directly from the uploaded Excel stream. No customers are created here.
    public async Task<CustomerImportResult> PrepareFromExcelAsync(Stream excelStream, int subdistributorId, CancellationToken ct = default)
    {
        var result = new CustomerImportResult { SubDistributorId = subdistributorId };

        if (excelStream is null || !excelStream.CanRead)
        {
            result.AddError(0, string.Empty, "Import file is missing or unreadable.");
            return result;
        }
        if (subdistributorId <= 0)
        {
            result.AddError(0, string.Empty, "Invalid subdistributor ID.");
            return result;
        }
        var subdistributors = await _customerService.GetSubDistributorsAsync();
        result.SubDistributorName = subdistributors
            .FirstOrDefault(s => s.SubDistributorId == subdistributorId)?.SubDistributorName;


        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            result.AddError(0, string.Empty, "The workbook does not contain any worksheets.");
            return result;
        }

        var detection = DetectHeaderRow(worksheet);

        if (detection.HeaderRowNumber == -1)
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
                result.AddError(0, string.Empty,
                    $"Could not find a header row within the first {MaxHeaderScanRows} rows containing the required columns: "
                    + string.Join(", ", RequiredHeaderMap.Keys));
            }
            return result;
        }

        var columnIndex = detection.ColumnIndex;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? detection.HeaderRowNumber;
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        result.OriginalHeaders = detection.AllHeaderColumns.Select(h => h.Header).ToList(); 

        for (int rowNumber = detection.HeaderRowNumber + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.CellsUsed().All(cell => cell.IsEmpty()))
                continue;

            string? Get(string column) =>
                columnIndex.TryGetValue(column, out var colNum)
                    ? GetString(row, colNum)
                    : null;

            var zipText = Get("ZipCode");
            int? zip = !string.IsNullOrWhiteSpace(zipText) && int.TryParse(zipText, out var z) ? z : null;

            var rowResult = new CustomerImportRowResult
            {
                RowNumber = rowNumber,
                CustomerCode = Get("CustomerCode") ?? string.Empty,
                CustomerName = Get("CustomerName") ?? string.Empty,
                SubdCustCode = Get("SubdCustCode") ?? string.Empty,
                SubdCustName = Get("SubdCustName") ?? string.Empty,
                CustomerType = Get("CustomerType"),
                AddressLine = Get("AddressLine"),
                Province = Get("Province"),
                City = Get("City"),
                ZipCode = zip
            };

            foreach (var (col, header) in detection.AllHeaderColumns)
            {
                rowResult.RawValues[header] = GetString(row, col);
            }

            if (string.IsNullOrWhiteSpace(rowResult.CustomerCode) &&
                string.IsNullOrWhiteSpace(rowResult.CustomerName) &&
                string.IsNullOrWhiteSpace(rowResult.CustomerType) &&
                string.IsNullOrWhiteSpace(rowResult.Province) && 
                string.IsNullOrWhiteSpace(rowResult.City) )
            {
                continue;
            }

            var entity = new Customer
            {
                CustomerCode = rowResult.CustomerCode,
                CustomerName = rowResult.CustomerName,
                SubdCustCode = rowResult.SubdCustCode,        
                SubdCustName = rowResult.SubdCustName,      
                CustomerType = rowResult.CustomerType,
                SubDistributorId = subdistributorId,
                IsActive = true,
                AddressLine = rowResult.AddressLine,
                Province = rowResult.Province,
                City = rowResult.City,
                ZipCode = rowResult.ZipCode
            };

            var validationIssues = (await CustomerValidations.ValidateAddCustomerAsync(entity, _customerService)).Values.ToList();

            // Import path uses its own duplicate check (real unique key + fillable-blank support)
            // instead of the generic "already exists" message from the base validator.
            foreach (var msg in validationIssues)
            {
                if (msg == "This Customer Code already exists for the selected Subdistributor.")
                    continue;
                rowResult.Issues.Add(msg);
            }

            var dupCheck = await CustomerImportValidation.ValidateDuplicateAsync(
                _customerService, rowResult.CustomerCode, rowResult.CustomerName, subdistributorId,
                rowResult.SubdCustCode, rowResult.SubdCustName);

            if (dupCheck.Outcome == ImportDuplicateOutcome.ExactDuplicate)
                rowResult.Issues.Add(dupCheck.IssueMessage!);
            else if (dupCheck.Outcome == ImportDuplicateOutcome.FillableBlank)
                rowResult.ExistingCustomerIdToUpdate = dupCheck.ExistingCustomerId;

            // Cross-check the location against the geographic reference data — only when both are provided.
            if (!string.IsNullOrWhiteSpace(rowResult.Province) && !string.IsNullOrWhiteSpace(rowResult.City))
            {
                var match = await _geoDataService.FindLocationAsync(rowResult.Province, rowResult.City);

                if (match is null)
                {
                    if (!await _geoDataService.ProvinceExistsAsync(rowResult.Province))
                        rowResult.Warnings.Add($"Province '{rowResult.Province}' was not found in the geographic reference data.");
                    else
                        rowResult.Warnings.Add($"City '{rowResult.City}' does not match Province '{rowResult.Province}' in the geographic reference data.");
                }
                else if (rowResult.ZipCode is null && match.ZipCode.HasValue)
                {
                    rowResult.ZipCode = match.ZipCode;
                }
            }
            var dupKey = $"{rowResult.CustomerCode}|{rowResult.CustomerName}|{rowResult.SubdCustCode}|{rowResult.SubdCustName}";
            if (!string.IsNullOrWhiteSpace(rowResult.CustomerCode) && !seenInFile.Add(dupKey))
                rowResult.Issues.Add($"Row is an exact duplicate of another row in this file: Customer Code '{rowResult.CustomerCode}', Subd Customer Code '{rowResult.SubdCustCode}', Subd Store Name '{rowResult.SubdCustName}' all match another row.");
                
            rowResult.IsSuccess = rowResult.Issues.Count == 0;
            result.Rows.Add(rowResult);
        }

        foreach (var custGroup in result.Rows.GroupBy(r => new
         {
             Code = (r.CustomerCode ?? string.Empty).Trim().ToUpperInvariant(),
             Name = (r.CustomerName ?? string.Empty).Trim().ToUpperInvariant()
         }))
        {
            var groupRows = custGroup.OrderBy(r => r.RowNumber).ToList();
            var group = new PreparedCustomerGroup(groupRows)
            {
                Selected = groupRows.All(r => r.IsSuccess)
            };

            foreach (var r in groupRows)
                foreach (var issue in r.Issues)
                    group.Issues.Add(new CustomerImportIssue(r.RowNumber, r.CustomerCode, issue));

            result.PreparedGroups.Add(group);
        }

        if (result.Rows.Count == 0 && !result.HasIssues)
        {
            result.AddError(0, string.Empty, "No customer rows were found in the file.");
        }

        if (result.Rows.Count == 0 && !result.HasIssues)
        {
            result.AddError(0, string.Empty, "No customer rows were found in the file.");
        }

        return result;
    }

    private sealed record HeaderDetectionResult(
        int HeaderRowNumber,
        Dictionary<string, int> ColumnIndex,
        int BestCandidateRowNumber,
        string[] BestCandidateHeaders,
        List<string> BestCandidateMissing,
        List<(int Column, string Header)> AllHeaderColumns,
        List<string> BestCandidateUnmatchedHeaders);

    private static HeaderDetectionResult DetectHeaderRow(IXLWorksheet worksheet)
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

            var missing = RequiredHeaderMap.Keys.Where(key => !foundCanonicalKeys.Contains(key)).ToList();

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

    // PHASE 2 — commit only the rows the user selected
    public async Task<int> CommitPreparedRowsAsync(IEnumerable<CustomerImportRowResult> rows, int subdistributorId, int userId, CancellationToken ct = default)
    {
        if (userId <= 0 || subdistributorId <= 0 || rows is null)
            return 0;

        var validRows = rows.Where(r => r.IsSuccess && r.Issues.Count == 0).ToList();
        if (validRows.Count == 0) return 0;

        var committed = 0;
        foreach (var row in validRows)
        {
            try
            {
                if (row.ExistingCustomerIdToUpdate is int existingId)
                {
                    var updated = await _customerService.FillBlankSubdMappingAsync(
                        existingId, row.SubdCustCode, row.SubdCustName, userId);
                    row.CustomerId = updated?.CustomerId ?? existingId;
                }
                else
                {
                    var created = await _customerService.CreateCustomerAsync(new CustomerCreateDto
                    {
                        CustomerCode = row.CustomerCode,
                        CustomerName = row.CustomerName,
                        SubdCustCode = row.SubdCustCode,
                        SubdCustName = row.SubdCustName,
                        CustomerType = row.CustomerType,
                        SubDistributorId = subdistributorId,
                        IsActive = true,
                        AddressLine = row.AddressLine,
                        Province = row.Province,
                        City = row.City,
                        ZipCode = row.ZipCode,
                        CreatedBy = userId
                    });
                    row.CustomerId = created?.CustomerId;
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
    public byte[] GenerateErrorReportExcel(CustomerImportResult result)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Errors");

        var headers = result.OriginalHeaders.Count > 0
                ? result.OriginalHeaders
                : TemplateHeaders.ToList();

        for (int i = 0; i < headers.Count; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        var errorColumn = headers.Count + 1;
        sheet.Cell(1, errorColumn).Value = "Error";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#000000");
        sheet.Row(1).Style.Font.FontColor = XLColor.FromHtml("#FFFFFF");
        sheet.SheetView.FreezeRows(1);

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
        "SUBD CUSTOMER CODE", "SUBD STORE NAME", "ADDRESS LINE",
        "CITY / MUNICIPALITY", "PROVINCE", "SHIPTOCODE", "SHIPTONAME",
        "ZIP CODE (OPTIONAL)", "CUSTOMER TYPE (OPTIONAL)"
    };
        
    public async Task<byte[]> GenerateTemplateExcelAsync()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Customers");

        for (int i = 0; i < TemplateHeaders.Length; i++)
            sheet.Cell(1, i + 1).Value = TemplateHeaders[i];

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#000000");
        sheet.Row(1).Style.Font.FontColor = XLColor.FromHtml("#FFFFFF");
        sheet.SheetView.FreezeRows(1);

        // A=SubdCustCode B=SubdCustName C=Address D=City E=Province F=ShipToCode G=ShipToName H=ZipCode I=CustomerType
        sheet.Cell(2, 1).Value = "SUBD-0001";
        sheet.Cell(2, 2).Value = "Ate Liza - Binangonan Rizal";
        sheet.Cell(2, 3).Value = "Pantok, Mabuhay Homes";
        sheet.Cell(2, 4).Value = "BINANGONAN";
        sheet.Cell(2, 5).Value = "RIZAL";
        sheet.Cell(2, 6).Value = "CUST-0001";
        sheet.Cell(2, 7).Value = "Ate Liza Store";
        sheet.Cell(2, 8).Value = "1940";
        sheet.Cell(2, 9).Value = "Sari Sari Store";

        void AddInputHint(int row, int column, string message)
        {
            var dv = sheet.Cell(row, column).CreateDataValidation();
            dv.ShowInputMessage = true;
            dv.InputMessage = message;
            dv.ShowErrorMessage = false;
        }

        AddInputHint(2, 1, "This is the Code of the customer based on Subdistributor.");
        AddInputHint(2, 2, "Name of the customer based on Subdistributor.");
        AddInputHint(2, 3, "Additional address of the customer.");
        AddInputHint(2, 6, "Company Code of the customer.");
        AddInputHint(2, 7, "Company Name of the customer.");
        AddInputHint(2, 8, "Auto-generated from the selected City and Province — no need to type this in.");

        const int lastDataRow = 500;

        var provinces = (await _geoDataService.GetAllProvincesAsync())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        if (provinces.Count > 0)
        {
            var geoSheet = workbook.Worksheets.Add("GeoData");
            geoSheet.Visibility = XLWorksheetVisibility.Hidden;

            for (int i = 0; i < provinces.Count; i++)
                geoSheet.Cell(i + 1, 1).Value = provinces[i];

            var provinceRange = geoSheet.Range(1, 1, provinces.Count, 1);
            workbook.NamedRanges.Add("ProvinceList", provinceRange);

            // Province is column E
            var provinceValidation = sheet.Range($"E2:E{lastDataRow}").CreateDataValidation();
            provinceValidation.List(provinceRange);
            provinceValidation.IgnoreBlanks = true;
            provinceValidation.ShowInputMessage = true;
            provinceValidation.InputTitle = "Province";
            provinceValidation.InputMessage = "Select a province.";
            provinceValidation.ShowErrorMessage = false;

            // Zip lookup table: Province|City -> Zip
            var zipLookupEntries = new List<(string Key, int? Zip)>();

            int col = 2;
            foreach (var province in provinces)
            {
                var cities = (await _geoDataService.GetCitiesMunicipalitiesByProvinceAsync(province))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();

                if (cities.Count == 0) continue;

                for (int i = 0; i < cities.Count; i++)
                    geoSheet.Cell(i + 1, col).Value = cities[i];

                var cityRange = geoSheet.Range(1, col, cities.Count, col);
                workbook.NamedRanges.Add(BuildProvinceDefinedName(province), cityRange);
                col++;

                foreach (var city in cities)
                {
                    var zip = await _geoDataService.GetZipCodeAsync(province, city);
                    zipLookupEntries.Add(($"{province}|{city}", zip));
                }
            }

            // City is column D, referencing Province (E) on the same row
            var cityValidation = sheet.Range($"D2:D{lastDataRow}").CreateDataValidation();
            cityValidation.List("INDIRECT(\"Prov_\"&SUBSTITUTE(SUBSTITUTE(E2,\" \",\"_\"),\"-\",\"_\"))");
            cityValidation.IgnoreBlanks = true;
            cityValidation.ShowInputMessage = true;
            cityValidation.InputTitle = "City / Municipality";
            cityValidation.InputMessage = "Select Province (column E) first — this list filters to match it.";
            cityValidation.ShowErrorMessage = false;

            // Zip is column H, referencing Province (E) and City (D) on the same row
            if (zipLookupEntries.Count > 0)
            {
                var zipKeyCol = col;
                var zipValueCol = col + 1;

                for (int i = 0; i < zipLookupEntries.Count; i++)
                {
                    geoSheet.Cell(i + 1, zipKeyCol).Value = zipLookupEntries[i].Key;
                    if (zipLookupEntries[i].Zip.HasValue)
                    {
                        geoSheet.Cell(i + 1, zipValueCol).Value = zipLookupEntries[i].Zip!.Value;
                    }
                }

                var zipLookupRange = geoSheet.Range(1, zipKeyCol, zipLookupEntries.Count, zipValueCol);
                workbook.NamedRanges.Add("ZipLookupTable", zipLookupRange);

                for (int row = 2; row <= lastDataRow; row++)
                {
                    sheet.Cell(row, 8).FormulaA1 =
                        $"=IFERROR(VLOOKUP(E{row}&\"|\"&D{row}, ZipLookupTable, 2, FALSE), \"\")";
                }
            }
        }

        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
    private static string BuildProvinceDefinedName(string province)
    {
        var sanitized = Regex.Replace(province, @"[^A-Za-z0-9]+", "_").Trim('_');
        if (sanitized.Length == 0 || char.IsDigit(sanitized[0]))
        {
            sanitized = "P_" + sanitized;
        }
        return $"Prov_{sanitized}";
    }
    private static string GetAcceptedAliasesText(string canonicalKey) =>
    RequiredHeaderMap.TryGetValue(canonicalKey, out var aliases)
            ? string.Join(", ", aliases)
            : "no known aliases";
}
