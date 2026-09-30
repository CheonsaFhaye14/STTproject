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
    public List<string> MissingRequired { get; } = new();
}

public static class TemplateSheetResolver
{
    public const int HeaderScanRows = 20;

    public static string Norm(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s\.\#\/\-\,\:\(\)]+", " ").Trim();


    public static ResolvedSheet? Resolve(ImportTemplateEditDto template, XLWorkbook wb, Action<string> addError)
    {
        var sheets = wb.Worksheets.ToList();
        var used = new HashSet<int>();

        foreach (var ts in template.Sheets.Where(s => s.SheetMatchMode != SheetMatchModes.Ignore))
        {
            var ws = FindSheet(ts, sheets, used);
            if (ws is null)
            {
                if (ts.IsRequired) addError($"Required sheet \"{ts.SheetLabel}\" was not found in the file.");
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
                addError($"Couldn't find the header row on sheet \"{ws.Name}\".");
                continue;
            }

            var resolved = new ResolvedSheet { Worksheet = ws, HeaderRow = headerRow.Value };

            var byText = new Dictionary<string, int>();
            for (var c = 1; c <= lastCol; c++)
            {
                var key = Norm(CellReader.Text(ws.Cell(headerRow.Value, c)));                
                if (key.Length > 0) byText.TryAdd(key, c);
            }

            foreach (var col in ts.Columns.Where(c => !string.IsNullOrWhiteSpace(c.FieldKey)))
            {
                if (byText.TryGetValue(Norm(col.HeaderText), out var idx))
                {
                    resolved.Headers[col.FieldKey!] = idx;
                    resolved.Columns[col.FieldKey!] = col;
                }
                else if (col.IsRequired)
                {
                    resolved.MissingRequired.Add(col.HeaderText);
                    addError($"Required column \"{col.HeaderText}\" was not found in the header row.");
                }
            }

            return resolved;
        }

        addError("None of the template's sheets could be read from this file.");
        return null;
    }

    public static IXLWorksheet? FindSheet(ImportTemplateSheetEditDto ts, List<IXLWorksheet> sheets, HashSet<int> used)
    {
        var free = sheets.Where(w => !used.Contains(w.Position)).ToList();
        var value = ts.SheetMatchValue?.Trim() ?? "";

        return ts.SheetMatchMode switch
        {
            SheetMatchModes.Any      => free.FirstOrDefault(),
            SheetMatchModes.Exact    => free.FirstOrDefault(w => string.Equals(w.Name, value, StringComparison.OrdinalIgnoreCase)),
            SheetMatchModes.Contains => free.FirstOrDefault(w => w.Name.Contains(value, StringComparison.OrdinalIgnoreCase)),
            SheetMatchModes.Position => int.TryParse(value, out var p) ? sheets.ElementAtOrDefault(p - 1) : null,
            _ => null
        };
    }

    public static int? FindHeaderRow(ImportTemplateSheetEditDto ts, IXLWorksheet ws, int scanRows, int lastCol)
    {
        var wanted = ts.Columns.Select(c => Norm(c.HeaderText)).Where(t => t.Length > 0).ToHashSet();

        int? best = null;
        var bestHits = 0;
        for (var row = 1; row <= scanRows; row++)
        {
            var hits = 0;
            for (var c = 1; c <= lastCol; c++)
                if (wanted.Contains(Norm(CellReader.Text(ws.Cell(row, c))))) hits++;

            if (hits > bestHits) { best = row; bestHits = hits; }
        }
        return best;
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
}