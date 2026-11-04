using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;
using Xunit;

namespace STTproject.Tests.ImportTemplate;

// Sheet-assignment outcome only: these use name/position matching, so no columns or header rows are needed.
public class WorkbookMatchTests
{
    private static ImportTemplateEditDto Tpl(params (string Label, string Mode, string? Value, bool Required)[] sheets) => new()
    {
        Sheets = sheets.Select(s => new ImportTemplateSheetEditDto
        {
            SheetLabel = s.Label,
            SheetMatchMode = s.Mode,
            SheetMatchValue = s.Value,
            IsRequired = s.Required
        }).ToList()
    };

    private static XLWorkbook Book(params (string Name, bool Hidden)[] sheets)
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
    public void Every_template_sheet_found_is_eligible()
    {
        using var wb = Book(("Sales", false), ("Returns", false));
        var m = WorkbookMatcher.Match(
            Tpl(("Sales", SheetMatchModes.Exact, "Sales", true), ("Returns", SheetMatchModes.Exact, "Returns", true)), wb);

        Assert.True(m.IsEligible);
        Assert.Equal(2, m.Matched.Count);
        Assert.Empty(m.Unclaimed);
        Assert.Empty(m.Hidden);
        Assert.Empty(m.Errors);
        Assert.All(m.Worksheets, w => Assert.Equal(WorksheetStatus.Matched, w.Status));
    }

    [Fact]
    public void Missing_required_sheet_blocks_the_workbook_and_is_named()
    {
        using var wb = Book(("Sales", false));
        var m = WorkbookMatcher.Match(
            Tpl(("Sales", SheetMatchModes.Exact, "Sales", true), ("Free Goods", SheetMatchModes.Exact, "Free Goods", true)), wb);

        Assert.False(m.IsEligible);
        Assert.Equal("Free Goods", Assert.Single(m.MissingRequired).TemplateSheet.SheetLabel);
        Assert.Single(m.Matched);
        Assert.Contains(m.Errors, e => e.Contains("Free Goods") && e.Contains("was not found"));
    }

    [Fact]
    public void Missing_optional_sheet_only_warns()
    {
        using var wb = Book(("Sales", false));
        var m = WorkbookMatcher.Match(
            Tpl(("Sales", SheetMatchModes.Exact, "Sales", true), ("Returns", SheetMatchModes.Exact, "Returns", false)), wb);

        Assert.True(m.IsEligible);
        Assert.Empty(m.MissingRequired);
        Assert.Single(m.MissingOptional);
        Assert.Empty(m.Errors);
        Assert.Contains(m.Warnings, w => w.Contains("Returns") && w.Contains("skipped"));
    }

    [Fact]
    public void Hidden_sheet_is_reported_but_not_matched_or_unclaimed()
    {
        using var wb = Book(("Lookup", true), ("Sales", false));
        var m = WorkbookMatcher.Match(Tpl(("Sales", SheetMatchModes.Exact, "Sales", true)), wb);

        Assert.True(m.IsEligible);
        Assert.Equal("Lookup", Assert.Single(m.Hidden).Name);
        Assert.Empty(m.Unclaimed);
        Assert.Equal(WorksheetStatus.Hidden, m.Worksheets[0].Status);
        Assert.Equal(WorksheetStatus.Matched, m.Worksheets[1].Status);
    }

    [Fact]
    public void Required_sheet_that_exists_only_as_a_hidden_sheet_is_missing_and_says_so()
    {
        using var wb = Book(("Lookup", true), ("Sales", false));
        var m = WorkbookMatcher.Match(Tpl(("Lookup", SheetMatchModes.Exact, "Lookup", true)), wb);

        Assert.False(m.IsEligible);
        var missing = Assert.Single(m.MissingRequired);
        Assert.Contains("hidden", missing.Reason);
        Assert.Equal("Sales", Assert.Single(m.Unclaimed).Name);
    }

    [Fact]
    public void Visible_sheet_nothing_claims_is_unclaimed_and_harmless()
    {
        using var wb = Book(("Sales", false), ("Notes", false));
        var m = WorkbookMatcher.Match(Tpl(("Sales", SheetMatchModes.Exact, "Sales", true)), wb);

        Assert.True(m.IsEligible);
        Assert.Equal("Notes", Assert.Single(m.Unclaimed).Name);
        Assert.Equal(WorksheetStatus.Unclaimed, m.Worksheets[1].Status);
    }

    [Fact]
    public void Interchangeable_sheets_are_reported_once_with_everything_involved()
    {
        using var wb = Book(("Data1", false), ("Data2", false));
        var m = WorkbookMatcher.Match(
            Tpl(("A", SheetMatchModes.Contains, "Data", true), ("B", SheetMatchModes.Contains, "Data", true)), wb);

        Assert.False(m.IsEligible);
        var amb = Assert.Single(m.Ambiguities);
        Assert.Equal(2, amb.TemplateSheets.Count);
        Assert.Equal(new[] { "Data1", "Data2" }, amb.Worksheets.Select(w => w.Name));
        Assert.Empty(m.Matched);
        Assert.Empty(m.Unclaimed);
        Assert.All(m.Worksheets, w => Assert.Equal(WorksheetStatus.Ambiguous, w.Status));
        Assert.Contains(m.Errors, e => e.Contains("can't tell"));
    }

    [Fact]
    public void Truncated_assignment_is_refused_and_carries_no_assignment_information()
    {
        // Seven interchangeable required sheets: 7! = 5,040 equally good assignments, over the 1,000 cap.
        using var wb = Book(Enumerable.Range(1, 7).Select(i => ($"S{i}", false)).ToArray());
        var m = WorkbookMatcher.Match(
            Tpl(Enumerable.Range(1, 7).Select(i => ($"T{i}", SheetMatchModes.Any, (string?)null, true)).ToArray()), wb);

        Assert.True(m.IsTruncated);
        Assert.False(m.IsEligible);
        Assert.Empty(m.Matched);
        Assert.Empty(m.Ambiguities);
        Assert.Empty(m.MissingRequired);
        Assert.Empty(m.Unclaimed);
        Assert.Contains(m.Errors, e => e.Contains("equally good"));
        Assert.All(m.Worksheets, w => Assert.Equal(WorksheetStatus.Undetermined, w.Status));
    }
}