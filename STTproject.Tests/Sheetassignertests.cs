using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;
using Xunit;

namespace STTproject.Tests.ImportTemplate;

public class SheetAssignerTests
{
    private const double N = double.NaN;   // "not eligible" marker, converted below

    private static double?[,] Matrix(double[][] rows)
    {
        var m = new double?[rows.Length, rows[0].Length];
        for (var i = 0; i < rows.Length; i++)
            for (var j = 0; j < rows[i].Length; j++)
                m[i, j] = double.IsNaN(rows[i][j]) ? null : rows[i][j];
        return m;
    }

    [Fact]
    public void Greedy_trap_is_avoided()
    {
        // T0 prefers S0 (0.95) but T1 can only use S0. Greedy leaves T1 empty; the global answer is T0->S1, T1->S0.
        var r = SheetAssigner.Solve(Matrix(new[] { new[] { 0.95, 0.90 }, new[] { 0.92, N } }), new[] { true, true });

        Assert.Equal(new[] { 1, 0 }, r.Assigned);
        Assert.Equal(2, r.Objective.RequiredMatched);
        Assert.Empty(r.AmbiguousTemplates);
    }

    [Fact]
    public void Required_sheet_beats_a_higher_scoring_optional_one()
    {
        // Both want S0. Optional scores 1.0, required only 0.1. Required count comes first.
        var r = SheetAssigner.Solve(Matrix(new[] { new[] { 1.0 }, new[] { 0.1 } }), new[] { false, true });

        Assert.Equal(new[] { -1, 0 }, r.Assigned);
        Assert.Empty(r.AmbiguousTemplates);
    }

    [Fact]
    public void Optional_sheet_without_a_candidate_stays_unmatched_without_ambiguity()
    {
        var r = SheetAssigner.Solve(Matrix(new[] { new[] { 0.8 }, new[] { N } }), new[] { true, false });

        Assert.Equal(new[] { 0, -1 }, r.Assigned);
        Assert.Empty(r.AmbiguousTemplates);
    }

    [Fact]
    public void Clear_winner_is_not_ambiguous()
    {
        var r = SheetAssigner.Solve(Matrix(new[] { new[] { 0.9, 0.5 } }), new[] { true });

        Assert.Equal(new[] { 0 }, r.Assigned);
        Assert.Empty(r.AmbiguousTemplates);
    }

    [Fact]
    public void Swappable_pair_reports_both_template_sheets_and_both_worksheets()
    {
        var r = SheetAssigner.Solve(Matrix(new[] { new[] { 1.0, 1.0 }, new[] { 1.0, 1.0 } }), new[] { true, true });

        Assert.Equal(new[] { 0, 1 }, r.AmbiguousTemplates.OrderBy(x => x));
        var g = Assert.Single(r.Groups);
        Assert.Equal(new[] { 0, 1 }, g.TemplateSheets);
        Assert.Equal(new[] { 0, 1 }, g.Worksheets);
    }

    [Fact]
    public void Two_required_sheets_fighting_for_one_worksheet_are_both_ambiguous()
    {
        // Only one can have S0, and they score the same: either could be the one that goes unmatched.
        var r = SheetAssigner.Solve(Matrix(new[] { new[] { 1.0 }, new[] { 1.0 } }), new[] { true, true });

        Assert.Equal(2, r.AmbiguousTemplates.Count);
        Assert.Contains(-1, r.Options[0]);
        Assert.Contains(0, r.Options[0]);
    }

    [Fact]
    public void Scores_within_tolerance_count_as_a_tie()
    {
        var r = SheetAssigner.Solve(Matrix(new[] { new[] { 0.9, 0.9 + 1e-12 } }), new[] { true });

        Assert.Contains(0, r.AmbiguousTemplates);
    }

    [Fact]
    public void A_worksheet_is_never_assigned_twice()
    {
        var r = SheetAssigner.Solve(
            Matrix(new[] { new[] { 0.9, 0.8, 0.1 }, new[] { 0.9, 0.8, 0.1 }, new[] { 0.9, 0.8, 0.1 } }),
            new[] { true, true, true });

        var taken = r.Assigned.Where(j => j >= 0).ToList();
        Assert.Equal(taken.Count, taken.Distinct().Count());
        Assert.Equal(3, r.Objective.RequiredMatched);
    }

    // ── Hidden worksheets and Position ────────────────────────────────────

    private static ImportTemplateEditDto PositionTemplate(params string[] positions) => new()
    {
        Sheets = positions.Select((p, i) => new ImportTemplateSheetEditDto
        {
            SheetLabel = $"S{i + 1}",
            SheetMatchMode = SheetMatchModes.Position,
            SheetMatchValue = p,
            IsRequired = true
        }).ToList()
    };

    private static XLWorkbook Workbook(params (string Name, bool Hidden)[] sheets)
    {
        var wb = new XLWorkbook();
        foreach (var (name, hidden) in sheets)
        {
            var ws = wb.Worksheets.Add(name);
            ws.Cell(1, 1).Value = "x";
            if (hidden) ws.Visibility = XLWorksheetVisibility.Hidden;
        }
        return wb;
    }

    [Fact]
    public void Hidden_sheet_before_visible_ones_does_not_shift_Position()
    {
        using var wb = Workbook(("Lookup", true), ("Sales", false), ("Returns", false));
        var result = WorkbookAssigner.Assign(PositionTemplate("1", "2"), WorkbookInspector.Inspect(wb));

        Assert.Equal("Sales", result.Sheets[0].Worksheet!.Name);
        Assert.Equal("Returns", result.Sheets[1].Worksheet!.Name);
    }

    [Fact]
    public void Hidden_sheet_between_visible_ones_does_not_shift_Position()
    {
        using var wb = Workbook(("Sales", false), ("Lookup", true), ("Returns", false));
        var result = WorkbookAssigner.Assign(PositionTemplate("1", "2"), WorkbookInspector.Inspect(wb));

        Assert.Equal("Sales", result.Sheets[0].Worksheet!.Name);
        Assert.Equal("Returns", result.Sheets[1].Worksheet!.Name);
    }

    [Fact]
    public void Hidden_sheets_are_never_unclaimed_or_matched()
    {
        using var wb = Workbook(("Lookup", true), ("Sales", false), ("Notes", false));
        var result = WorkbookAssigner.Assign(PositionTemplate("1"), WorkbookInspector.Inspect(wb));

        Assert.Equal("Sales", result.Sheets[0].Worksheet!.Name);
        Assert.Equal(new[] { "Notes" }, result.Unclaimed.Select(u => u.Name));
    }
}