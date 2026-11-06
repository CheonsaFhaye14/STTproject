using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>
/// One matched template sheet after its header row and columns have been resolved against its worksheet.
/// Resolution only: nothing here reads data rows, and nothing joins sheets together.
/// </summary>
public sealed class ResolvedWorksheet
{
    public ImportTemplateSheetEditDto TemplateSheet { get; init; } = default!;
    public WorksheetInspection Inspection { get; init; } = default!;

    /// <summary>Null when the header row couldn't be found, or the sheet is empty.</summary>
    public int? HeaderRow { get; init; }
    public int HeaderRowCount { get; init; } = 1;

    /// <summary>Null when <see cref="HeaderRow"/> is null.</summary>
    public ColumnResolution? Columns { get; init; }

    /// <summary>
    /// Why this sheet can't be read as configured: empty sheet, no header row, required columns missing,
    /// ambiguous columns. Unprefixed; the caller adds the sheet label when it flattens them.
    /// </summary>
    private readonly List<PlanError> _problems = new();

    /// <summary>Same texts as before, read-only. Add through <see cref="AddProblem"/> so every problem has a kind.</summary>
    public IReadOnlyList<string> Problems => _problems.Select(p => p.Message).ToList();

    internal void AddProblem(PlanErrorKind kind, string message, object? detail = null) =>
        _problems.Add(new PlanError(kind, message, Inspection.SheetIndex, Inspection.Name, detail));

    /// <summary><see cref="ProblemMessages"/> as typed errors: same sentences, each naming the sheet.</summary>
    public IEnumerable<PlanError> ProblemErrors =>
        _problems.Select(p => p with
        {
            Message = p.Message.Contains($"\"{Inspection.Name}\"") ? p.Message : $"Sheet \"{Inspection.Name}\": {p.Message}"
        });

    public IXLWorksheet Worksheet => Inspection.Worksheet;
    public string Label => TemplateSheet.SheetLabel;

    /// <summary><see cref="Problems"/> ready to show, each naming the sheet (some already do).</summary>
    public IEnumerable<string> ProblemMessages => ProblemErrors.Select(p => p.Message);

    /// <summary>First row after the header block; 0 when there is no header row.</summary>
    public int FirstDataRow => HeaderRow is null ? 0 : HeaderRow.Value + HeaderRowCount;

    /// <summary>Fail-closed: any problem means the sheet isn't read. Whether that fails the workbook is the caller's policy.</summary>
    public bool IsReadable => HeaderRow is not null && Columns is not null && _problems.Count == 0;
}

/// <summary>
/// What one template makes of one workbook, as far as sheets and columns go: the sheet assignment
/// (<see cref="WorkbookMatch"/>) plus, for every matched sheet, its header row and column mapping.
/// Policy (which problems skip a sheet, which fail the file) belongs to the importer, not to this type.
/// </summary>
public sealed class ResolvedWorkbook
{
    public ResolvedWorkbook(WorkbookMatch match) => Match = match;

    public WorkbookMatch Match { get; }

    /// <summary>One entry per matched template sheet, in template order. Missing and ambiguous sheets are in <see cref="Match"/>.</summary>
    public List<ResolvedWorksheet> Sheets { get; } = new();

    /// <summary>Sheet assignment succeeded (see <see cref="WorkbookMatch.IsEligible"/>). Says nothing about columns.</summary>
    public bool IsEligible => Match.IsEligible;

    /// <summary>Workbook-level reasons it can't be used (missing required sheets, ambiguity, truncation).</summary>
    public IReadOnlyList<string> Errors => Match.Errors;

    public IReadOnlyList<string> Warnings => Match.Warnings;

    public IEnumerable<ResolvedWorksheet> ReadableSheets => Sheets.Where(s => s.IsReadable);
    public IEnumerable<ResolvedWorksheet> ProblemSheets => Sheets.Where(s => !s.IsReadable);

    /// <summary>The strictest reading: eligible, and every matched sheet resolves cleanly.</summary>
    public bool IsFullyReadable => IsEligible && Sheets.Count > 0 && Sheets.All(s => s.IsReadable);

    public ResolvedWorksheet? For(ImportTemplateSheetEditDto templateSheet) =>
        Sheets.FirstOrDefault(s => ReferenceEquals(s.TemplateSheet, templateSheet));
}

public static class WorkbookResolver
{
    public static ResolvedWorkbook Resolve(ImportTemplateEditDto template, XLWorkbook workbook) =>
        Resolve(template, WorkbookInspector.Inspect(workbook));

    /// <summary>Takes the inspections so a caller that already inspected the workbook doesn't read it twice.</summary>
    public static ResolvedWorkbook Resolve(ImportTemplateEditDto template, IReadOnlyList<WorksheetInspection> inspections)
    {
        var resolved = new ResolvedWorkbook(WorkbookMatcher.Match(template, inspections));

        foreach (var m in resolved.Match.Matched)
            resolved.Sheets.Add(ResolveSheet(template.ImportType, m.TemplateSheet, m.Worksheet));

        return resolved;
    }

    private static ResolvedWorksheet ResolveSheet(string importType, ImportTemplateSheetEditDto ts, WorksheetInspection sheet)
    {
        var count = Math.Max(1, ts.HeaderRowCount);

        if (sheet.IsEmpty)
        {
            var empty = new ResolvedWorksheet { TemplateSheet = ts, Inspection = sheet, HeaderRowCount = count };
            empty.AddProblem(PlanErrorKind.EmptySheet, "The sheet is empty.");
            return empty;
        }

        int? headerRow;
        string? headerProblem = null;

        if (ts.HeaderRowMode == HeaderRowModes.Fixed)
            headerRow = ts.HeaderRowNumber ?? 1;
        else
        {
            var found = HeaderRowFinder.Detect(ts, sheet);
            headerRow = found is { Accepted: true } ? found.Row : null;
            if (headerRow is null) headerProblem = HeaderRowFinder.DescribeMissing(sheet, found);
        }

        if (headerRow is null)
        {
            var noHeader = new ResolvedWorksheet { TemplateSheet = ts, Inspection = sheet, HeaderRowCount = count };
            noHeader.AddProblem(PlanErrorKind.HeaderRowNotFound, headerProblem!);
            return noHeader;
        }

        var columns = TemplateSheetResolver.ResolveColumns(
            importType, ts, sheet.GetCandidate(headerRow.Value, count).Columns);

        var ok = new ResolvedWorksheet
        {
            TemplateSheet = ts,
            Inspection = sheet,
            HeaderRow = headerRow,
            HeaderRowCount = count,
            Columns = columns
        };
        // MissingRequired (display names) and MissingRequiredMessages are filled together, one entry each.
        for (var i = 0; i < columns.MissingRequiredMessages.Count; i++)
            ok.AddProblem(PlanErrorKind.MissingRequiredHeaders, columns.MissingRequiredMessages[i],
                new { Field = i < columns.MissingRequired.Count ? columns.MissingRequired[i] : null });
        foreach (var message in columns.Ambiguities)
            ok.AddProblem(PlanErrorKind.AmbiguousColumns, message);
        return ok;
    }
}

/// <summary>
/// Finds the header row of an already-inspected worksheet. Same ranking and acceptance gates as the old
/// <c>TemplateSheetResolver.DetectHeaderRow</c> (most fields, then most required, then earliest row), but reads
/// through <see cref="WorksheetInspection"/> so cells are read once.
/// </summary>
public static class HeaderRowFinder
{
    public static HeaderRowMatch? Detect(ImportTemplateSheetEditDto ts, WorksheetInspection sheet)
    {
        if (sheet.IsEmpty) return null;

        var fields = TemplateSheetScorer.Fields(ts);
        var count = Math.Max(1, ts.HeaderRowCount);
        var required = fields.Count(f => f.Required);

        HeaderRowMatch? best = null;
        foreach (var cand in sheet.ScanCandidates(count, Math.Min(sheet.LastRow, TemplateSheetResolver.HeaderScanRows)))
        {
            var present = cand.Keys.ToHashSet();
            var matched = fields.Count(f => f.Aliases.Overlaps(present));
            if (matched == 0) continue;

            var requiredMatched = fields.Count(f => f.Required && f.Aliases.Overlaps(present));
            var accepted = fields.Count > 0
                && matched >= Math.Min(TemplateSheetResolver.MinHeaderFields, fields.Count)
                && (required == 0 || (double)requiredMatched / required >= TemplateSheetResolver.MinRequiredShare);

            if (best is null
                || matched > best.MatchedFields
                || (matched == best.MatchedFields && requiredMatched > best.RequiredMatched))
                best = new HeaderRowMatch(cand.Row, matched, fields.Count, requiredMatched, required, accepted);
        }
        return best;
    }

    public static string DescribeMissing(WorksheetInspection sheet, HeaderRowMatch? best) =>
        best is null
            ? $"Couldn't find the header row on sheet \"{sheet.Name}\": none of the expected headers appear in its first {TemplateSheetResolver.HeaderScanRows} rows."
            : $"Couldn't find the header row on sheet \"{sheet.Name}\": the closest, row {best.Row}, has only {best.MatchedFields} of {best.ConfiguredFields} expected fields.";
}