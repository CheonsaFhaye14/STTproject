using System.Globalization;
using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Validators;
using CsvHelper;
using CsvHelper.Configuration;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public enum TestVerdict { Passed, NeedsLook, Blocked }

public enum TestSheetStatus { Passed, NeedsLook, Blocked, NotFound }

public enum FieldState { Found, OptionalMissing, RequiredMissing, Ambiguous }

public enum IssueKind
{
    FileUnreadable, TemplateStructure,
    MissingSheet, AmbiguousSheets, EmptySheet, HeaderRow,
    MissingColumn, AmbiguousHeader,
    OptionalSheet, OptionalColumns, RowProblem
}

public sealed class TemplateTestIssue
{
    public IssueKind Kind { get; init; }
    public bool IsBlocking { get; init; }
    public string? SheetLabel { get; init; }
    public int? RowNumber { get; init; }
    public string Title { get; init; } = "";
    public string? Detail { get; init; }
    public string? FixInFile { get; init; }
    public string? FixInTemplate { get; init; }

    public string KindLabel => Kind switch
    {
        IssueKind.FileUnreadable => "File problem",
        IssueKind.TemplateStructure => "Template problem",
        IssueKind.MissingSheet => "Missing sheet",
        IssueKind.AmbiguousSheets => "Ambiguous sheets",
        IssueKind.EmptySheet => "Empty sheet",
        IssueKind.HeaderRow => "Header row",
        IssueKind.MissingColumn => "Missing column",
        IssueKind.AmbiguousHeader => "Ambiguous header",
        IssueKind.OptionalSheet => "Optional sheet",
        IssueKind.OptionalColumns => "Optional columns",
        _ => "Row problem"
    };
}

public sealed class TemplateTestResult
{
    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> Notes { get; } = new();
    public List<TemplateTestSheet> Sheets { get; } = new();

    /// <summary>Everything wrong or worth a look, blocking first. This is what the UI lists.</summary>
    public List<TemplateTestIssue> Issues { get; } = new();

    /// <summary>The worksheets in the file and which template sheet reads each one.</summary>
    public List<TemplateTestFileSheet> FileSheets { get; } = new();

    public TestVerdict Verdict =>
        Issues.Any(i => i.IsBlocking) ? TestVerdict.Blocked
        : Issues.Count > 0 || Warnings.Count > 0 ? TestVerdict.NeedsLook
        : TestVerdict.Passed;

    public int BlockingCount => Issues.Count(i => i.IsBlocking);
    public int ReviewCount => Issues.Count(i => !i.IsBlocking);
    public int SheetsExpected => Sheets.Count;
    public int SheetsFound => Sheets.Count(s => s.Status != TestSheetStatus.NotFound);
    public int RowsChecked => Sheets.Sum(s => s.Rows.Count);
}

public sealed record TemplateTestFileSheet(string Name, string? UsedBy, bool IsHidden);

public sealed class TemplateTestSheet
{
    public string Label { get; init; } = "";
    public bool IsRequired { get; init; }
    public TestSheetStatus Status { get; set; } = TestSheetStatus.Passed;
    public string? ExcelSheetName { get; set; }
    public int? HeaderRow { get; set; }
    public List<string> Problems { get; } = new();
    public List<TemplateTestColumn> Columns { get; } = new();

    /// <summary>One entry per system field the template maps on this sheet, in template order.</summary>
    public List<TemplateTestField> Fields { get; } = new();
    public List<TemplateTestRow> Rows { get; } = new();
}

/// <summary>What became of one system field on one sheet: found (and through which header), missing, or ambiguous.</summary>
public sealed class TemplateTestField
{
    public string FieldKey { get; init; } = "";
    public string Display { get; init; } = "";
    public bool IsRequired { get; init; }
    public FieldState State { get; init; }
    public string? FoundHeader { get; init; }
    public bool IsAlias { get; init; }

    /// <summary>Every header the template accepts for this field (primary first, then aliases).</summary>
    public List<string> LookedFor { get; init; } = new();

    /// <summary>The file columns this field matched. More than one only when the field is ambiguous.</summary>
    public List<TemplateTestSourceColumn> Sources { get; } = new();
}

public sealed record TemplateTestSourceColumn(string Header, int ColumnIndex)
{
    public string Letter => XLHelper.GetColumnLetterFromNumber(ColumnIndex);
}

/// <param name="ColumnIndex">Where the header was found, or null if it isn't in the sheet.</param>
/// <param name="IsRequired">True only when the field is required and none of its aliases was found.</param>
/// <param name="IsUsed">The field is read from this column.</param>
/// <param name="IsAmbiguous">Found, but its field matched more than one column, so it isn't read.</param>
/// <param name="IsAlias">The header is an alias of the column's primary header, not the primary itself.</param>
public sealed record TemplateTestColumn(
    string HeaderText, string? FieldKey, int? ColumnIndex, bool IsRequired, bool IsUsed = true, bool IsAmbiguous = false,
    bool IsAlias = false);

public sealed class TemplateTestRow
{
    public int RowNumber { get; init; }
    public Dictionary<string, string> Values { get; } = new();      // FieldKey -> processed (after the template's rule)
    public Dictionary<string, string> Original { get; } = new();    // FieldKey -> text as it is in the file
    public HashSet<string> BadFields { get; } = new();              // FieldKeys whose value has a problem
    public Dictionary<int, string> AmbiguousRaw { get; } = new();   // source column -> text, for ambiguous fields
    public List<string> Messages { get; } = new();
}

public interface IImportTemplateTestService
{
    TemplateTestResult Test(ImportTemplateEditDto template, Stream file, string fileName, int maxRows = 50);
}

public sealed class ImportTemplateTestService : IImportTemplateTestService
{
    private const int MaxRowIssues = 5;

    public TemplateTestResult Test(ImportTemplateEditDto template, Stream file, string fileName, int maxRows = 50)
    {
        var result = new TemplateTestResult();

        // Structure only: an incomplete template can still be tested. Missing required groups are reported, not blocking.
        var errors = ImportTemplateValidator.ValidateStructure(template);
        if (errors.Count > 0)
        {
            foreach (var e in errors)
            {
                result.Errors.Add(e);
                result.Issues.Add(new TemplateTestIssue
                {
                    Kind = IssueKind.TemplateStructure,
                    IsBlocking = true,
                    Title = e,
                    FixInTemplate = "Fix this in the template, then test again."
                });
            }
            return result;
        }

        result.Warnings.AddRange(ImportTemplateValidator.RequiredGroupErrors(template)
            .Select(m => $"Can't be saved yet: {m}"));

        XLWorkbook wb;
        try
        {
            wb = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? CsvToWorkbook(file, fileName)
                : new XLWorkbook(file);
        }
        catch (Exception)
        {
            const string msg = "Couldn't read this file. Check that it's a valid .xlsx or .csv.";
            result.Errors.Add(msg);
            result.Issues.Add(new TemplateTestIssue
            {
                Kind = IssueKind.FileUnreadable,
                IsBlocking = true,
                Title = msg,
                FixInFile = "Open it in Excel, save it again as .xlsx or .csv, and choose it again."
            });
            return result;
        }

        using (wb)
        {
            ReportWorkbook(template, WorkbookResolver.Resolve(template, wb), result, maxRows);
        }

        // Blocking first, then in the order they were found.
        var ordered = result.Issues.OrderBy(i => i.IsBlocking ? 0 : 1).ToList();
        result.Issues.Clear();
        result.Issues.AddRange(ordered);

        return result;
    }

    // One TemplateTestSheet per template sheet that isn't Ignore, in template order, whether or not it was found.
    private static void ReportWorkbook(
        ImportTemplateEditDto template, ResolvedWorkbook resolved, TemplateTestResult result, int maxRows)
    {
        var match = resolved.Match;

        foreach (var w in match.Worksheets)
            result.FileSheets.Add(new TemplateTestFileSheet(
                w.Worksheet.Name, w.TemplateSheet?.SheetLabel, w.Status == WorksheetStatus.Hidden));

        if (match.IsTruncated)
        {
            result.Errors.Add(WorkbookMatcher.TruncatedMessage);
            result.Issues.Add(new TemplateTestIssue
            {
                Kind = IssueKind.AmbiguousSheets,
                IsBlocking = true,
                Title = "Too many ways to match this file's sheets to the template",
                Detail = WorkbookMatcher.TruncatedMessage,
                FixInFile = "Give the sheets in the file clearly different names.",
                FixInTemplate = "Or give the template's sheets more distinctive names, positions or headers."
            });
            return;
        }

        var visibleNames = string.Join(", ", match.Worksheets
            .Where(w => w.Status != WorksheetStatus.Hidden).Select(w => $"\"{w.Worksheet.Name}\""));
        var reportedAmbiguities = new HashSet<SheetAmbiguity>();

        foreach (var ts in template.Sheets.Where(s => s.SheetMatchMode != SheetMatchModes.Ignore))
        {
            var r = new TemplateTestSheet { Label = ts.SheetLabel, IsRequired = ts.IsRequired };
            result.Sheets.Add(r);

            var rs = resolved.For(ts);
            if (rs is not null)
            {
                ReadSheet(template.ImportType, rs, r, maxRows);
                AddSheetIssues(r, rs, result);
                continue;
            }

            var ambiguity = match.Ambiguities.FirstOrDefault(a => a.TemplateSheets.Contains(ts));
            if (ambiguity is not null)
            {
                r.Status = TestSheetStatus.Blocked;
                r.Problems.Add(ambiguity.Message);
                if (reportedAmbiguities.Add(ambiguity))
                    result.Issues.Add(new TemplateTestIssue
                    {
                        Kind = IssueKind.AmbiguousSheets,
                        IsBlocking = true,
                        SheetLabel = ts.SheetLabel,
                        Title = "Can't tell which sheet is which",
                        Detail = ambiguity.Message,
                        FixInFile = "Rename or remove sheets so each template sheet fits only one.",
                        FixInTemplate = "Or give the template's sheets more distinctive names, positions or headers."
                    });
                continue;
            }

            var missing = match.MissingRequired.Concat(match.MissingOptional)
                .FirstOrDefault(m => ReferenceEquals(m.TemplateSheet, ts));
            var reason = missing is null ? null : Capitalize(missing.Reason);
            r.Status = TestSheetStatus.NotFound;
            r.Problems.Add(ts.IsRequired
                ? "Required sheet not found in this file" + (reason is null ? "." : $": {reason}.")
                : "Sheet not found (it's optional, so it would be skipped)" + (reason is null ? "." : $": {reason}."));

            if (ts.IsRequired)
                result.Issues.Add(new TemplateTestIssue
                {
                    Kind = IssueKind.MissingSheet,
                    IsBlocking = true,
                    SheetLabel = ts.SheetLabel,
                    Title = $"Required sheet \"{ts.SheetLabel}\" was not found",
                    Detail = (reason is null ? "" : reason + ". ") + (visibleNames.Length > 0 ? $"The file has {visibleNames}." : ""),
                    FixInFile = $"Add a sheet for \"{ts.SheetLabel}\", or rename the one that holds it.",
                    FixInTemplate = "Or change how the template finds this sheet, or mark it optional."
                });
            else
                result.Issues.Add(new TemplateTestIssue
                {
                    Kind = IssueKind.OptionalSheet,
                    IsBlocking = false,
                    SheetLabel = ts.SheetLabel,
                    Title = $"Optional sheet \"{ts.SheetLabel}\" was not found",
                    Detail = "It will be skipped."
                });
        }

        foreach (var u in match.Unclaimed)
            result.Notes.Add($"Sheet \"{u.Name}\" isn't used by this template.");
        foreach (var h in match.Hidden)
            result.Notes.Add($"Sheet \"{h.Name}\" is hidden, so it isn't matched.");
    }

    // Turns what one sheet resolved to into issues, and settles the sheet's status.
    private static void AddSheetIssues(TemplateTestSheet r, ResolvedWorksheet rs, TemplateTestResult result)
    {
        var label = r.Label;
        var blocked = false;

        if (rs.HeaderRow is null || rs.Columns is null)
        {
            var empty = rs.Inspection.IsEmpty;
            result.Issues.Add(new TemplateTestIssue
            {
                Kind = empty ? IssueKind.EmptySheet : IssueKind.HeaderRow,
                IsBlocking = true,
                SheetLabel = label,
                Title = empty ? $"{label}: the sheet is empty" : $"{label}: the header row wasn't found",
                Detail = rs.Problems.FirstOrDefault(),
                FixInFile = empty
                    ? "Check that this sheet holds the data."
                    : "Make sure the headers are in the first 20 rows and spelled the way the template expects.",
                FixInTemplate = empty ? null : "Or set the header row number in the template."
            });
            r.Status = TestSheetStatus.Blocked;
            return;
        }

        foreach (var f in r.Fields)
        {
            if (f.State == FieldState.RequiredMissing)
            {
                blocked = true;
                result.Issues.Add(new TemplateTestIssue
                {
                    Kind = IssueKind.MissingColumn,
                    IsBlocking = true,
                    SheetLabel = label,
                    Title = $"{label}: required column {f.Display} was not found",
                    Detail = $"Looked for {Quote(f.LookedFor)} in header row {r.HeaderRow}.",
                    FixInFile = f.LookedFor.Count == 1
                        ? "Rename the column header in the file to match."
                        : "Rename the column header in the file to one of those names.",
                    FixInTemplate = $"Or add the header your file uses as an alias of {f.Display}."
                });
            }
            else if (f.State == FieldState.Ambiguous)
            {
                blocked = true;
                var where = string.Join(", ", f.Sources.Select(s => $"\"{s.Header}\" (column {s.Letter})"));
                result.Issues.Add(new TemplateTestIssue
                {
                    Kind = IssueKind.AmbiguousHeader,
                    IsBlocking = true,
                    SheetLabel = label,
                    Title = $"{label}: {f.Display} matches more than one column",
                    Detail = $"{where} are all set up for {f.Display}, so it can't tell which one to read.",
                    FixInFile = "Keep one of those columns, or rename the others.",
                    FixInTemplate = "Or remove the extra headers from the template."
                });
            }
        }

        if (rs.Problems.Count > 0) blocked = true;

        var optional = r.Fields.Where(f => f.State == FieldState.OptionalMissing).ToList();
        if (optional.Count > 0)
            result.Issues.Add(new TemplateTestIssue
            {
                Kind = IssueKind.OptionalColumns,
                IsBlocking = false,
                SheetLabel = label,
                Title = optional.Count == 1
                    ? $"{label}: optional column {optional[0].Display} was not found"
                    : $"{label}: {optional.Count} optional columns were not found",
                Detail = (optional.Count == 1 ? "" : string.Join(", ", optional.Select(o => o.Display)) + ". ")
                         + "These fields will be left empty."
            });

        var badRows = r.Rows.Where(x => x.Messages.Count > 0).ToList();
        if (!blocked)
        {
            foreach (var row in badRows.Take(MaxRowIssues))
                result.Issues.Add(new TemplateTestIssue
                {
                    Kind = IssueKind.RowProblem,
                    IsBlocking = false,
                    SheetLabel = label,
                    RowNumber = row.RowNumber,
                    Title = string.Join("; ", row.Messages),
                    Detail = "That row would be flagged during import."
                });
            if (badRows.Count > MaxRowIssues)
                result.Issues.Add(new TemplateTestIssue
                {
                    Kind = IssueKind.RowProblem,
                    IsBlocking = false,
                    SheetLabel = label,
                    Title = $"{label}: {badRows.Count - MaxRowIssues} more rows have problems"
                });
        }

        r.Status = blocked ? TestSheetStatus.Blocked
            : optional.Count > 0 || badRows.Count > 0 ? TestSheetStatus.NeedsLook
            : TestSheetStatus.Passed;
    }

    private static void ReadSheet(string importType, ResolvedWorksheet rs, TemplateTestSheet r, int maxRows)
    {
        var ws = rs.Worksheet;
        r.ExcelSheetName = rs.Inspection.Name;
        r.HeaderRow = rs.HeaderRow;

        if (rs.HeaderRow is null || rs.Columns is null)
        {
            r.Problems.AddRange(rs.Problems);
            return;
        }

        foreach (var a in rs.Columns.Aliases)
            r.Columns.Add(new TemplateTestColumn(
                a.HeaderText, a.Column.FieldKey, a.ColumnIndex, a.IsMissingRequired, a.IsUsed, a.IsAmbiguous, a.IsAlias));

        BuildFields(importType, rs, r);
        r.Problems.AddRange(rs.Problems);

        var mapped = rs.Columns.Aliases
            .Where(a => a.IsUsed)
            .Select(a => (Col: a.Column, Index: a.ColumnIndex!.Value))
            .ToList();
        var ambiguousSources = r.Fields.Where(f => f.State == FieldState.Ambiguous).SelectMany(f => f.Sources).ToList();

        var lastRow = rs.Inspection.LastRow;
        var rowsRead = 0;
        for (var rowNo = rs.FirstDataRow; rowNo <= lastRow && rowsRead < maxRows; rowNo++)
        {
            if (ws.Row(rowNo).IsEmpty()) continue;
            rowsRead++;

            var tr = new TemplateTestRow { RowNumber = rowNo };
            foreach (var (col, idx) in mapped)
            {
                var before = tr.Messages.Count;
                tr.Values[col.FieldKey!] = ReadCell(importType, ws.Cell(rowNo, idx), col, tr.Messages, out var original);
                tr.Original[col.FieldKey!] = original;
                if (tr.Messages.Count > before) tr.BadFields.Add(col.FieldKey!);
            }

            foreach (var src in ambiguousSources)
                tr.AmbiguousRaw[src.ColumnIndex] = CellReader.Text(ws.Cell(rowNo, src.ColumnIndex));

            r.Rows.Add(tr);
        }
    }

    // One entry per field, from the per-header results. Ambiguous fields also get every file column that matched.
    private static void BuildFields(string importType, ResolvedWorksheet rs, TemplateTestSheet r)
    {
        var candidate = rs.Inspection.GetCandidate(rs.HeaderRow!.Value, rs.HeaderRowCount);

        var groups = rs.Columns!.Aliases
            .Where(a => !string.IsNullOrWhiteSpace(a.Column.FieldKey))
            .GroupBy(a => a.Column.FieldKey!, StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups)
        {
            var used = g.FirstOrDefault(a => a.IsUsed);
            var ambiguous = g.Any(a => a.IsAmbiguous);
            var missingRequired = g.Any(a => a.IsMissingRequired);
            var looked = g.Select(a => a.HeaderText).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var field = new TemplateTestField
            {
                FieldKey = g.Key,
                Display = ImportFieldRegistry.GetField(importType, g.Key)?.Display ?? g.Key,
                IsRequired = g.Any(a => a.Column.IsRequired),
                State = ambiguous ? FieldState.Ambiguous
                    : used is not null ? FieldState.Found
                    : missingRequired ? FieldState.RequiredMissing
                    : FieldState.OptionalMissing,
                FoundHeader = used?.HeaderText,
                IsAlias = used?.IsAlias ?? false,
                LookedFor = looked
            };

            if (ambiguous)
            {
                foreach (var text in looked)
                    if (candidate.Columns.TryGetValue(TemplateSheetResolver.Norm(text), out var cols))
                        foreach (var c in cols)
                            if (field.Sources.All(s => s.ColumnIndex != c))
                                field.Sources.Add(new TemplateTestSourceColumn(text, c));
                field.Sources.Sort((a, b) => a.ColumnIndex.CompareTo(b.ColumnIndex));
            }

            r.Fields.Add(field);
        }
    }

    private static string ReadCell(
        string importType, IXLCell cell, ImportTemplateColumnEditDto col, List<string> messages, out string original)
    {
        original = RawText(cell);
        var type = ImportFieldRegistry.GetField(importType, col.FieldKey)?.Type ?? ImportFieldType.Text;

        if (type == ImportFieldType.Date)
        {
            // A real Excel date needs no format; only text dates do.
            if (cell.DataType == XLDataType.DateTime)
                return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var text = CellReader.Text(cell);
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
                : CellReader.Text(cell).Replace(",", "");
            if (raw.Length == 0) return "";

            var ok = type == ImportFieldType.Integer
                ? long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                : decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

            if (!ok)
                messages.Add($"{col.HeaderText}: \"{raw}\" isn't a valid {(type == ImportFieldType.Integer ? "whole number" : "number")}.");
            return raw;
        }

        var value = ImportRulePreview.Apply(CellReader.Text(cell), col.RuleType, col.OptionsJson);
        if (col.RuleType != ColumnRuleTypes.Direct && value.StartsWith("("))
            messages.Add($"{col.HeaderText}: {value}");
        return value;
    }

    // The cell as the file shows it. A real Excel date is shown the way Excel formats it.
    private static string RawText(IXLCell cell)
    {
        try
        {
            return cell.DataType == XLDataType.DateTime ? cell.GetFormattedString().Trim() : CellReader.Text(cell);
        }
        catch
        {
            return CellReader.Text(cell);
        }
    }

    private static string Quote(IEnumerable<string> names) =>
        string.Join(" and ", names.Select(n => $"\"{n}\""));

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

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