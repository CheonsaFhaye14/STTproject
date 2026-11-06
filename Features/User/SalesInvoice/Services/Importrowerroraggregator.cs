using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Services;

namespace STTproject.Features.User.SalesInvoice.Services;

/// <summary>
/// Turns the issues found while preparing a file into the few ImportError records worth saving. Pure: no database,
/// no clock, so it can be tested on its own.
///
/// One record per distinct error: the same ErrorType + field + normalized value + context. The same SKU missing on
/// 15 rows is one record that says so, while two different SKUs stay two records because they need different fixes.
/// An occurrence is a distinct (sheet, row), so a problem noticed by two validation layers on one row counts once.
/// Hold notes and duplicate-invoice rejections are skipped (the first only restates another row's error, the second is expected). There is no cap on distinct errors: every distinct
/// problem is returned so an administrator can review each one. What stays bounded is the detail kept per error
/// (value variants, and at most ImportLogService.MaxAffectedRows row snapshots per batch), never the number of errors.
///
/// Two payloads per error: Data (a small summary, stored on the ImportError) and BatchData (this batch's evidence, stored on
/// ImportErrorBatch): every affected row with ALL its original columns, taken from the sheet's RawHeaders / RawRows.
/// </summary>
public static class ImportRowErrorAggregator
{
    /// <summary>
    /// Code on an issue for a duplicate invoice number. It is an expected business-rule rejection: it is shown in the
    /// results, but an administrator has nothing to fix, so it is never logged.
    /// </summary>
    public const string InvoiceDuplicateCode = "InvoiceDuplicate";

    public const int MaxSampleRows = 20;
    public const int MaxVariants = 5;

    private const int MaxValueLength = 300;
    private const int MaxCellLength = 2000;   // one spreadsheet cell; keeps a stray giant cell from bloating the evidence

    private readonly record struct GroupKey(string Code, string Field, string Value, string Context);

    /// <summary>One affected Excel row: where it is, what was wrong, and the whole row as it was in the file.</summary>
    internal sealed record AffectedRow(
        string? SheetName,
        int SheetIndex,
        int RowNumber,
        string? InvoiceNumber,
        string? ProblemField,
        string? ProblemValue,
        Dictionary<string, string>? RowData);

    private sealed class Group
    {
        public string Code = string.Empty;
        public string? Field;
        public string? Value;
        public string? Context;
        public string Message = string.Empty;
        public int? SuggestionCount;
        public int FirstRow;
        public readonly List<string> Variants = new();
        public readonly HashSet<(int Sheet, int Row)> Rows = new();
        public readonly HashSet<string> Invoices = new(StringComparer.Ordinal);
        public readonly List<object> Samples = new();
        public readonly List<AffectedRow> AffectedRows = new();
        public bool Truncated;
    }

    public static List<ImportErrorEntry> Aggregate(ImportSalesInvoiceResult result, int subDistributorId)
        => Aggregate(result, result.Issues, subDistributorId);

    /// <summary>
    /// Aggregates exactly the given issues. <paramref name="result"/> is only used to look up sheet names. Used at commit time,
    /// where only that commit's own failures may be logged: result.Issues still holds the preparation errors already saved.
    /// </summary>
    public static List<ImportErrorEntry> Aggregate(
        ImportSalesInvoiceResult result, IEnumerable<ImportSalesInvoiceIssue> issues, int subDistributorId)
    {
        var groups = new List<Group>();                       // first-seen order, which is also the order they are saved in
        var index = new Dictionary<GroupKey, Group>();

        foreach (var issue in issues)
        {
            if (issue.IsHoldNote || issue.Code == InvoiceDuplicateCode) continue;

            var classified = !string.IsNullOrWhiteSpace(issue.Code);
            var code = classified ? issue.Code! : ImportErrorTypes.UnclassifiedError;
            // Unclassified issues are told apart by their message, since there is no value to go by.
            var value = classified ? issue.Value : issue.Header;

            var key = new GroupKey(code, Normalize(issue.ColumnName), Normalize(value), Normalize(issue.Context));
            if (!index.TryGetValue(key, out var group))
            {
                group = new Group
                {
                    Code = code,
                    Field = issue.ColumnName,
                    Value = Clip(value),
                    Context = Clip(issue.Context),
                    Message = issue.Header,
                    FirstRow = issue.RowNumber
                };
                index[key] = group;
                groups.Add(group);
            }

            group.SuggestionCount ??= issue.SuggestionCount;

            if (!string.IsNullOrWhiteSpace(value) && group.Variants.Count < MaxVariants && !group.Variants.Contains(value))
                group.Variants.Add(Clip(value)!);

            // The same row reported twice is one occurrence.
            if (!group.Rows.Add((issue.SheetIndex, issue.RowNumber))) continue;

            var invoice = Normalize(issue.InvoiceNumber);
            if (invoice.Length > 0) group.Invoices.Add(invoice);

            if (group.Samples.Count < MaxSampleRows)
                group.Samples.Add(new
                {
                    SheetName = result.Sheets.TryGetValue(issue.SheetIndex, out var sheet) ? sheet.Name : null,
                    issue.SheetIndex,
                    issue.RowNumber,
                    InvoiceCode = issue.InvoiceNumber
                });

            // The complete evidence: this row with every original column. A row seen twice for the same error was skipped above.
            if (issue.RowNumber > 0)
            {
                if (group.AffectedRows.Count < ImportLogService.MaxAffectedRows)
                    group.AffectedRows.Add(new AffectedRow(
                        result.Sheets.TryGetValue(issue.SheetIndex, out var snapSheet) ? snapSheet.Name : null,
                        issue.SheetIndex,
                        issue.RowNumber,
                        issue.InvoiceNumber,
                        issue.ColumnName,
                        classified ? Clip(issue.Value) : null,
                        SnapshotRow(result, issue.SheetIndex, issue.RowNumber)));
                else
                    group.Truncated = true;
            }
        }

        return groups
            .Select(g => new ImportErrorEntry(
                g.Code,
                g.Message,
                g.Field,
                // A single occurrence keeps its row; an aggregated error has none (the rows are in the data).
                g.Rows.Count == 1 && g.FirstRow > 0 ? g.FirstRow : null,
                new
                {
                    g.Value,
                    Variants = g.Variants.Count > 1 ? g.Variants : null,
                    g.Context,
                    SubDistributorId = subDistributorId,
                    Occurrences = g.Rows.Count,
                    AffectedInvoices = g.Invoices.Count,
                    g.SuggestionCount,
                    SampleRows = g.Samples
                },
                // What identifies this error across batches (see ImportErrorFingerprint) and how many rows it covers in this one.
                new ErrorIdentity(g.Value, g.Context),
                g.Rows.Count,
                // This batch's evidence for ImportErrorBatch.DataJson: every affected row with all its original columns.
                BatchData: new
                {
                    Occurrences = g.Rows.Count,
                    AffectedInvoices = g.Invoices.OrderBy(i => i, StringComparer.Ordinal).ToList(),
                    Truncated = g.Truncated ? true : (bool?)null,
                    AffectedRows = g.AffectedRows
                }))
            .ToList();
    }

    /// <summary>Trim, collapse inner whitespace, upper-case: how values are compared. The original is kept in the data.</summary>
    internal static string Normalize(string? text) => ImportErrorFingerprint.Normalize(text);

    /// <summary>
    /// The row exactly as it was in the file: header text to cell text, in column order. Columns with no header and no value are
    /// left out; a headerless column with a value is named "Column C"; repeated headers get " (2)", " (3)".
    /// Null when the sheet or row was not captured.
    /// </summary>
    private static Dictionary<string, string>? SnapshotRow(ImportSalesInvoiceResult result, int sheetIndex, int rowNumber)
    {
        if (!result.Sheets.TryGetValue(sheetIndex, out var sheet)) return null;
        if (!sheet.RawRows.TryGetValue(rowNumber, out var cells) || cells is null) return null;

        var headers = sheet.RawHeaders;
        var data = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < cells.Length; i++)
        {
            var value = cells[i] ?? string.Empty;
            var header = i < headers.Count ? (headers[i] ?? string.Empty).Trim() : string.Empty;
            if (header.Length == 0 && value.Length == 0) continue;

            var name = header.Length > 0 ? header : $"Column {ColumnLetter(i + 1)}";
            var unique = name;
            for (var n = 2; data.ContainsKey(unique); n++) unique = $"{name} ({n})";

            data[unique] = value.Length <= MaxCellLength ? value : value[..(MaxCellLength - 3)] + "...";
        }

        return data;
    }

    private static string ColumnLetter(int column)
    {
        var letters = string.Empty;
        for (; column > 0; column = (column - 1) / 26)
            letters = (char)('A' + (column - 1) % 26) + letters;
        return letters;
    }

    private static string? Clip(string? text) =>
        text is null ? null : text.Length <= MaxValueLength ? text : text[..(MaxValueLength - 3)] + "...";
}