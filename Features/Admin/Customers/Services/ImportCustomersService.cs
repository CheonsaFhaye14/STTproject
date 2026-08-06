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

    private static readonly IReadOnlyDictionary<string, string[]> RequiredHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Customer Code"] = new[] { "CustomerCode", "Customer Code", "code" },
            ["Customer Name"] = new[] { "CustomerName", "Customer Name", "name" },
            ["Customer Type"] = new[] { "CustomerType", "Customer Type", "type" },
            ["City"] = new[] { "City", "CITY/MUNICIPALITY", "municipality" },
            ["Province"] = new[] { "Province" },
        };

    private static readonly IReadOnlyDictionary<string, string[]> OptionalHeaderMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Address Line"] = new[] { "AddressLine", "Address Line", "barangay" },
            ["Zip Code"] = new[] { "ZipCode", "Zip Code", "zip" },
        };

    // Normalized alias -> canonical key, built once.
    private static readonly IReadOnlyDictionary<string, string> AliasLookup = BuildAliasLookup();

    public ImportCustomersService(IAdminCustomerService customerService)
    {
        _customerService = customerService;
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
                CustomerType = Get("CustomerType") ?? string.Empty,
                AddressLine = Get("AddressLine"),
                City = Get("City"),
                Province = Get("Province"),
                ZipCode = zip
            };

            // Skip fully-blank data rows (all required fields empty).
            if (string.IsNullOrWhiteSpace(rowResult.CustomerCode) &&
                string.IsNullOrWhiteSpace(rowResult.CustomerName) &&
                string.IsNullOrWhiteSpace(rowResult.CustomerType) &&
                string.IsNullOrWhiteSpace(rowResult.City) &&
                string.IsNullOrWhiteSpace(rowResult.Province))
            {
                continue;
            }

            var entity = new Customer
            {
                CustomerCode = rowResult.CustomerCode,
                CustomerName = rowResult.CustomerName,
                CustomerType = rowResult.CustomerType,
                SubDistributorId = subdistributorId,
                IsActive = true,
                AddressLine = rowResult.AddressLine,
                City = rowResult.City,
                Province = rowResult.Province,
                ZipCode = rowResult.ZipCode
            };
            foreach (var msg in (await CustomerValidations.ValidateAddCustomerAsync(entity, _customerService)).Values)
                rowResult.Issues.Add(msg);

            if (!string.IsNullOrWhiteSpace(rowResult.CustomerCode) && !seenInFile.Add(rowResult.CustomerCode))
                rowResult.Issues.Add($"Customer Code '{rowResult.CustomerCode}' is duplicated within the uploaded file.");

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
        List<string> BestCandidateMissing);

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
                return new HeaderDetectionResult(rowNumber, columnIndex, -1, Array.Empty<string>(), new List<string>());
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
            bestCandidateRowNumber, bestCandidateHeaders, bestCandidateMissing);
    }

    private static string GetString(IXLRow row, int columnNumber)
    {
        var cell = row.Cell(columnNumber);
        if (cell.HasFormula)
            return cell.CachedValue.ToString()?.Trim() ?? string.Empty;
        return cell.GetString().Trim();
    }

    // Same normalization the other importers use: lowercase, collapse whitespace/punctuation into a single space.
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
                    CustomerType = row.CustomerType,
                    SubDistributorId = subdistributorId,
                    IsActive = true,
                    AddressLine = row.AddressLine,
                    City = row.City,
                    Province = row.Province,
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
}