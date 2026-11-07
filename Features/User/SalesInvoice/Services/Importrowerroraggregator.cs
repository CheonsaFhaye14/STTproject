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
///
/// Whole-invoice evidence: InvoiceInconsistent and InvoiceHeaderInvalid are judged on an invoice as a whole but are reported on one
/// row. Their evidence also lists every other row of the same invoice number (even rows that grouping put in a different group),
/// marked IsReportedRow = false. Those context rows are evidence only: they never change Occurrences, SourceRowNumber, the
/// error's grouping or its fingerprint, and the reported rows are always stored first.
/// </summary>
public static class ImportRowErrorAggregator
{
    /// <summary>
    /// Code on an issue for a duplicate invoice number. It is an expected business-rule rejection: it is shown in the
    /// results, but an administrator has nothing to fix, so it is never logged.
    /// </summary>
    public const string InvoiceDuplicateCode = "InvoiceDuplicate";

    /// <summary>Errors that are judged on a whole invoice. Their evidence also lists the invoice's other rows (see class summary).</summary>
    private static readonly HashSet<string> WholeInvoiceCodes = new(StringComparer.Ordinal)
    {
        ImportErrorTypes.InvoiceInconsistent,
        ImportErrorTypes.InvoiceHeaderInvalid,
        // Not judged on the whole invoice, but its evidence lists the invoice's other rows (with their real salesman) for comparison.
        ImportErrorTypes.SalesmanMissing
    };

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
        Dictionary<string, string>? RowData,
        // True for a row the importer actually reported; false for a row listed only because it belongs to the same invoice.
        bool IsReportedRow = true,
        // Structured facts for the details page (customer code/name, item code/name, uploaded unit). Evidence only.
        IReadOnlyDictionary<string, string?>? Details = null);

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
        public readonly HashSet<(int Sheet, int Row)> AffectedKeys = new();
        public bool Truncated;
        public bool ContextTruncated;
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
        var wholeInvoice = new List<(Group Group, ImportSalesInvoiceIssue Issue)>();

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
                // Recorded before the capacity check, so a reported row that does not fit is still never re-added as a context row.
                group.AffectedKeys.Add((issue.SheetIndex, issue.RowNumber));

                if (group.AffectedRows.Count < ImportLogService.MaxAffectedRows)
                {
                    group.AffectedRows.Add(new AffectedRow(
                        result.Sheets.TryGetValue(issue.SheetIndex, out var snapSheet) ? snapSheet.Name : null,
                        issue.SheetIndex,
                        issue.RowNumber,
                        issue.InvoiceNumber,
                        issue.ColumnName,
                        classified ? Clip(issue.Value) : null,
                        SnapshotRow(result, issue.SheetIndex, issue.RowNumber),
                        Details: issue.Details));
                }
                else
                    group.Truncated = true;
            }

            if (classified && WholeInvoiceCodes.Contains(code))
                wholeInvoice.Add((group, issue));
        }

        // Context rows are added only after every reported row of every error is stored, so they can never take the room a reported
        // row needs. They are not occurrences: group.Rows (and so Occurrences and SourceRowNumber) is not touched.
        if (wholeInvoice.Count > 0)
        {
            var rowsByInvoice = IndexRowsByInvoice(result);
            foreach (var (group, issue) in wholeInvoice)
                AddInvoiceContextRows(result, group, issue, rowsByInvoice);
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
                    // Reported rows are complete, but the invoice's other rows did not all fit.
                    ContextTruncated = g.ContextTruncated ? true : (bool?)null,
                    AffectedRows = g.AffectedRows
                }))
            .ToList();
    }

    /// <summary>
    /// Every row of the file by its normalized invoice number, from the union of the parsed rows, the rows that had errors and the
    /// raw rows (which also holds rows that were skipped before parsing finished, such as zero-quantity rows). Parsed and errored
    /// rows carry the invoice code after the template's rules; raw rows carry the cell text, so a rule that rewrites the invoice
    /// code can keep a raw-only row from matching.
    /// </summary>
    private static Dictionary<string, HashSet<(int Sheet, int Row)>> IndexRowsByInvoice(ImportSalesInvoiceResult result)
    {
        var byInvoice = new Dictionary<string, HashSet<(int Sheet, int Row)>>(StringComparer.Ordinal);

        void Add(string? invoiceCode, int sheetIndex, int rowNumber)
        {
            var invoice = Normalize(invoiceCode);
            if (invoice.Length == 0 || rowNumber <= 0) return;

            if (!byInvoice.TryGetValue(invoice, out var set))
                byInvoice[invoice] = set = new HashSet<(int Sheet, int Row)>();
            set.Add((sheetIndex, rowNumber));
        }

        foreach (var row in result.Rows) Add(row.InvoiceCode, row.SheetIndex, row.RowNumber);
        foreach (var errored in result.ErroredRows) Add(errored.InvoiceCode, errored.SheetIndex, errored.RowNumber);
        foreach (var (key, values) in result.RawValuesByRow)
        {
            if (key.IsFileLevel) continue;
            values.TryGetValue("InvoiceCode", out var rawCode);
            Add(rawCode, key.SheetIndex, key.RowNumber);
        }

        return byInvoice;
    }

    /// <summary>Adds every other row of the reported invoice to the group's evidence as a context row (IsReportedRow = false).</summary>
    private static void AddInvoiceContextRows(
        ImportSalesInvoiceResult result,
        Group group,
        ImportSalesInvoiceIssue issue,
        Dictionary<string, HashSet<(int Sheet, int Row)>> rowsByInvoice)
    {
        if (!rowsByInvoice.TryGetValue(Normalize(issue.InvoiceNumber), out var rows)) return;

        var contextField = group.Code == ImportErrorTypes.SalesmanMissing && !string.IsNullOrWhiteSpace(group.Field) ? group.Field : null;

        foreach (var (sheetIndex, rowNumber) in rows.OrderBy(r => r.Sheet).ThenBy(r => r.Row))
        {
            if (group.AffectedKeys.Contains((sheetIndex, rowNumber))) continue;   // a reported row, or already listed

            if (group.AffectedRows.Count >= ImportLogService.MaxAffectedRows)
            {
                group.ContextTruncated = true;
                return;
            }

            group.AffectedKeys.Add((sheetIndex, rowNumber));
            group.AffectedRows.Add(new AffectedRow(
                result.Sheets.TryGetValue(sheetIndex, out var sheet) ? sheet.Name : null,
                sheetIndex,
                rowNumber,
                issue.InvoiceNumber,
                // The row's real value of the problem field (empty when it is blank), so the page can show it next to the error's own rows.
                // Null when the error has no single field, or the importer did not keep the row's values.
                contextField,
                contextField is null ? null : ContextFieldValue(result, sheetIndex, rowNumber, contextField),
                SnapshotRow(result, sheetIndex, rowNumber),
                IsReportedRow: false));
        }
    }

    // The importer's own value for one template field of one row ("" when the cell is blank), or null when it was not kept.
    private static string? ContextFieldValue(ImportSalesInvoiceResult result, int sheetIndex, int rowNumber, string field)
    {
        if (!result.RawValuesByRow.TryGetValue(new(sheetIndex, rowNumber), out var values)) return null;
        foreach (var (name, value) in values)
            if (string.Equals(name, field, StringComparison.OrdinalIgnoreCase))
                return Clip((value ?? string.Empty).Trim()) ?? string.Empty;
        return null;
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