using System.Globalization;
using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Validators;
using CsvHelper;
using CsvHelper.Configuration;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public sealed class TemplateTestResult
{
    public List<string> Errors { get; } = new();     
    public List<TemplateTestSheet> Sheets { get; } = new();
}

public sealed class TemplateTestSheet
{
    public string Label { get; init; } = "";
    public string? ExcelSheetName { get; set; }
    public int? HeaderRow { get; set; }
    public List<string> Problems { get; } = new();
    public List<TemplateTestColumn> Columns { get; } = new();
    public List<TemplateTestRow> Rows { get; } = new();
}

public sealed record TemplateTestColumn(string HeaderText, string? FieldKey, int? ColumnIndex, bool IsRequired);

public sealed class TemplateTestRow
{
    public int RowNumber { get; init; }
    public Dictionary<string, string> Values { get; } = new();   // FieldKey -> result
    public List<string> Messages { get; } = new();
}

public interface IImportTemplateTestService
{
    TemplateTestResult Test(ImportTemplateEditDto template, Stream file, string fileName, int maxRows = 50);
}

public sealed class ImportTemplateTestService : IImportTemplateTestService
{

    public TemplateTestResult Test(ImportTemplateEditDto template, Stream file, string fileName, int maxRows = 50)
    {
        var result = new TemplateTestResult();

        var errors = ImportTemplateValidator.Validate(template);
        if (errors.Count > 0)
        {
            result.Errors.AddRange(errors);
            return result;
        }

        XLWorkbook wb;
        try
        {
            wb = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? CsvToWorkbook(file, fileName)
                : new XLWorkbook(file);
        }
        catch (Exception)
        {
            result.Errors.Add("Couldn't read this file. Check that it's a valid .xlsx or .csv.");
            return result;
        }

        using (wb)
        {
            var sheets = wb.Worksheets.ToList();
            var used = new HashSet<int>();   // worksheets already claimed by an earlier template sheet

            foreach (var ts in template.Sheets.Where(s => s.SheetMatchMode != SheetMatchModes.Ignore))
            {
                var r = new TemplateTestSheet { Label = ts.SheetLabel };
                result.Sheets.Add(r);

                var ws = TemplateSheetResolver.FindSheet(ts, sheets, used);
                if (ws is null)
                {
                    r.Problems.Add(ts.IsRequired
                        ? "Required sheet not found in this file."
                        : "Sheet not found (it's optional, so it would be skipped).");
                    continue;
                }

                used.Add(ws.Position);
                r.ExcelSheetName = ws.Name;
                ReadSheet(template.ImportType, ts, ws, r, maxRows);
            }
        }

        return result;
    }

    private static void ReadSheet(string importType, ImportTemplateSheetEditDto ts, IXLWorksheet ws, TemplateTestSheet r, int maxRows)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0) { r.Problems.Add("The sheet is empty."); return; }

        var headerRow = ts.HeaderRowMode == HeaderRowModes.Fixed
            ? ts.HeaderRowNumber ?? 1
            : TemplateSheetResolver.FindHeaderRow(ts, ws, Math.Min(lastRow, TemplateSheetResolver.HeaderScanRows), lastCol);

        if (headerRow is null)
        {
            r.Problems.Add("Couldn't find a header row. None of the header texts appear in the first rows.");
            return;
        }
        r.HeaderRow = headerRow;

        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 1; c <= lastCol; c++)
        {
            var text = ws.Cell(headerRow.Value, c).GetString().Trim();
            if (text.Length > 0) headers.TryAdd(TemplateSheetResolver.Norm(text), c);
        }

        var mapped = new List<(ImportTemplateColumnEditDto Col, int Index)>();
        foreach (var col in ts.Columns)
        {
            headers.TryGetValue(TemplateSheetResolver.Norm(col.HeaderText), out var idx);
            r.Columns.Add(new TemplateTestColumn(col.HeaderText, col.FieldKey, idx == 0 ? null : idx, col.IsRequired));

            if (idx == 0)
            {
                if (col.IsRequired)
                    r.Problems.Add($"Required column \"{col.HeaderText}\" not found in the header row.");
                continue;
            }
            mapped.Add((col, idx));
        }

        var rowsRead = 0;
        for (var rowNo = headerRow.Value + 1; rowNo <= lastRow && rowsRead < maxRows; rowNo++)
        {
            if (ws.Row(rowNo).IsEmpty()) continue;  
            rowsRead++;

            var tr = new TemplateTestRow { RowNumber = rowNo };
            foreach (var (col, idx) in mapped)
                tr.Values[col.FieldKey!] = ReadCell(importType, ws.Cell(rowNo, idx), col, tr.Messages);

            r.Rows.Add(tr);
        }
    }

    private static string ReadCell(string importType, IXLCell cell, ImportTemplateColumnEditDto col, List<string> messages)
    {
        var type = ImportFieldRegistry.GetField(importType, col.FieldKey)?.Type ?? ImportFieldType.Text;

        if (type == ImportFieldType.Date)
        {
            // A real Excel date needs no format; only text dates do.
            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var text = cell.GetString().Trim();
            if (text.Length == 0) return "";

            var shown = ImportRulePreview.ApplyDate(text, col.OptionsJson);
            if (shown.StartsWith("(") || shown.Contains("could be swapped"))
                messages.Add($"{col.HeaderText}: {shown}");
            return shown;
        }

        if (type is ImportFieldType.Integer or ImportFieldType.Decimal)
        {
            var raw = cell.DataType == XLDataType.Number
                ? cell.GetDouble().ToString(CultureInfo.InvariantCulture)
                : cell.GetString().Trim().Replace(",", "");
            if (raw.Length == 0) return "";

            var ok = type == ImportFieldType.Integer
                ? long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                : decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

            if (!ok)
                messages.Add($"{col.HeaderText}: \"{raw}\" isn't a valid {(type == ImportFieldType.Integer ? "whole number" : "number")}.");
            return raw;
        }

        var value = ImportRulePreview.Apply(cell.GetString().Trim(), col.RuleType, col.OptionsJson);
        if (col.RuleType != ColumnRuleTypes.Direct && value.StartsWith("("))
            messages.Add($"{col.HeaderText}: {value}");
        return value;
    }
    private const int CsvMaxRows = 500;   

    private static XLWorkbook CsvToWorkbook(Stream file, string fileName)
    {
        var name = new string(Path.GetFileNameWithoutExtension(fileName)
            .Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        if (name.Length > 31) name = name[..31];          
        if (string.IsNullOrWhiteSpace(name)) name = "Sheet1";

        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(name);

        using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            DetectDelimiter = true,      
            BadDataFound = null,
            MissingFieldFound = null
        };
        using var csv = new CsvParser(reader, config);

        var row = 0;
        while (row < CsvMaxRows && csv.Read())
        {
            row++;
            var rec = csv.Record;
            if (rec is null) continue;

            for (var c = 0; c < rec.Length; c++)
                if (!string.IsNullOrEmpty(rec[c]))
                    ws.Cell(row, c + 1).SetValue(rec[c]);   
        }
        return wb;
    }
}