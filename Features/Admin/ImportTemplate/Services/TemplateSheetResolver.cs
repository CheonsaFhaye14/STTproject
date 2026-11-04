using System.Text.RegularExpressions;
using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using System.Globalization;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public sealed class ResolvedSheet
{
    public IXLWorksheet Worksheet { get; init; } = default!;
    public int HeaderRow { get; init; }
    public Dictionary<string, int> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ImportTemplateColumnEditDto> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Display names of required fields that no configured header matched.</summary>
    public List<string> MissingRequired { get; } = new();

    /// <summary>Fields or headers that matched more than one source column. These fields are not mapped.</summary>
    public List<string> Ambiguities { get; } = new();
    public int FirstDataRow { get; init; }
}

/// <summary>What one configured header (the parent's primary header or one of its aliases) turned out to be in a given sheet.</summary>
/// <param name="Column">The parent column mapping. Its cleaning rule, options and required flag apply to every header.</param>
/// <param name="HeaderText">The header text this entry stands for: the primary header or one alias.</param>
/// <param name="IsAlias">False for the parent's primary header.</param>
/// <param name="ColumnIndex">First source column holding this header, or null if the header isn't in the sheet.</param>
/// <param name="IsUsed">This is the header the field is read from.</param>
/// <param name="IsAmbiguous">The header was found but its field couldn't be resolved to a single column.</param>
/// <param name="IsMissingRequired">Its field is required and none of its headers was found.</param>
public sealed record AliasResult(
    ImportTemplateColumnEditDto Column, string HeaderText, bool IsAlias, int? ColumnIndex,
    bool IsUsed, bool IsAmbiguous, bool IsMissingRequired);

/// <summary>Outcome of matching a sheet's configured columns against the header row, one field at a time.</summary>
public sealed class ColumnResolution
{
    /// <summary>FieldKey -> source column, only for fields resolved to exactly one column.</summary>
    public Dictionary<string, int> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>FieldKey -> the parent column mapping, so its cleaning rule is applied whichever header (primary or alias) matched.</summary>
    public Dictionary<string, ImportTemplateColumnEditDto> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> MissingRequired { get; } = new();          // field display names
    public List<string> MissingRequiredMessages { get; } = new();
    public List<string> Ambiguities { get; } = new();              // messages
    public List<AliasResult> Aliases { get; } = new();             // one per configured header: each primary header and each alias
}

/// <summary>How well one header row matches a template sheet's configured fields.</summary>
/// <param name="Accepted">Meets the minimum: enough fields found, and enough of the required ones.</param>
public sealed record HeaderRowMatch(
    int Row, int MatchedFields, int ConfiguredFields, int RequiredMatched, int RequiredFields, bool Accepted);

public static class TemplateSheetResolver
{
    public const int HeaderScanRows = 20;

    public static string Norm(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s\.\#\/\-\,\:\(\)]+", " ").Trim();


    /// <summary>
    /// Every header text that means this column's field: the primary header first, then its aliases in order.
    /// Blank texts are left out. This is the one place that defines "all headers of a column".
    /// </summary>
    public static IEnumerable<string> HeaderTexts(ImportTemplateColumnEditDto c)
    {
        if (!string.IsNullOrWhiteSpace(c.HeaderText)) yield return c.HeaderText.Trim();
        foreach (var a in (c.Aliases ?? new()).OrderBy(a => a.SortOrder))
            if (!string.IsNullOrWhiteSpace(a.AliasText)) yield return a.AliasText.Trim();
    }

    /// <summary>The same headers, normalized, with empties and repeats removed.</summary>
    public static HashSet<string> NormalizedHeaders(IEnumerable<ImportTemplateColumnEditDto> columns) =>
        columns.SelectMany(HeaderTexts).Select(Norm).Where(k => k.Length > 0).ToHashSet();

    public static ResolvedSheet? Resolve(ImportTemplateEditDto template, XLWorkbook wb, Action<string> addError)
    {
        var sheets = wb.Worksheets.ToList();
        var used = new HashSet<int>();

        foreach (var ts in template.Sheets.Where(s => s.SheetMatchMode != SheetMatchModes.Ignore))
        {
            var ws = FindSheet(ts, sheets, used, out var notFoundReason);
            if (ws is null)
            {
                if (ts.IsRequired)
                    addError($"Required sheet \"{ts.SheetLabel}\" was not found in the file" +
                             (notFoundReason is null ? "." : $": {notFoundReason}"));
                continue;
            }
            used.Add(ws.Position);

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow == 0) continue;

            var headerRow = ts.HeaderRowMode == HeaderRowModes.Fixed
                ? ts.HeaderRowNumber ?? 1
                : FindHeaderRow(ts, ws, Math.Min(lastRow, HeaderScanRows), lastCol);

            if (headerRow is null)
            {
                addError(DescribeMissingHeaderRow(ts, ws, lastRow, lastCol));
                continue;
            }

            var count = Math.Max(1, ts.HeaderRowCount);
            var resolved = new ResolvedSheet
            {
                Worksheet = ws,
                HeaderRow = headerRow.Value,
                FirstDataRow = headerRow.Value + count
            };

            var cols = ResolveColumns(template.ImportType, ts, ws, headerRow.Value, count, lastCol);

            foreach (var (field, idx) in cols.Headers) resolved.Headers[field] = idx;
            foreach (var (field, col) in cols.Columns) resolved.Columns[field] = col;
            resolved.MissingRequired.AddRange(cols.MissingRequired);
            resolved.Ambiguities.AddRange(cols.Ambiguities);

            foreach (var msg in cols.MissingRequiredMessages) addError($"Sheet \"{ws.Name}\": {msg}");
            foreach (var msg in cols.Ambiguities) addError($"Sheet \"{ws.Name}\": {msg}");

            return resolved;
        }

        addError("None of the template's sheets could be read from this file.");
        return null;
    }

    /// <summary>
    /// Matches a sheet's configured columns to its header row, one field at a time.
    /// Several configured rows can name the same field (aliases such as "Invoice No." / "SI Number").
    /// - A field is required when any of its aliases is marked required.
    /// - A required field is satisfied when any one alias is found.
    /// - Two different alias headers found for one field = alias ambiguity; the field is left unmapped.
    /// - One header found in several source columns = duplicate-header ambiguity; the field is left unmapped.
    /// - The cleaning rule is the one on the alias that matched. Nothing is overwritten or picked silently.
    /// </summary>
    public static ColumnResolution ResolveColumns(
        string importType, ImportTemplateSheetEditDto ts, IXLWorksheet ws, int headerRow, int headerRowCount, int lastCol)
    {
        // Every source column per header text, so a repeated header is seen instead of the first one winning.
        var byText = new Dictionary<string, List<int>>();
        for (var c = 1; c <= lastCol; c++)
        {
            var key = Norm(HeaderText(ws, headerRow, headerRowCount, c));
            if (key.Length == 0) continue;
            if (!byText.TryGetValue(key, out var list)) byText[key] = list = new List<int>();
            list.Add(c);
        }

        return ResolveColumns(importType, ts, byText);
    }

    /// <summary>
    /// Same field-by-field matching, from header text -> source columns already read (a <see cref="HeaderCandidate"/>'s
    /// Columns), so nothing re-reads the worksheet.
    /// </summary>
    public static ColumnResolution ResolveColumns(
        string importType, ImportTemplateSheetEditDto ts, IReadOnlyDictionary<string, List<int>> byText)
    {
        var result = new ColumnResolution();

        var chosen = new HashSet<(ImportTemplateColumnEditDto Parent, string Key)>();   // (mapping, header) a field is read from
        var ambiguousFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var groups = ts.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey))
            .GroupBy(c => c.FieldKey!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var field = group.Key;
            var display = ImportFieldRegistry.GetField(importType, field)?.Display ?? field;
            var required = group.Any(c => c.IsRequired);

            // Every header (primary + aliases) of this field's mapping, and which of them exist in the sheet.
            // Two headers with the same normalized text count once.
            var headers = group.SelectMany(TemplateHeaders).ToList();
            var found = headers
                .Where(h => h.Key.Length > 0 && byText.ContainsKey(h.Key))
                .ToList();
            var foundKeys = found.Select(h => h.Key).Distinct().ToList();

            var ambiguous = false;

            // Alias ambiguity: the file has more than one of this field's headers, in different columns.
            if (foundKeys.Count > 1)
            {
                ambiguous = true;
                var where = foundKeys.Select(k =>
                    $"\"{found.First(h => h.Key == k).Text}\" ({ColumnList(byText[k])})");
                result.Ambiguities.Add(
                    $"Alias ambiguity: {display} has more than one matching header in this sheet: {string.Join(", ", where)}. " +
                    "Only one of them should be present.");
            }

            // Duplicate-header ambiguity: one header text sits in several source columns.
            foreach (var k in foundKeys.Where(k => byText[k].Count > 1))
            {
                ambiguous = true;
                var text = found.First(h => h.Key == k).Text;
                result.Ambiguities.Add(
                    $"Duplicate header: \"{text}\" appears in columns {ColumnList(byText[k])}, so it can't be matched to {display}. " +
                    "Rename or remove one of them.");
            }

            if (ambiguous)
            {
                ambiguousFields.Add(field);
                continue;   // not mapped: never guess which column was meant
            }

            if (foundKeys.Count == 1)
            {
                var hit = found[0];                         // the parent's rule applies, whichever header matched
                result.Headers[field] = byText[foundKeys[0]][0];
                result.Columns[field] = hit.Parent;
                chosen.Add((hit.Parent, foundKeys[0]));
                continue;
            }

            if (required)
            {
                missingFields.Add(field);
                result.MissingRequired.Add(display);
                var names = headers.Select(h => $"\"{h.Text}\"").Distinct().ToList();
                result.MissingRequiredMessages.Add(names.Count == 1
                    ? $"Required column {names[0]} ({display}) was not found in the header row."
                    : $"Required field {display} was not found in the header row. Expected one of: {string.Join(", ", names)}.");
            }
        }

        foreach (var col in ts.Columns)
        {
            var hasField = !string.IsNullOrWhiteSpace(col.FieldKey);
            foreach (var h in TemplateHeaders(col))
            {
                int? idx = h.Key.Length > 0 && byText.TryGetValue(h.Key, out var cols) ? (int?)cols[0] : null;
                result.Aliases.Add(new AliasResult(
                    col,
                    h.Text,
                    h.IsAlias,
                    idx,
                    IsUsed: chosen.Contains((col, h.Key)),
                    IsAmbiguous: idx is not null && hasField && ambiguousFields.Contains(col.FieldKey!),
                    IsMissingRequired: hasField && missingFields.Contains(col.FieldKey!)));
            }
        }

        return result;
    }

    private sealed record TemplateHeader(ImportTemplateColumnEditDto Parent, string Text, string Key, bool IsAlias);

    // The parent's primary header followed by its aliases, each with its normalized key.
    private static IEnumerable<TemplateHeader> TemplateHeaders(ImportTemplateColumnEditDto c)
    {
        var primaryPending = !string.IsNullOrWhiteSpace(c.HeaderText);
        foreach (var text in HeaderTexts(c))
        {
            yield return new TemplateHeader(c, text, Norm(text), IsAlias: !primaryPending);
            primaryPending = false;
        }
    }

    private static string ColumnList(IEnumerable<int> columns) =>
        string.Join(" and ", columns.Select(c => XLHelper.GetColumnLetterFromNumber(c)));

    public static IXLWorksheet? FindSheet(ImportTemplateSheetEditDto ts, List<IXLWorksheet> sheets, HashSet<int> used) =>
        FindSheet(ts, sheets, used, out _);

    /// <param name="reason">Why nothing was returned, when the mode can say (Headers mode). Null otherwise.</param>
    public static IXLWorksheet? FindSheet(
        ImportTemplateSheetEditDto ts, List<IXLWorksheet> sheets, HashSet<int> used, out string? reason)
    {
        var free = sheets.Where(w => !used.Contains(w.Position)).ToList();
        var value = ts.SheetMatchValue?.Trim() ?? "";

        reason = null;
        if (ts.SheetMatchMode == SheetMatchModes.Headers)
            return FindSheetByHeaders(ts, free, out reason);

        return ts.SheetMatchMode switch
        {
            SheetMatchModes.Any => free.FirstOrDefault(),
            SheetMatchModes.Exact => free.FirstOrDefault(w => string.Equals(w.Name, value, StringComparison.OrdinalIgnoreCase)),
            SheetMatchModes.Contains => free.FirstOrDefault(w => w.Name.Contains(value, StringComparison.OrdinalIgnoreCase)),
            SheetMatchModes.Position => int.TryParse(value, out var p) ? sheets.ElementAtOrDefault(p - 1) : null,
            _ => null
        };
    }
    // ── Header matching ──────────────────────────────────────────────────
    // A row or sheet counts as "the one with the headers" only if it clearly is, so one coincidental word
    // (a title like "Sales Report - Quantity") can't be taken as the header row.

    /// <summary>At least this many of the template's fields must be found (or all of them, if fewer are configured).</summary>
    public const int MinHeaderFields = 2;

    /// <summary>
    /// ...and at least this share of its required fields. This rejects coincidences without demanding
    /// completeness: a file missing one required column still matches, and that column is then reported by name.
    /// </summary>
    public const double MinRequiredShare = 0.5;

    private sealed record FieldAliases(HashSet<string> Aliases, bool Required);

    // One entry per configured field; its aliases are the normalized header texts that mean it.
    private static List<FieldAliases> ConfiguredFields(ImportTemplateSheetEditDto ts) =>
        ts.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey))
            .GroupBy(c => c.FieldKey!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FieldAliases(NormalizedHeaders(g), g.Any(c => c.IsRequired)))
            .Where(f => f.Aliases.Count > 0)
            .ToList();

    private static HeaderRowMatch ScoreHeaderRow(
        List<FieldAliases> fields, IXLWorksheet ws, int row, int headerRowCount, int lastCol)
    {
        var present = new HashSet<string>();
        for (var c = 1; c <= lastCol; c++)
        {
            var key = Norm(HeaderText(ws, row, headerRowCount, c));
            if (key.Length > 0) present.Add(key);
        }

        // Counted per field, not per column, so repeated headers and aliases can't inflate the score.
        var matched = fields.Count(f => f.Aliases.Overlaps(present));
        var required = fields.Count(f => f.Required);
        var requiredMatched = fields.Count(f => f.Required && f.Aliases.Overlaps(present));

        var accepted = fields.Count > 0
            && matched >= Math.Min(MinHeaderFields, fields.Count)
            && (required == 0 || (double)requiredMatched / required >= MinRequiredShare);

        return new HeaderRowMatch(row, matched, fields.Count, requiredMatched, required, accepted);
    }

    /// <summary>
    /// The best header-row candidate in the first rows: most fields found, then most required fields,
    /// then the earliest row. Null when no row has any of the headers. Check <see cref="HeaderRowMatch.Accepted"/>.
    /// </summary>
    public static HeaderRowMatch? DetectHeaderRow(ImportTemplateSheetEditDto ts, IXLWorksheet ws, int scanRows, int lastCol)
    {
        var fields = ConfiguredFields(ts);
        var count = Math.Max(1, ts.HeaderRowCount);

        HeaderRowMatch? best = null;
        for (var row = 1; row <= scanRows; row++)
        {
            var m = ScoreHeaderRow(fields, ws, row, count, lastCol);
            if (m.MatchedFields == 0) continue;

            if (best is null
                || m.MatchedFields > best.MatchedFields
                || (m.MatchedFields == best.MatchedFields && m.RequiredMatched > best.RequiredMatched))
                best = m;
        }
        return best;
    }

    /// <summary>The header row, or null if no row meets the minimum. See <see cref="DescribeMissingHeaderRow"/> for why.</summary>
    public static int? FindHeaderRow(ImportTemplateSheetEditDto ts, IXLWorksheet ws, int scanRows, int lastCol) =>
        DetectHeaderRow(ts, ws, scanRows, lastCol) is { Accepted: true } m ? (int?)m.Row : null;

    public static string DescribeMissingHeaderRow(ImportTemplateSheetEditDto ts, IXLWorksheet ws, int lastRow, int lastCol)
    {
        var best = DetectHeaderRow(ts, ws, Math.Min(lastRow, HeaderScanRows), lastCol);
        return best is null
            ? $"Couldn't find the header row on sheet \"{ws.Name}\": none of the expected headers appear in its first {HeaderScanRows} rows."
            : $"Couldn't find the header row on sheet \"{ws.Name}\": the closest, row {best.Row}, has only {best.MatchedFields} of {best.ConfiguredFields} expected fields.";
    }

    // How well a whole worksheet matches a template sheet. A fixed header row is scored where the admin said it is.
    private static HeaderRowMatch? ScoreSheetHeaders(ImportTemplateSheetEditDto ts, IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0 || lastCol == 0) return null;

        if (ts.HeaderRowMode == HeaderRowModes.Fixed)
            return ScoreHeaderRow(ConfiguredFields(ts), ws, ts.HeaderRowNumber ?? 1, Math.Max(1, ts.HeaderRowCount), lastCol);

        return DetectHeaderRow(ts, ws, Math.Min(lastRow, HeaderScanRows), lastCol);
    }

    // Headers mode: the unclaimed worksheet whose headers fit best. A tie is reported, never resolved by picking one.
    private static IXLWorksheet? FindSheetByHeaders(ImportTemplateSheetEditDto ts, List<IXLWorksheet> free, out string? reason)
    {
        reason = null;

        var scored = free
            .Select(w => (Sheet: w, Match: ScoreSheetHeaders(ts, w)))
            .Where(x => x.Match is not null)
            .Select(x => (x.Sheet, Match: x.Match!))
            .ToList();

        var accepted = scored.Where(x => x.Match.Accepted).ToList();
        if (accepted.Count == 0)
        {
            if (scored.Count == 0)
            {
                reason = "no sheet in the file has any of these headers.";
                return null;
            }

            var closest = scored
                .OrderByDescending(x => x.Match.MatchedFields)
                .ThenByDescending(x => x.Match.RequiredMatched)
                .First();
            reason = $"no sheet has enough of these headers. The closest, \"{closest.Sheet.Name}\", has {closest.Match.MatchedFields} of {closest.Match.ConfiguredFields} expected fields.";
            return null;
        }

        var ranked = accepted
            .OrderByDescending(x => x.Match.MatchedFields)
            .ThenByDescending(x => x.Match.RequiredMatched)
            .ToList();
        var top = ranked[0];
        var tied = ranked
            .Where(x => x.Match.MatchedFields == top.Match.MatchedFields && x.Match.RequiredMatched == top.Match.RequiredMatched)
            .ToList();

        if (tied.Count > 1)
        {
            reason = $"{string.Join(", ", tied.Select(x => $"\"{x.Sheet.Name}\""))} match these headers equally well, so it can't tell which one to read.";
            return null;
        }

        return top.Sheet;
    }

    public static string HeaderText(IXLWorksheet ws, int firstRow, int rowCount, int col)
    {
        var parts = new List<string>();
        for (var r = firstRow; r < firstRow + rowCount; r++)
        {
            var cell = ws.Cell(r, col);
            if (cell.IsMerged()) cell = cell.MergedRange().FirstCell();

            var t = CellReader.Text(cell);
            if (t.Length == 0) continue;
            if (parts.Count > 0 && string.Equals(parts[^1], t, StringComparison.OrdinalIgnoreCase)) continue;
            parts.Add(t);
        }
        return string.Join(" ", parts);
    }
}

public static class CellReader
{
    public static string Text(IXLCell cell)
    {
        try
        {
            if (!cell.HasFormula)
                return cell.GetString().Trim();

            var cached = cell.CachedValue;
            if (!cached.IsBlank)
                return cached.ToString(CultureInfo.InvariantCulture).Trim();

            return cell.GetString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}