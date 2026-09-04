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
            ["Customer Name"]  = new[] { "CustomerName", "Customer Name", "name", "SHIPTONAME", "Ship To Name" },
            ["Subd Cust Code"] = new[] { "SubdCustCode", "Subd Cust Code", "SUBD CUSTOMER CODE", "Subd Customer Code" },
            ["Subd Cust Name"] = new[] { "SubdCustName", "Subd Cust Name", "SUBD STORE NAME", "Subd Store Name" },
            ["Province"]       = new[] { "Province", "SUBD ADDRESS (PROVINCE)", "Subd Address (Province)" },
            ["City"]           = new[] { "City", "CITY/MUNICIPALITY", "municipality", "SUBD ADDRESS (CITY)", "Subd Address (City)" },
        };

    private static readonly IReadOnlyDictionary<string, string[]> OptionalHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Address Line"] = new[] { "AddressLine", "Address Line", "barangay", "SUBD ADDRESS (STREET/BRGY)", "Subd Address (Street/Brgy)" },
            ["Zip Code"]     = new[] { "ZipCode", "Zip Code", "zip" },
            ["Customer Type"] = new[] { "CustomerType", "Customer Type", "type" }, 
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
                var rawHeadersShown = string.Join(" | ", detection.BestCandidateHeaders.Select(h => $"\"{h}\""));
                result.AddError(detection.BestCandidateRowNumber, string.Empty,
                    $"Closest header row found at row {detection.BestCandidateRowNumber}, but it's missing: {string.Join(", ", detection.BestCandidateMissing)}. "
                    + $"Columns actually found on that row: {rawHeadersShown}");
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
                SubdCustCode = rowResult.SubdCustCode,        // NEW
                SubdCustName = rowResult.SubdCustName,        // NEW
                CustomerType = rowResult.CustomerType,
                SubDistributorId = subdistributorId,
                IsActive = true,
                AddressLine = rowResult.AddressLine,
                Province = rowResult.Province,
                City = rowResult.City,
                ZipCode = rowResult.ZipCode
            };
            foreach (var msg in (await CustomerValidations.ValidateAddCustomerAsync(entity, _customerService)).Values)
                rowResult.Issues.Add(msg);

            // Cross-check the location against the geographic reference data.
            if (!string.IsNullOrWhiteSpace(rowResult.Province) || !string.IsNullOrWhiteSpace(rowResult.City))
            {
                var match = await _geoDataService.FindLocationAsync(rowResult.Province, rowResult.City);

                if (match is null)
                {
                    if (!string.IsNullOrWhiteSpace(rowResult.Province) &&
                        !await _geoDataService.ProvinceExistsAsync(rowResult.Province))
                    {
                        rowResult.Issues.Add($"Province '{rowResult.Province}' was not found in the geographic reference data.");
                    }
                    else if (string.IsNullOrWhiteSpace(rowResult.City))
                    {
                        rowResult.Issues.Add($"City is required to validate the location for Province '{rowResult.Province}'.");
                    }
                    else
                    {
                        rowResult.Issues.Add(
                            $"City '{rowResult.City}' does not match Province '{rowResult.Province}' in the geographic reference data.");
                    }
                }
                else if (rowResult.ZipCode is null && match.ZipCode.HasValue)
                {
                    // The sheet left Zip blank but the reference data has one for this exact city/province — fill it in.
                    rowResult.ZipCode = match.ZipCode;
                }
            }
            var dupKey = $"{rowResult.CustomerCode}|{rowResult.CustomerName}|{rowResult.SubdCustCode}|{rowResult.SubdCustName}";
            if (!string.IsNullOrWhiteSpace(rowResult.CustomerCode) && !seenInFile.Add(dupKey))
                rowResult.Issues.Add($"Row is an exact duplicate of another row in this file: Customer Code '{rowResult.CustomerCode}', Subd Customer Code '{rowResult.SubdCustCode}', Subd Store Name '{rowResult.SubdCustName}' all match another row.");
                
            rowResult.IsSuccess = rowResult.Issues.Count == 0;
            result.Rows.Add(rowResult);

            var group = new PreparedCustomerGroup(new List<CustomerImportRowResult> { rowResult })
            {
                Selected = rowResult.IsSuccess
            };
            foreach (var issue in rowResult.Issues)
                group.Issues.Add(new CustomerImportIssue(rowNumber, rowResult.CustomerCode, issue));

            result.PreparedGroups.Add(group);
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
        List<(int Column, string Header)> AllHeaderColumns); 

    private static HeaderDetectionResult DetectHeaderRow(IXLWorksheet worksheet)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var scanLimit = Math.Min(MaxHeaderScanRows, lastRow);

        int bestMissingCount = int.MaxValue;
        int bestCandidateRowNumber = -1;
        string[] bestCandidateHeaders = Array.Empty<string>();
        List<string> bestCandidateMissing = new();

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

            var missing = RequiredHeaderMap.Keys.Where(key => !foundCanonicalKeys.Contains(key)).ToList();

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
            : new List<string> { "SUBD CUSTOMER CODE", "SUBD STORE NAME", "SUBD ADDRESS (STREET/BRGY)",
                                "SUBD ADDRESS (CITY)", "SUBD ADDRESS (PROVINCE)", "SHIPTOCODE", "SHIPTONAME" };

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
        "SUBD CUSTOMER CODE", "SUBD STORE NAME", "SUBD ADDRESS (STREET/BRGY)",
        "SUBD ADDRESS (CITY)", "SUBD ADDRESS (PROVINCE)", "SHIPTOCODE", "SHIPTONAME"
    };
    public async Task<byte[]> GenerateTemplateExcelAsync()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Customers");

        for (int i = 0; i < TemplateHeaders.Length; i++)
            sheet.Cell(1, i + 1).Value = TemplateHeaders[i];

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EDE9FB");

        // A=SubdCustCode B=SubdCustName C=Address D=City E=Province F=ShipToCode G=ShipToName
        sheet.Cell(2, 1).Value = "SUBD-0001";
        sheet.Cell(2, 2).Value = "Green Breeze - Montalban Rizal";
        sheet.Cell(2, 3).Value = "Brgy San Isidro";
        sheet.Cell(2, 4).Value = "Montalban (Rodriguez)";
        sheet.Cell(2, 5).Value = "Rizal";
        sheet.Cell(2, 6).Value = "CUST-0001";
        sheet.Cell(2, 7).Value = "Juan Dela Cruz";
        sheet.Row(2).Style.Font.Italic = true;
        sheet.Row(2).Style.Font.FontColor = XLColor.FromHtml("#A09ABF");

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
            }

            // City is column D, referencing Province (E) on the same row
            var cityValidation = sheet.Range($"D2:D{lastDataRow}").CreateDataValidation();
            cityValidation.List("INDIRECT(\"Prov_\"&SUBSTITUTE(SUBSTITUTE(E2,\" \",\"_\"),\"-\",\"_\"))");
            cityValidation.IgnoreBlanks = true;
            cityValidation.ShowInputMessage = true;
            cityValidation.InputTitle = "City / Municipality";
            cityValidation.InputMessage = "Select Province (column E) first — this list filters to match it.";
            cityValidation.ShowErrorMessage = false;
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
}
