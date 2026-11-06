using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Models;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>
/// One row as the importer sees it: a physical row plus the template's field mapping for its sheet.
/// Importers read fields through <see cref="Cell"/> and <see cref="Rule"/>, never by column number, so that a later
/// header/detail assembly stage can supply some fields from another sheet's row without touching the importer.
/// </summary>
public sealed class LogicalRow
{
    private readonly IReadOnlyDictionary<string, int> _columns;
    private readonly IReadOnlyDictionary<string, ImportTemplateColumnEditDto> _rules;

    internal LogicalRow(ResolvedWorksheet sheet, IXLRow row)
    {
        Sheet = sheet;
        Row = row;
        _columns = sheet.Columns!.Headers;
        _rules = sheet.Columns.Columns;
    }

    public ResolvedWorksheet Sheet { get; }

    /// <summary>The physical row this logical row was read from.</summary>
    public IXLRow Row { get; }

    /// <summary>Worksheet position in the original workbook (1-based, hidden sheets counted).</summary>
    public int SheetIndex => Sheet.Inspection.SheetIndex;
    public int RowNumber => Row.RowNumber();

    /// <summary>Where this row came from. Row numbers alone collide across sheets.</summary>
    public SourceRowKey Source => new(SheetIndex, RowNumber);

    /// <summary>The fields this row can supply, in column order of its sheet's resolution.</summary>
    public IEnumerable<string> FieldKeys => _columns.Keys;

    public bool Has(string field) => _columns.ContainsKey(field);

    /// <summary>The cell a field is read from, or null when the field isn't mapped.</summary>
    public IXLCell? Cell(string field) => _columns.TryGetValue(field, out var column) ? Row.Cell(column) : null;

    /// <summary>The template's "how to read it" rule for a field, or null when the field isn't mapped.</summary>
    public ImportTemplateColumnEditDto? Rule(string field) => _rules.TryGetValue(field, out var rule) ? rule : null;
}

/// <summary>
/// The logical rows of a workbook, in a fixed order: sheets in workbook order, rows ascending.
/// One shared row budget covers every sheet. Enumerating again starts a fresh, identical pass, which is what lets
/// a pre-scan and the real read see the same rows.
/// </summary>
public sealed class WorkbookRows
{
    public WorkbookRows(IReadOnlyList<ResolvedWorksheet> sheets, int maxRows)
    {
        Sheets = sheets;
        LineSheets = sheets.Where(WorkbookReader.IsLineBearing).ToList();
        MaxRows = maxRows;
    }

    /// <summary>Every sheet given, in workbook order, header-only sheets included (header/detail assembly will need them).</summary>
    public IReadOnlyList<ResolvedWorksheet> Sheets { get; }

    /// <summary>The sheets whose rows are read: the ones that carry line data.</summary>
    public IReadOnlyList<ResolvedWorksheet> LineSheets { get; }

    /// <summary>
    /// Data rows to examine across all line-bearing sheets, blank rows included: the same unit the single-sheet
    /// importer always used. A header-only sheet never uses any of it. It limits the rows read, not the size of
    /// any worksheet.
    /// </summary>
    public int MaxRows { get; }

    public IEnumerable<LogicalRow> Rows()
    {
        var budget = (long)MaxRows;

        foreach (var sheet in LineSheets)
        {
            var worksheet = sheet.Worksheet;
            var lastRow = sheet.Inspection.LastRow;

            for (var rowNumber = sheet.FirstDataRow; rowNumber <= lastRow; rowNumber++)
            {
                if (budget <= 0) yield break;
                budget--;

                yield return new LogicalRow(sheet, worksheet.Row(rowNumber));
            }
        }
    }
}

/// <summary>What the reader decided about a resolved workbook: either rows to read, or the reasons it can't be read.</summary>
public sealed class WorkbookReadPlan
{
    /// <summary>Reasons the workbook can't be read, ready to show. Empty when <see cref="Rows"/> is set.</summary>
    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>The same reasons as <see cref="Errors"/>, with a code and context. Always the same length and order.</summary>
    public List<PlanError> ErrorDetails { get; } = new();

    internal void AddError(PlanErrorKind kind, string message, int? sheetIndex = null, string? sheetName = null, object? detail = null)
    {
        Errors.Add(message);
        ErrorDetails.Add(new PlanError(kind, message, sheetIndex, sheetName, detail));
    }

    /// <summary>
    /// The same sheet-level shortfalls the <see cref="Errors"/> text describes, as data, for the results UI.
    /// Presentation only: <see cref="Errors"/> still carries every message.
    /// </summary>
    public List<SheetShortfall> SheetShortfalls { get; } = new();

    /// <summary>The sheets that resolved cleanly, in workbook order. Set even when the plan has errors.</summary>
    public IReadOnlyList<ResolvedWorksheet> Sheets { get; init; } = Array.Empty<ResolvedWorksheet>();

    /// <summary>Every field mapped on any readable sheet.</summary>
    public IReadOnlyList<string> MappedFields { get; init; } = Array.Empty<string>();

    /// <summary>Null when the workbook can't be read.</summary>
    public WorkbookRows? Rows { get; set; }
}

public static class WorkbookReader
{
    /// <summary>Fields that only a line row can supply. A sheet mapping any of them is line-bearing.</summary>
    public static readonly IReadOnlySet<string> LineFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SkuCode", "ItemName", "Quantity", "CaseQuantity", "DozenQuantity", "PieceQuantity", "InBoxQuantity"
    };

    public static bool IsLineBearing(ResolvedWorksheet sheet) =>
        sheet.Columns is not null && sheet.Columns.Headers.Keys.Any(LineFields.Contains);

    public const string NoReadableSheetMessage = "None of the template's sheets could be read from this file.";

    public static string MissingColumnsMessage(IEnumerable<string> messages) =>
        "Missing required columns: " + string.Join(" ", messages);

    /// <summary>
    /// Decides whether a resolved workbook can be read, under the strict policy: the sheet assignment must be
    /// eligible and every matched sheet must resolve cleanly (Step 6 will relax this for skippable sheets).
    /// Required fields are checked across the whole workbook, with the registry as the single definition.
    /// Until header/detail assembly exists, every sheet must also hold all required fields on its own, so a
    /// workbook that splits invoices across sheets is refused instead of half-read.
    /// </summary>
    public static WorkbookReadPlan Plan(ResolvedWorkbook resolved, string importType, int maxRows)
    {
        var readable = resolved.ReadableSheets.OrderBy(s => s.Inspection.SheetIndex).ToList();
        var pooled = readable
            .SelectMany(s => s.Columns!.Headers.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var plan = new WorkbookReadPlan { Sheets = readable, MappedFields = pooled };
        plan.Warnings.AddRange(resolved.Warnings);

        plan.Errors.AddRange(resolved.Errors);
        plan.ErrorDetails.AddRange(resolved.Match.ErrorDetails);
        foreach (var sheet in resolved.ProblemSheets.OrderBy(s => s.Inspection.SheetIndex))
        {
            plan.Errors.AddRange(sheet.ProblemMessages);
            plan.ErrorDetails.AddRange(sheet.ProblemErrors);
        }

        if (plan.Errors.Count == 0 && resolved.Sheets.Count == 0)
            plan.AddError(PlanErrorKind.NoReadableSheet, NoReadableSheetMessage);
        if (plan.Errors.Count > 0) return plan;

        var def = ImportFieldRegistry.Get(importType);

        // Required fields that no readable sheet maps at all.
        var missing = ImportFieldRegistry.MissingRequired(importType, pooled);
        if (missing.Count > 0)
        {
            plan.AddError(PlanErrorKind.MissingRequiredHeaders,
                def is null
                    ? MissingColumnsMessage(missing)
                    : DescribeWorkbookShortfall(def, readable, pooled, missing),
                detail: new { Scope = "Workbook", SheetsRead = readable.Select(s => s.Inspection.Name).ToList(), Missing = missing });
            return plan;
        }

        // Every required field is mapped somewhere, but a sheet doesn't hold all of them itself.
        foreach (var sheet in readable)
        {
            var own = ImportFieldRegistry.MissingRequired(importType, sheet.Columns!.Headers.Keys);
            if (own.Count == 0) continue;

            var message = def is null
                ? $"Sheet \"{sheet.Inspection.Name}\" doesn't hold every required column on its own ({string.Join(" ", own)})."
                : DescribeSheetShortfall(def, sheet, readable, own);
            var shortfall = def is null ? null : BuildSheetShortfall(def, sheet, readable, own, message);
            object detail = shortfall is null
                ? (object)new { Scope = "Sheet", Missing = own }
                : new { Scope = "Sheet", shortfall.HeaderRow, shortfall.MissingFields };
            plan.AddError(PlanErrorKind.MissingRequiredHeaders, message,
                sheet.Inspection.SheetIndex, sheet.Inspection.Name, detail);
            if (shortfall is not null)
                plan.SheetShortfalls.Add(shortfall);
        }
        if (plan.Errors.Count > 0) return plan;

        plan.Rows = new WorkbookRows(readable, maxRows);
        return plan;
    }

    /// <summary>
    /// The facts <see cref="DescribeSheetShortfall"/> words as a sentence, kept as data. Reads the same inputs and
    /// changes nothing about the message text.
    /// </summary>
    private static SheetShortfall BuildSheetShortfall(
        ImportTypeDefinition def, ResolvedWorksheet sheet, IReadOnlyList<ResolvedWorksheet> all,
        IReadOnlyList<string> messages, string message)
    {
        var mapped = new HashSet<string>(sheet.Columns!.Headers.Keys, StringComparer.OrdinalIgnoreCase);
        var groups = def.RequiredGroups.Where(g => !g.Any(mapped.Contains)).ToList();
        var configured = sheet.TemplateSheet.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey))
            .ToLookup(c => c.FieldKey!, StringComparer.OrdinalIgnoreCase);

        var fields = new List<MissingField>();
        foreach (var group in groups)
        {
            var configuredKeys = group.Where(configured.Contains).ToList();
            var expected = configuredKeys
                .SelectMany(k => configured[k])
                .SelectMany(TemplateSheetResolver.HeaderTexts)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var elsewhere = all
                .Where(s => !ReferenceEquals(s, sheet) && group.Any(k => s.Columns!.Headers.ContainsKey(k)))
                .Select(s => s.Inspection.Name)
                .ToList();

            fields.Add(new MissingField(
                GroupLabel(def, group),
                configuredKeys.Count == 0 ? MissingFieldReason.NotMapped : MissingFieldReason.HeaderNotFound,
                expected,
                elsewhere));
        }

        return new SheetShortfall(
            sheet.Inspection.SheetIndex,
            sheet.Inspection.Name,
            sheet.HeaderRow,
            fields,
            messages.Skip(groups.Count).ToList(),
            message);
    }

    private static string DisplayOf(ImportTypeDefinition def, string key) =>
        def.Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))?.Display ?? key;

    private static string GroupLabel(ImportTypeDefinition def, string[] group) =>
        group.Length == 1
            ? DisplayOf(def, group[0])
            : "one of " + string.Join(" / ", group.Select(k => DisplayOf(def, k)));

    /// <summary>No readable sheet maps these required fields. Names the fields and the sheets that were read.</summary>
    private static string DescribeWorkbookShortfall(
        ImportTypeDefinition def, IReadOnlyList<ResolvedWorksheet> sheets, IEnumerable<string> pooled, IReadOnlyList<string> messages)
    {
        var mapped = new HashSet<string>(pooled, StringComparer.OrdinalIgnoreCase);
        var groups = def.RequiredGroups.Where(g => !g.Any(mapped.Contains)).ToList();

        var parts = new List<string>();
        if (groups.Count > 0)
        {
            var names = string.Join(", ", sheets.Select(s => $"\"{s.Inspection.Name}\""));
            parts.Add($"Required fields not mapped on any sheet that was read ({names}): " +
                      string.Join(", ", groups.Select(g => GroupLabel(def, g))) + ".");
        }
        // Dependency messages (e.g. Unit of Measure with Quantity) come after the group messages.
        parts.AddRange(messages.Skip(groups.Count));
        parts.Add("Check the column mappings and header rows in Template Management.");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// A sheet is missing required fields that other sheets do map. Says, per field, whether the template doesn't map it
    /// for this sheet or its header wasn't found, and where it is mapped instead.
    /// </summary>
    private static string DescribeSheetShortfall(
        ImportTypeDefinition def, ResolvedWorksheet sheet, IReadOnlyList<ResolvedWorksheet> all, IReadOnlyList<string> messages)
    {
        var mapped = new HashSet<string>(sheet.Columns!.Headers.Keys, StringComparer.OrdinalIgnoreCase);
        var groups = def.RequiredGroups.Where(g => !g.Any(mapped.Contains)).ToList();
        var configured = sheet.TemplateSheet.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey))
            .ToLookup(c => c.FieldKey!, StringComparer.OrdinalIgnoreCase);

        var fields = new List<string>();
        var notConfigured = false;
        var headerMismatch = false;
        foreach (var group in groups)
        {
            var configuredKeys = group.Where(configured.Contains).ToList();
            string why;
            if (configuredKeys.Count == 0)
            {
                why = "the template doesn't map it for this sheet";
                notConfigured = true;
            }
            else
            {
                var headers = configuredKeys
                    .SelectMany(k => configured[k])
                    .SelectMany(TemplateSheetResolver.HeaderTexts)
                    .Select(h => $"\"{h}\"")
                    .Distinct()
                    .ToList();
                why = $"header {string.Join(" / ", headers)} wasn't found in header row {sheet.HeaderRow}";
                headerMismatch = true;
            }

            var elsewhere = all
                .Where(s => !ReferenceEquals(s, sheet) && group.Any(k => s.Columns!.Headers.ContainsKey(k)))
                .Select(s => $"\"{s.Inspection.Name}\"")
                .ToList();
            if (elsewhere.Count > 0) why += $"; mapped on {string.Join(", ", elsewhere)}";

            fields.Add($"{GroupLabel(def, group)} ({why})");
        }

        // Lead with the failure that actually happened. Header mismatch alone is a header problem;
        // anything involving an unmapped field is the "can't provide all required fields" case.
        var lead = headerMismatch && !notConfigured
            ? $"Sheet \"{sheet.Inspection.Name}\" is missing required headers in row {sheet.HeaderRow}."
            : $"Sheet \"{sheet.Inspection.Name}\" can't provide all required fields on its own.";
        var parts = new List<string> { lead };
        if (fields.Count > 0) parts.Add("Missing: " + string.Join("; ", fields) + ".");
        parts.AddRange(messages.Skip(groups.Count));
        if (notConfigured)
        {
            parts.Add("Each worksheet must independently map all required fields. Combining fields across worksheets is not supported.");
            parts.Add("Add the missing columns to this sheet's mapping in Template Management, or set it to Ignore if it shouldn't be read.");
        }
        if (headerMismatch)
        {
            // TemplateSheetResolver.Norm ignores case and collapses spaces and . # / - , : ( ) ; any other character still has to match.
            parts.Add($"Compare the headers in row {sheet.HeaderRow} with the expected headers above. " +
                      "Case, spaces and the characters . # / - , : ( ) are ignored when matching headers. " +
                      "Also check that the template sheet is assigned to the correct worksheet.");
        }
        if (!notConfigured && !headerMismatch)
            parts.Add("Check this sheet's column mapping in Template Management.");

        return string.Join(" ", parts);
    }
}

public enum MissingFieldReason
{
    /// <summary>The template doesn't map this field for the sheet.</summary>
    NotMapped,
    /// <summary>The template maps it, but its header wasn't found in the header row.</summary>
    HeaderNotFound
}

/// <summary>One required field (or one-of group) a sheet can't supply.</summary>
public sealed record MissingField(
    string Label,
    MissingFieldReason Reason,
    IReadOnlyList<string> ExpectedHeaders,
    IReadOnlyList<string> MappedOnSheets);

/// <summary>A sheet that can't provide every required field on its own, as data. <see cref="Message"/> is the original sentence.</summary>
public sealed record SheetShortfall(
    int SheetIndex,
    string SheetName,
    int? HeaderRow,
    IReadOnlyList<MissingField> MissingFields,
    IReadOnlyList<string> OtherMessages,
    string Message);