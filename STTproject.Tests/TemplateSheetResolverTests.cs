using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;
using STTproject.Features.Admin.ImportTemplate.Validators;
using Xunit;

namespace STTproject.Tests.ImportTemplate;

// Regression tests for field-level column resolution (Step 2) and Headers matching / thresholds (Step 3).
// They run TemplateSheetResolver.Resolve against real ClosedXML workbooks, which is the same call the
// Sales Invoice importer makes, so they cover the ClosedXML behaviour as well as the logic.
//
// Not covered here: ImportSalesInvoiceService.PrepareFromExcelAsync itself, because it needs a database.
// The importer treats any error reported by Resolve as "file could not be processed" and stops before reading
// a single row, so "Resolve reported an error" is the behaviour that matters for it. Written for xUnit;
// swap the attributes if the test project uses NUnit or MSTest.
public class TemplateSheetResolverTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static ImportTemplateColumnEditDto Col(
        string field, string header, bool required = true,
        string rule = ColumnRuleTypes.Direct, string? options = null) => new()
    {
        FieldKey = field, HeaderText = header, IsRequired = required, RuleType = rule, OptionsJson = options
    };

    // Four fields, so a file can be missing one and still be recognised as this template's sheet.
    private static List<ImportTemplateColumnEditDto> StandardColumns() => new()
    {
        Col("InvoiceCode", "Invoice No."), Col("InvoiceCode", "SI Number"),
        Col("CustomerCode", "Customer Code"), Col("CustomerCode", "Cust. Code"),
        Col("SkuCode", "SKU"),
        Col("Quantity", "Qty", required: false),
    };

    private static ImportTemplateEditDto Template(
        List<ImportTemplateColumnEditDto>? cols = null, string mode = SheetMatchModes.Any) => new()
    {
        ImportType = "SalesInvoice",
        Sheets =
        {
            new ImportTemplateSheetEditDto
            {
                SheetLabel = "Sales", SheetMatchMode = mode, Columns = cols ?? StandardColumns()
            }
        }
    };

    private static XLWorkbook Book(params (string Name, string[][] Rows)[] sheets)
    {
        var wb = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var ws = wb.Worksheets.Add(name);
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    ws.Cell(r + 1, c + 1).Value = rows[r][c];
        }
        return wb;
    }

    private static (ResolvedSheet? Sheet, List<string> Errors) Run(ImportTemplateEditDto template, XLWorkbook wb)
    {
        var errors = new List<string>();
        var sheet = TemplateSheetResolver.Resolve(template, wb, errors.Add);
        return (sheet, errors);
    }

    private static string[][] File(params string[][] rows) => rows;
    private static string[] Row(params string[] cells) => cells;

    // ── Step 2: field-level resolution ───────────────────────────────────

    [Fact]
    public void One_alias_found_resolves_the_field()
    {
        using var wb = Book(("Data", File(Row("Invoice No.", "Customer Code", "SKU", "Qty"), Row("1", "A", "S", "2"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Empty(errors);
        Assert.Equal(1, sheet!.Headers["InvoiceCode"]);
        Assert.Equal("Invoice No.", sheet.Columns["InvoiceCode"].HeaderText);
    }

    [Fact]
    public void Alternative_alias_found_resolves_the_field()
    {
        using var wb = Book(("Data", File(Row("SI Number", "Cust. Code", "SKU"), Row("1", "A", "S"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Empty(errors);
        Assert.Equal(1, sheet!.Headers["InvoiceCode"]);
        Assert.Equal("SI Number", sheet.Columns["InvoiceCode"].HeaderText);
        Assert.Equal(2, sheet.Headers["CustomerCode"]);
    }

    [Fact]
    public void Both_aliases_found_is_an_alias_ambiguity_and_the_field_is_left_unmapped()
    {
        using var wb = Book(("Data", File(Row("Invoice No.", "SI Number", "Customer Code", "SKU"), Row("1", "1", "A", "S"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Contains(errors, e => e.Contains("Alias ambiguity") && e.Contains("Invoice No.") && e.Contains("SI Number"));
        Assert.DoesNotContain("InvoiceCode", sheet!.Headers.Keys);
        Assert.Single(sheet.Ambiguities);
        Assert.Empty(sheet.MissingRequired);   // ambiguous is not "missing": it is reported once, as an ambiguity
    }

    [Fact]
    public void No_required_alias_found_reports_the_missing_field_by_name()
    {
        using var wb = Book(("Data", File(Row("Customer Code", "SKU", "Qty"), Row("A", "S", "2"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.NotNull(sheet);   // the sheet and its header row are still recognised...
        Assert.DoesNotContain(errors, e => e.Contains("Couldn't find the header row"));   // ...so no header-row failure
        Assert.Contains(errors, e => e.Contains("Required field") && e.Contains("Invoice No.") && e.Contains("SI Number"));
        Assert.Contains("Invoice No.", sheet!.MissingRequired);
    }

    [Fact]
    public void Missing_required_field_is_named_by_its_display_name_not_by_its_aliases()
    {
        // Aliases deliberately differ from the field's display name ("Invoice No."), so the two can't be confused.
        var cols = new List<ImportTemplateColumnEditDto>
        {
            Col("InvoiceCode", "Bill Ref"), Col("InvoiceCode", "Doc Ref"),
            Col("CustomerCode", "Customer Code"), Col("SkuCode", "SKU"),
        };
        using var wb = Book(("Data", File(Row("Customer Code", "SKU"), Row("A", "S"))));
        var (sheet, errors) = Run(Template(cols), wb);

        Assert.Contains(errors, e => e.Contains("Required field Invoice No. was not found")
                                     && e.Contains("\"Bill Ref\"") && e.Contains("\"Doc Ref\""));
        Assert.Contains("Invoice No.", sheet!.MissingRequired);
        Assert.DoesNotContain("Bill Ref", sheet.MissingRequired);
    }

    [Fact]
    public void Duplicate_source_header_is_a_duplicate_header_ambiguity()
    {
        using var wb = Book(("Data", File(Row("Invoice No.", "Customer Code", "SKU", "SKU"), Row("1", "A", "S", "S"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Contains(errors, e => e.Contains("Duplicate header") && e.Contains("SKU") && e.Contains("C and D"));
        Assert.DoesNotContain("SkuCode", sheet!.Headers.Keys);
        Assert.DoesNotContain(errors, e => e.Contains("Alias ambiguity"));   // a different situation, a different message
    }

    [Fact]
    public void Missing_optional_field_is_not_an_error()
    {
        using var wb = Book(("Data", File(Row("Invoice No.", "Customer Code", "SKU"), Row("1", "A", "S"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Empty(errors);
        Assert.DoesNotContain("Quantity", sheet!.Headers.Keys);
    }

    [Fact]
    public void Each_alias_keeps_its_own_cleaning_rule()
    {
        List<ImportTemplateColumnEditDto> Cols() => new()
        {
            Col("InvoiceCode", "Invoice No."),                                              // as is
            Col("InvoiceCode", "SI Number", rule: "StripLeadingCode"),                      // "00123 - INV9" -> "INV9"
            Col("CustomerCode", "Customer Code"), Col("SkuCode", "SKU"),
        };

        using var withSi = Book(("Data", File(Row("SI Number", "Customer Code", "SKU"), Row("00123 - INV9", "A", "S"))));
        var (siSheet, siErrors) = Run(Template(Cols()), withSi);
        Assert.Empty(siErrors);

        var siRule = siSheet!.Columns["InvoiceCode"];
        Assert.Equal("StripLeadingCode", siRule.RuleType);
        Assert.True(ImportRules.TryApply("00123 - INV9", siRule.RuleType, siRule.OptionsJson, out var cleaned, out _));
        Assert.Equal("INV9", cleaned);

        using var withPlain = Book(("Data", File(Row("Invoice No.", "Customer Code", "SKU"), Row("INV9", "A", "S"))));
        var (plainSheet, plainErrors) = Run(Template(Cols()), withPlain);
        Assert.Empty(plainErrors);
        Assert.Equal(ColumnRuleTypes.Direct, plainSheet!.Columns["InvoiceCode"].RuleType);
    }

    // ── Step 3: header-row threshold ─────────────────────────────────────

    [Fact]
    public void Header_row_is_found_below_title_rows()
    {
        using var wb = Book(("Data", File(
            Row("Sales Report"), Array.Empty<string>(),
            Row("Invoice No.", "Customer Code", "SKU", "Qty"), Row("1", "A", "S", "2"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Empty(errors);
        Assert.Equal(3, sheet!.HeaderRow);
    }

    [Fact]
    public void A_single_coincidental_header_is_not_taken_as_the_header_row()
    {
        using var wb = Book(("Data", File(Row("Qty"), Row("x", "y"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Null(sheet);
        Assert.Contains(errors, e => e.Contains("Couldn't find the header row") && e.Contains("row 1") && e.Contains("only 1 of 4"));
    }

    [Fact]
    public void Missing_most_required_fields_means_no_header_row()
    {
        using var wb = Book(("Data", File(Row("SKU", "Qty", "Notes"), Row("S", "2", "n"))));
        var (sheet, errors) = Run(Template(), wb);

        Assert.Null(sheet);
        Assert.Contains(errors, e => e.Contains("Couldn't find the header row"));
    }

    // ── Step 3: Headers match mode ───────────────────────────────────────

    [Fact]
    public void Headers_mode_finds_the_sheet_by_its_headers_not_its_name()
    {
        using var wb = Book(
            ("Summary", File(Row("Total", "10"))),
            ("Whatever it is called", File(Row("Invoice No.", "Customer Code", "SKU", "Qty"), Row("1", "A", "S", "2"))));
        var (sheet, errors) = Run(Template(mode: SheetMatchModes.Headers), wb);

        Assert.Empty(errors);
        Assert.Equal("Whatever it is called", sheet!.Worksheet.Name);
    }

    [Fact]
    public void Headers_mode_with_two_equally_good_sheets_reports_it_instead_of_picking_one()
    {
        var layout = File(Row("Invoice No.", "Customer Code", "SKU", "Qty"), Row("1", "A", "S", "2"));
        using var wb = Book(("Jan", layout), ("Feb", layout));
        var (sheet, errors) = Run(Template(mode: SheetMatchModes.Headers), wb);

        Assert.Null(sheet);
        Assert.Contains(errors, e => e.Contains("equally well") && e.Contains("Jan") && e.Contains("Feb"));
    }

    [Fact]
    public void Headers_mode_with_no_matching_sheet_names_the_closest_one()
    {
        using var wb = Book(("A", File(Row("Qty", "bar"))), ("B", File(Row("baz"))));
        var (sheet, errors) = Run(Template(mode: SheetMatchModes.Headers), wb);

        Assert.Null(sheet);
        Assert.Contains(errors, e => e.Contains("was not found") && e.Contains("closest") && e.Contains("\"A\""));
    }

    [Fact]
    public void Headers_mode_needs_at_least_two_different_fields()
    {
        var template = Template(new List<ImportTemplateColumnEditDto> { Col("InvoiceCode", "Invoice No.") }, SheetMatchModes.Headers);
        var errors = ImportTemplateValidator.Validate(template);

        Assert.Contains(errors, e => e.Contains("at least 2 different fields"));
    }
}