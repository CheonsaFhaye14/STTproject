using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public enum WorksheetStatus
{
    /// <summary>Assigned to a template sheet.</summary>
    Matched,

    /// <summary>Involved in a tie: equally good assignments exist, so it isn't read.</summary>
    Ambiguous,

    /// <summary>Visible, not assigned, and not involved in any ambiguity.</summary>
    Unclaimed,

    /// <summary>Hidden or very hidden. Reported, never matched.</summary>
    Hidden,

    /// <summary>The assignment was truncated, so nothing can be said about this visible sheet.</summary>
    Undetermined
}

public sealed record SheetMatch(ImportTemplateSheetEditDto TemplateSheet, WorksheetInspection Worksheet, SheetMatchScore? Score);

public sealed record MissingSheet(ImportTemplateSheetEditDto TemplateSheet, string Reason);

/// <summary>Template sheets and worksheets that are mixed up with each other, with the message to show.</summary>
public sealed record SheetAmbiguity(
    IReadOnlyList<ImportTemplateSheetEditDto> TemplateSheets,
    IReadOnlyList<WorksheetInspection> Worksheets,
    string Message);

public sealed record WorksheetReport(WorksheetInspection Worksheet, WorksheetStatus Status, ImportTemplateSheetEditDto? TemplateSheet);

/// <summary>
/// What one template makes of one workbook: which template sheet reads which worksheet, and everything that
/// didn't fit. Template selection, sheet assignment and column resolution are separate steps; this is the
/// outcome of sheet assignment only.
/// </summary>
public sealed class WorkbookMatch
{
    public ImportTemplateEditDto Template { get; init; } = default!;

    /// <summary>Template sheets assigned to a worksheet. Empty when <see cref="IsTruncated"/>.</summary>
    public List<SheetMatch> Matched { get; } = new();

    /// <summary>Visible worksheets nothing claimed. Empty when <see cref="IsTruncated"/>.</summary>
    public List<WorksheetInspection> Unclaimed { get; } = new();

    /// <summary>Hidden worksheets, retained for reporting. Never matched.</summary>
    public List<WorksheetInspection> Hidden { get; } = new();

    /// <summary>Blocks the workbook.</summary>
    public List<MissingSheet> MissingRequired { get; } = new();

    /// <summary>A warning only: the sheet is skipped.</summary>
    public List<MissingSheet> MissingOptional { get; } = new();

    /// <summary>Blocks the workbook. Every affected template sheet and competing worksheet is listed.</summary>
    public List<SheetAmbiguity> Ambiguities { get; } = new();

    /// <summary>One entry per worksheet, in workbook order, with its status.</summary>
    public List<WorksheetReport> Worksheets { get; } = new();

    /// <summary>
    /// Too many equally good assignments to list. No assignment information in this result can be trusted,
    /// so Matched, Unclaimed, the missing lists and Ambiguities are left empty on purpose.
    /// </summary>
    public bool IsTruncated { get; init; }

    /// <summary>Reasons the workbook can't be used. Ready to show.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>The same reasons as <see cref="Errors"/>, with a code and context. Always the same length and order.</summary>
    public List<PlanError> ErrorDetails { get; } = new();

    /// <summary>The only way to add an error, so the text list and the typed list can't drift apart.</summary>
    internal void AddError(PlanErrorKind kind, string message, object? detail = null)
    {
        Errors.Add(message);
        ErrorDetails.Add(new PlanError(kind, message, Detail: detail));
    }

    /// <summary>Things worth knowing that don't stop the import.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Fail-closed. Missing optional sheets, unclaimed worksheets and hidden worksheets never affect it.
    /// Ambiguity of an optional sheet does: that is intentional, same policy as column ambiguity.
    /// </summary>
    public bool IsEligible => !IsTruncated && MissingRequired.Count == 0 && Ambiguities.Count == 0;
}

public static class WorkbookMatcher
{
    public const string TruncatedMessage =
        "Too many equally good ways to match this file's sheets to the template, so none can be trusted. " +
        "Give the template's sheets more distinctive names, positions or headers.";

    public static WorkbookMatch Match(ImportTemplateEditDto template, XLWorkbook workbook) =>
        Match(template, WorkbookInspector.Inspect(workbook));

    public static WorkbookMatch Match(ImportTemplateEditDto template, IReadOnlyList<WorksheetInspection> inspections)
    {
        var assignment = WorkbookAssigner.Assign(template, inspections);
        var match = new WorkbookMatch { Template = template, IsTruncated = assignment.Truncated };

        match.Hidden.AddRange(inspections.Where(i => !i.IsVisible));

        // Fail closed: a truncated result carries no assignment information at all.
        if (assignment.Truncated)
        {
            match.AddError(PlanErrorKind.TruncatedSheetMatch, TruncatedMessage);
            foreach (var ins in inspections)
                match.Worksheets.Add(new WorksheetReport(
                    ins, ins.IsVisible ? WorksheetStatus.Undetermined : WorksheetStatus.Hidden, null));
            return match;
        }

        // Matched and missing template sheets.
        foreach (var a in assignment.Sheets)
        {
            if (a.IsAmbiguous) continue;

            if (a.Worksheet is not null)
            {
                match.Matched.Add(new SheetMatch(a.TemplateSheet, a.Worksheet, a.Score));
                continue;
            }

            var missing = new MissingSheet(a.TemplateSheet, DescribeMissing(a.TemplateSheet, inspections));
            if (a.TemplateSheet.IsRequired)
            {
                match.MissingRequired.Add(missing);
                match.AddError(PlanErrorKind.RequiredSheetMissing,
                    $"Required sheet \"{a.TemplateSheet.SheetLabel}\" was not found in the file: {missing.Reason}.",
                    new { SheetLabel = a.TemplateSheet.SheetLabel, missing.Reason });
            }
            else
            {
                match.MissingOptional.Add(missing);
                match.Warnings.Add($"Optional sheet \"{a.TemplateSheet.SheetLabel}\" was not found, so it will be skipped: {missing.Reason}.");
            }
        }

        AddAmbiguities(match, assignment);
        match.Unclaimed.AddRange(assignment.Unclaimed);

        // Per-worksheet status, in workbook order.
        var matchedBy = match.Matched.ToDictionary(m => m.Worksheet.SheetIndex, m => m.TemplateSheet);
        var ambiguous = match.Ambiguities.SelectMany(a => a.Worksheets).Select(w => w.SheetIndex).ToHashSet();

        foreach (var ins in inspections)
        {
            if (!ins.IsVisible) match.Worksheets.Add(new WorksheetReport(ins, WorksheetStatus.Hidden, null));
            else if (matchedBy.TryGetValue(ins.SheetIndex, out var ts)) match.Worksheets.Add(new WorksheetReport(ins, WorksheetStatus.Matched, ts));
            else if (ambiguous.Contains(ins.SheetIndex)) match.Worksheets.Add(new WorksheetReport(ins, WorksheetStatus.Ambiguous, null));
            else match.Worksheets.Add(new WorksheetReport(ins, WorksheetStatus.Unclaimed, null));
        }

        return match;
    }

    // Template sheets that share candidate worksheets are one ambiguity, so the user sees the whole tangle once.
    private static void AddAmbiguities(WorkbookMatch match, WorkbookAssignment assignment)
    {
        var parent = new Dictionary<string, string>();
        string Find(string x)
        {
            if (!parent.ContainsKey(x)) parent[x] = x;
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(string a, string b) { var ra = Find(a); var rb = Find(b); parent[ra] = rb; }

        for (var i = 0; i < assignment.Sheets.Count; i++)
            if (assignment.Sheets[i].IsAmbiguous)
                foreach (var alt in assignment.Sheets[i].Alternatives)
                    Union($"T{i}", $"W{alt.SheetIndex}");

        var groups = Enumerable.Range(0, assignment.Sheets.Count)
            .Where(i => assignment.Sheets[i].IsAmbiguous)
            .GroupBy(i => Find($"T{i}"));

        foreach (var g in groups)
        {
            var sheets = g.Select(i => assignment.Sheets[i].TemplateSheet).ToList();
            var worksheets = g.SelectMany(i => assignment.Sheets[i].Alternatives)
                .GroupBy(w => w.SheetIndex).Select(x => x.First()).OrderBy(w => w.SheetIndex).ToList();

            var amb = new SheetAmbiguity(sheets, worksheets, AmbiguityMessage(sheets, worksheets));
            match.Ambiguities.Add(amb);
            match.AddError(PlanErrorKind.AmbiguousSheet, amb.Message,
                new { TemplateSheets = sheets.Select(s => s.SheetLabel).ToList(), Worksheets = worksheets.Select(w => w.Name).ToList() });
        }
    }

    private static string AmbiguityMessage(
        IReadOnlyList<ImportTemplateSheetEditDto> templates, IReadOnlyList<WorksheetInspection> worksheets)
    {
        var t = string.Join(", ", templates.Select(x => $"\"{x.SheetLabel}\""));
        var w = string.Join(", ", worksheets.Select(x => $"\"{x.Name}\""));

        if (templates.Count == 1)
            return $"Sheet {t} fits {w} equally well, so it can't tell which one to read.";
        if (worksheets.Count == 1)
            return $"Sheets {t} all fit {w} equally well, so it can't tell which of them should read it.";
        return $"Sheets {t} can't be told apart: each fits {w} equally well, so it can't tell which is which.";
    }

    // Why a template sheet got no worksheet, as a fragment that follows "was not found in the file: ".
    private static string DescribeMissing(ImportTemplateSheetEditDto ts, IReadOnlyList<WorksheetInspection> all)
    {
        var visible = all.Where(i => i.IsVisible).ToList();
        if (visible.Count == 0) return "the file has no visible sheets";

        var scored = visible.Select(w => (Sheet: w, Score: TemplateSheetScorer.Score(ts, w))).ToList();

        // Something fit, but another template sheet took it.
        var fitted = scored.Where(x => x.Score.Eligible).Select(x => $"\"{x.Sheet.Name}\"").ToList();
        if (fitted.Count > 0)
            return $"{string.Join(", ", fitted)} fit{(fitted.Count == 1 ? "s" : "")} but already used by other template sheets";

        var value = ts.SheetMatchValue?.Trim() ?? "";
        var text = ts.SheetMatchMode switch
        {
            SheetMatchModes.Exact => $"no sheet is named \"{value}\"",
            SheetMatchModes.Contains => $"no sheet name contains \"{value}\"",
            SheetMatchModes.Position => $"there is no visible sheet number {value}",
            SheetMatchModes.Any => "the file has no readable sheet",
            SheetMatchModes.Headers => ClosestHeaders(scored),
            _ => "no sheet matched"
        };

        var hidden = all.FirstOrDefault(i => !i.IsVisible && NameFits(ts, i, value));
        return hidden is null ? text : $"{text}; a hidden sheet \"{hidden.Name}\" matches, but hidden sheets are never matched";
    }

    private static bool NameFits(ImportTemplateSheetEditDto ts, WorksheetInspection sheet, string value) =>
        ts.SheetMatchMode switch
        {
            SheetMatchModes.Exact => string.Equals(sheet.Name, value, StringComparison.OrdinalIgnoreCase),
            SheetMatchModes.Contains => value.Length > 0 && sheet.Name.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static string ClosestHeaders(List<(WorksheetInspection Sheet, SheetMatchScore Score)> scored)
    {
        var closest = scored
            .Where(x => x.Score.MatchedFields > 0)
            .OrderByDescending(x => x.Score.MatchedFields)
            .ThenByDescending(x => x.Score.RequiredMatched)
            .FirstOrDefault();

        if (closest.Sheet is null) return "no sheet has any of these headers";

        var s = closest.Score;
        return $"no sheet has enough of these headers (closest: \"{closest.Sheet.Name}\", " +
               $"{s.MatchedFields} of {s.ConfiguredFields} fields, {s.RequiredMatched} of {s.RequiredFields} required)";
    }
}