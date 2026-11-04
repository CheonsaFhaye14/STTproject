using ClosedXML.Excel;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>
/// One possible header block on a worksheet: the row it starts on, how many rows it spans, and where each
/// (normalized) header text sits. Built from the sheet only, so it doesn't depend on any template.
/// </summary>
public sealed class HeaderCandidate
{
    public int Row { get; init; }
    public int RowCount { get; init; }

    /// <summary>Normalized header text -> source columns (1-based, left to right). More than one column = duplicate header.</summary>
    public IReadOnlyDictionary<string, List<int>> Columns { get; init; } = new Dictionary<string, List<int>>();

    /// <summary>The distinct normalized headers on this block. This is what field matching counts.</summary>
    public IEnumerable<string> Keys => Columns.Keys;

    public bool HasDuplicates => Columns.Values.Any(c => c.Count > 1);
}

/// <summary>
/// Everything the matcher needs to know about one worksheet, read once and cached.
/// Cell text is read lazily per row, so a Fixed header row below row 20 still works.
/// Header blocks are cached per (row, rowCount) because a multi-row header joins its rows into one text,
/// which means the same row looks different to a template with HeaderRowCount 1 and one with 2.
/// </summary>
public sealed class WorksheetInspection
{
    private readonly Dictionary<int, string[]> _rows = new();
    private readonly Dictionary<(int Row, int Count), HeaderCandidate> _candidates = new();

    public WorksheetInspection(IXLWorksheet worksheet, int? visibleOrdinal)
    {
        Worksheet = worksheet;
        VisibleOrdinal = visibleOrdinal;
        Visibility = worksheet.Visibility;
        LastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        LastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
    }

    public IXLWorksheet Worksheet { get; }

    /// <summary>Position in the workbook, counting hidden sheets (what ClosedXML calls Position).</summary>
    public int SheetIndex => Worksheet.Position;

    public string Name => Worksheet.Name;
    public XLWorksheetVisibility Visibility { get; }

    /// <summary>Hidden and very-hidden sheets are never matched automatically.</summary>
    public bool IsVisible => Visibility == XLWorksheetVisibility.Visible;

    /// <summary>1-based position among visible sheets only; null for hidden ones. Use this for "Position" matching.</summary>
    public int? VisibleOrdinal { get; }

    public int LastRow { get; }
    public int LastCol { get; }
    public bool IsEmpty => LastRow == 0 || LastCol == 0;

    // Cell text for one row, columns 1..LastCol, merged cells resolved to the merged range's first cell.
    private string[] RowTexts(int row)
    {
        if (_rows.TryGetValue(row, out var cached)) return cached;

        var texts = new string[LastCol];
        for (var c = 1; c <= LastCol; c++)
        {
            var cell = Worksheet.Cell(row, c);
            if (cell.IsMerged()) cell = cell.MergedRange().FirstCell();
            texts[c - 1] = CellReader.Text(cell);
        }
        return _rows[row] = texts;
    }

    /// <summary>Same result as <see cref="TemplateSheetResolver.HeaderText"/>, without re-reading cells.</summary>
    public string HeaderText(int firstRow, int rowCount, int col)
    {
        if (col < 1 || col > LastCol) return "";

        var parts = new List<string>();
        for (var r = firstRow; r < firstRow + rowCount; r++)
        {
            var t = RowTexts(r)[col - 1];
            if (t.Length == 0) continue;
            if (parts.Count > 0 && string.Equals(parts[^1], t, StringComparison.OrdinalIgnoreCase)) continue;
            parts.Add(t);
        }
        return string.Join(" ", parts);
    }

    /// <summary>The header block that starts on <paramref name="row"/> and spans <paramref name="rowCount"/> rows.</summary>
    public HeaderCandidate GetCandidate(int row, int rowCount)
    {
        rowCount = Math.Max(1, rowCount);
        if (_candidates.TryGetValue((row, rowCount), out var cached)) return cached;

        var columns = new Dictionary<string, List<int>>();
        if (!IsEmpty)
        {
            for (var c = 1; c <= LastCol; c++)
            {
                var key = TemplateSheetResolver.Norm(HeaderText(row, rowCount, c));
                if (key.Length == 0) continue;
                if (!columns.TryGetValue(key, out var list)) columns[key] = list = new List<int>();
                list.Add(c);
            }
        }

        return _candidates[(row, rowCount)] = new HeaderCandidate { Row = row, RowCount = rowCount, Columns = columns };
    }

    /// <summary>Every row in the first <paramref name="scanRows"/> that has at least one header text, read as a block of <paramref name="rowCount"/> rows.</summary>
    public IReadOnlyList<HeaderCandidate> ScanCandidates(int rowCount, int scanRows = TemplateSheetResolver.HeaderScanRows)
    {
        if (IsEmpty) return Array.Empty<HeaderCandidate>();

        var list = new List<HeaderCandidate>();
        for (var row = 1; row <= Math.Min(LastRow, scanRows); row++)
        {
            var cand = GetCandidate(row, rowCount);
            if (cand.Columns.Count > 0) list.Add(cand);
        }
        return list;
    }
}

public static class WorkbookInspector
{
    /// <summary>
    /// Inspects every worksheet, hidden ones included (so they can be reported), in workbook order.
    /// Nothing here knows about templates; the matcher decides what to do with each result.
    /// </summary>
    public static List<WorksheetInspection> Inspect(XLWorkbook workbook)
    {
        var result = new List<WorksheetInspection>();
        var visible = 0;

        foreach (var ws in workbook.Worksheets)
        {
            var isVisible = ws.Visibility == XLWorksheetVisibility.Visible;
            result.Add(new WorksheetInspection(ws, isVisible ? ++visible : null));
        }
        return result;
    }
}