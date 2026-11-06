using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;
using STTproject.Features.Admin.ImportTemplate.Validators;
using Xunit;

namespace STTproject.Tests.ImportTemplate;

// Step 4.5: WorkbookResolver (sheet assignment + header row + column resolution) and the Template Tester built on it.
// IsEligible (the sheets were assigned) and IsFullyReadable (every matched sheet also resolves cleanly) are different
// questions; several tests exist to keep them apart, because Step 6 decides what a problem sheet means.
public class ResolvedWorkbookTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static ImportTemplateColumnEditDto Col(string field, string header, bool required = true) => new()
    {
        FieldKey = field,
        HeaderText = header,
        IsRequired = required
    };

    // Three required fields and one optional, so a file can lack some and still be recognised in Headers mode.
    private static List<ImportTemplateColumnEditDto> StandardColumns() => new()
    {
        Col("InvoiceCode", "Invoice No."), Col("InvoiceCode", "SI Number"),
        Col("CustomerCode", "Customer Code"), Col("CustomerCode", "Cust. Code"),
        Col("SkuCode", "SKU"),
        Col("Quantity", "Qty", required: false),
    };

    // One column per field, as the validator requires (a field is mapped once per sheet; extra headers are aliases).
    // The tester runs ValidateStructure first, so tester tests need columns that pass it.
    private static List<ImportTemplateColumnEditDto> SavableColumns() => new()
    {
        Col("InvoiceCode", "Invoice No."), Col("CustomerCode", "Customer Code"),
        Col("SkuCode", "SKU"), Col("Quantity", "Qty", required: false),
    };

    private static ImportTemplateSheetEditDto Sheet(
        string label, string mode, string? value = null, bool required = true,
        List<ImportTemplateColumnEditDto>? cols = null,
        string headerMode = HeaderRowModes.Automatic, int? headerRow = null) => new()
        {
            SheetLabel = label,
            SheetMatchMode = mode,
            SheetMatchValue = value,
            IsRequired = required,
            HeaderRowMode = headerMode,
            HeaderRowNumber = headerRow,
            Columns = cols ?? StandardColumns()
        };

    private static ImportTemplateEditDto Tpl(params ImportTemplateSheetEditDto[] sheets) => new()
    {
        ImportType = "SalesInvoice",
        Sheets = sheets.ToList()
    };

    private static string[] Row(params string[] cells) => cells;
    private static string[][] File(params string[][] rows) => rows;

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

    private static MemoryStream Save(XLWorkbook wb)
    {
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static readonly string[] GoodHeaders = { "Invoice No.", "Customer Code", "SKU", "Qty" };

    // ── Fixed mode: the configured row is authoritative ──────────────────

    [Fact]
    public void Fixed_header_row_matches_by_name_even_when_required_columns_are_missing()
    {
        // Only SKU and Qty: in Headers mode this sheet would not be recognised (see the next test).
        using var wb = Book(("Data", File(Row("SKU", "Qty"), Row("S", "2"))));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, "Data", headerMode: HeaderRowModes.Fixed, headerRow: 1));

        var resolved = WorkbookResolver.Resolve(tpl, wb);

        Assert.True(resolved.IsEligible);                 // the sheet was assigned...
        Assert.False(resolved.IsFullyReadable);           // ...but its columns don't resolve
        var sheet = Assert.Single(resolved.Sheets);
        Assert.Equal(1, sheet.HeaderRow);
        Assert.Equal(2, sheet.FirstDataRow);
        Assert.False(sheet.IsReadable);
        Assert.Contains("Invoice No.", sheet.Columns!.MissingRequired);
        Assert.Contains("Customer Code", sheet.Columns.MissingRequired);
        Assert.Contains(sheet.Problems, p => p.Contains("Required field") && p.Contains("Invoice No."));
        Assert.Single(resolved.ProblemSheets);
        Assert.Empty(resolved.ReadableSheets);
    }

    [Fact]
    public void Headers_mode_does_not_accept_the_same_file()
    {
        using var wb = Book(("Data", File(Row("SKU", "Qty"), Row("S", "2"))));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Headers));

        var resolved = WorkbookResolver.Resolve(tpl, wb);

        Assert.False(resolved.IsEligible);
        Assert.Empty(resolved.Sheets);                    // never matched, so there is nothing to resolve
        Assert.Single(resolved.Match.MissingRequired);
    }

    [Fact]
    public void A_valid_assignment_with_a_column_problem_is_eligible_but_not_fully_readable()
    {
        using var wb = Book(
            ("Sales", File(Row(GoodHeaders), Row("1", "A", "S", "2"))),
            ("Returns", File(Row("SKU", "Qty"), Row("S", "1"))));
        var tpl = Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns", headerMode: HeaderRowModes.Fixed, headerRow: 1));

        var resolved = WorkbookResolver.Resolve(tpl, wb);

        Assert.True(resolved.IsEligible);
        Assert.False(resolved.IsFullyReadable);
        Assert.Equal(new[] { "Sales" }, resolved.ReadableSheets.Select(s => s.Label));
        Assert.Equal(new[] { "Returns" }, resolved.ProblemSheets.Select(s => s.Label));
    }

    // ── Empty matched sheets ─────────────────────────────────────────────

    [Fact]
    public void An_empty_matched_sheet_is_a_sheet_problem_not_a_silent_skip()
    {
        using var wb = Book(("Sales", Array.Empty<string[]>()));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales"));

        var resolved = WorkbookResolver.Resolve(tpl, wb);

        Assert.True(resolved.IsEligible);                 // Exact matches by name, empty or not
        var sheet = Assert.Single(resolved.Sheets);
        Assert.Null(sheet.HeaderRow);
        Assert.Null(sheet.Columns);
        Assert.False(sheet.IsReadable);
        Assert.Contains(sheet.Problems, p => p.Contains("empty"));
        Assert.False(resolved.IsFullyReadable);
    }

    // ── Required versus optional ─────────────────────────────────────────

    [Fact]
    public void Missing_optional_sheet_leaves_the_workbook_eligible_and_resolves_the_rest()
    {
        using var wb = Book(("Sales", File(Row(GoodHeaders), Row("1", "A", "S", "2"))));
        var tpl = Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns", required: false));

        var resolved = WorkbookResolver.Resolve(tpl, wb);

        Assert.True(resolved.IsEligible);
        Assert.True(resolved.IsFullyReadable);
        Assert.Single(resolved.Sheets);
        Assert.Empty(resolved.Errors);
        Assert.Contains(resolved.Warnings, w => w.Contains("Returns") && w.Contains("skipped"));
    }

    [Fact]
    public void Missing_required_sheet_makes_the_workbook_ineligible_but_still_resolves_what_was_found()
    {
        using var wb = Book(("Sales", File(Row(GoodHeaders), Row("1", "A", "S", "2"))));
        var tpl = Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns"));

        var resolved = WorkbookResolver.Resolve(tpl, wb);

        Assert.False(resolved.IsEligible);
        Assert.False(resolved.IsFullyReadable);
        Assert.Single(resolved.Sheets);
        Assert.Contains(resolved.Errors, e => e.Contains("Returns") && e.Contains("was not found"));
    }

    // ── Header row position ──────────────────────────────────────────────

    private static string[][] HeaderAtRow25()
    {
        var rows = new List<string[]> { Row("Quarterly report") };
        while (rows.Count < 24) rows.Add(Array.Empty<string>());
        rows.Add(Row(GoodHeaders));
        rows.Add(Row("1", "A", "S", "2"));
        return rows.ToArray();
    }

    [Fact]
    public void Fixed_header_row_below_the_scan_range_is_read()
    {
        using var wb = Book(("Data", HeaderAtRow25()));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, "Data", headerMode: HeaderRowModes.Fixed, headerRow: 25));

        var sheet = Assert.Single(WorkbookResolver.Resolve(tpl, wb).Sheets);

        Assert.True(sheet.IsReadable);
        Assert.Equal(25, sheet.HeaderRow);
        Assert.Equal(26, sheet.FirstDataRow);
        Assert.Equal(1, sheet.Columns!.Headers["InvoiceCode"]);
    }

    [Fact]
    public void Automatic_header_search_stops_at_the_scan_range()
    {
        using var wb = Book(("Data", HeaderAtRow25()));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, "Data"));

        var sheet = Assert.Single(WorkbookResolver.Resolve(tpl, wb).Sheets);

        Assert.False(sheet.IsReadable);
        Assert.Null(sheet.HeaderRow);
        Assert.Contains(sheet.Problems, p => p.Contains("Couldn't find the header row"));
    }

    [Fact]
    public void Header_row_is_found_below_title_rows_and_a_multi_row_header_is_joined()
    {
        using var wb = Book(("Data", File(
            Row("Sales Report"),
            Row("Invoice", "Customer", "SKU"),
            Row("No.", "Code", ""),
            Row("1", "A", "S"))));
        var cols = new List<ImportTemplateColumnEditDto>
        {
            Col("InvoiceCode", "Invoice No."), Col("CustomerCode", "Customer Code"), Col("SkuCode", "SKU")
        };
        var sheetDto = Sheet("Sales", SheetMatchModes.Exact, "Data", cols: cols);
        sheetDto.HeaderRowCount = 2;

        var sheet = Assert.Single(WorkbookResolver.Resolve(Tpl(sheetDto), wb).Sheets);

        Assert.True(sheet.IsReadable);
        Assert.Equal(2, sheet.HeaderRow);
        Assert.Equal(4, sheet.FirstDataRow);
    }

    // ── Aliases and ambiguity ────────────────────────────────────────────

    [Fact]
    public void Two_aliases_of_one_field_is_an_ambiguity_problem()
    {
        using var wb = Book(("Data", File(Row("Invoice No.", "SI Number", "Customer Code", "SKU"), Row("1", "1", "A", "S"))));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, "Data"));

        var sheet = Assert.Single(WorkbookResolver.Resolve(tpl, wb).Sheets);

        Assert.False(sheet.IsReadable);
        Assert.Contains(sheet.Problems, p => p.Contains("Alias ambiguity"));
        Assert.DoesNotContain("InvoiceCode", sheet.Columns!.Headers.Keys);
    }

    [Fact]
    public void A_repeated_source_header_is_a_duplicate_header_problem()
    {
        using var wb = Book(("Data", File(Row("Invoice No.", "Customer Code", "SKU", "SKU"), Row("1", "A", "S", "S"))));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, "Data"));

        var sheet = Assert.Single(WorkbookResolver.Resolve(tpl, wb).Sheets);

        Assert.Contains(sheet.Problems, p => p.Contains("Duplicate header") && p.Contains("C and D"));
    }

    // ── Single-sheet backward compatibility ──────────────────────────────

    // The resolver must read a single-sheet file exactly as the old TemplateSheetResolver.Resolve did.
    public static IEnumerable<object[]> SingleSheetFiles()
    {
        yield return new object[] { File(Row(GoodHeaders), Row("1", "A", "S", "2")) };
        yield return new object[] { File(Row("SI Number", "Cust. Code", "SKU"), Row("1", "A", "S")) };
        yield return new object[] { File(Row("Sales Report"), Array.Empty<string>(), Row(GoodHeaders), Row("1", "A", "S", "2")) };
    }

    [Theory]
    [MemberData(nameof(SingleSheetFiles))]
    public void A_single_sheet_template_resolves_like_the_old_resolver(string[][] rows)
    {
        using var wb = Book(("Data", rows));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Any));

        var errors = new List<string>();
        var old = TemplateSheetResolver.Resolve(tpl, wb, errors.Add);
        var sheet = Assert.Single(WorkbookResolver.Resolve(tpl, wb).Sheets);

        Assert.NotNull(old);
        Assert.Empty(errors);
        Assert.True(sheet.IsReadable);
        Assert.Equal(old!.HeaderRow, sheet.HeaderRow);
        Assert.Equal(old.FirstDataRow, sheet.FirstDataRow);
        Assert.Equal(old.Headers.OrderBy(k => k.Key), sheet.Columns!.Headers.OrderBy(k => k.Key));
    }

    // ── Truncation ───────────────────────────────────────────────────────

    // Seven interchangeable required sheets: 7! = 5,040 equally good assignments, over the 1,000 cap.
    // Contains (not Any) so the template passes structural validation, which the tester runs first.
    private static (ImportTemplateEditDto Template, XLWorkbook Workbook) Truncating()
    {
        var wb = Book(Enumerable.Range(1, 7).Select(i => ($"S{i}", File(Row("x")))).ToArray());
        var tpl = Tpl(Enumerable.Range(1, 7)
            .Select(i => Sheet($"T{i}", SheetMatchModes.Contains, "S", cols: new() { Col("InvoiceCode", "Invoice No.") }))
            .ToArray());
        return (tpl, wb);
    }

    [Fact]
    public void A_truncated_assignment_exposes_no_readable_sheets()
    {
        var (tpl, wb) = Truncating();
        using (wb)
        {
            var resolved = WorkbookResolver.Resolve(tpl, wb);

            Assert.True(resolved.Match.IsTruncated);
            Assert.False(resolved.IsEligible);
            Assert.False(resolved.IsFullyReadable);
            Assert.Empty(resolved.Sheets);
            Assert.Empty(resolved.ReadableSheets);
            Assert.Contains(resolved.Errors, e => e.Contains("equally good"));
        }
    }

    [Fact]
    public void The_tester_reports_a_truncated_assignment_as_a_template_error_with_no_sheets()
    {
        var (tpl, wb) = Truncating();
        using (wb)
        using (var stream = Save(wb))
        {
            var result = new ImportTemplateTestService().Test(tpl, stream, "file.xlsx");

            Assert.Contains(result.Errors, e => e == WorkbookMatcher.TruncatedMessage);
            Assert.Empty(result.Sheets);
        }
    }

    // ── Ambiguity: resolver and tester must agree ────────────────────────

    [Fact]
    public void Competing_template_sheets_get_the_same_ambiguity_in_the_resolver_and_the_tester()
    {
        using var wb = Book(("Data1", File(Row("x"))), ("Data2", File(Row("x"))));
        var tpl = Tpl(
            Sheet("A", SheetMatchModes.Contains, "Data", cols: new() { Col("InvoiceCode", "Invoice No.") }),
            Sheet("B", SheetMatchModes.Contains, "Data", cols: new() { Col("InvoiceCode", "Invoice No.") }));

        var resolved = WorkbookResolver.Resolve(tpl, wb);
        var amb = Assert.Single(resolved.Match.Ambiguities);
        Assert.False(resolved.IsEligible);
        Assert.Empty(resolved.Sheets);

        using var stream = Save(wb);
        var result = new ImportTemplateTestService().Test(tpl, stream, "file.xlsx");

        Assert.Equal(2, result.Sheets.Count);
        Assert.All(result.Sheets, s =>
        {
            Assert.Null(s.ExcelSheetName);
            Assert.Contains(amb.Message, s.Problems);
        });
    }

    // ── Tester ───────────────────────────────────────────────────────────

    [Fact]
    public void Tester_results_follow_template_order_not_workbook_order()
    {
        using var wb = Book(
            ("Sales", File(Row(GoodHeaders), Row("1", "A", "S", "2"))),
            ("Returns", File(Row(GoodHeaders), Row("2", "B", "T", "1"))));
        var tpl = Tpl(
            Sheet("Returns label", SheetMatchModes.Exact, "Returns", cols: SavableColumns()),
            Sheet("Sales label", SheetMatchModes.Exact, "Sales", cols: SavableColumns()));

        using var stream = Save(wb);
        var result = new ImportTemplateTestService().Test(tpl, stream, "file.xlsx");

        Assert.Equal(new[] { "Returns label", "Sales label" }, result.Sheets.Select(s => s.Label));
        Assert.Equal(new[] { "Returns", "Sales" }, result.Sheets.Select(s => s.ExcelSheetName));
        Assert.All(result.Sheets, s => Assert.Single(s.Rows));
    }

    [Fact]
    public void Tester_lists_a_missing_sheet_in_its_template_position_and_notes_unused_and_hidden_sheets()
    {
        using var wb = Book(
            ("Sales", File(Row(GoodHeaders), Row("1", "A", "S", "2"))),
            ("Notes", File(Row("x"))),
            ("Lookup", File(Row("x"))));
        wb.Worksheet("Lookup").Visibility = XLWorksheetVisibility.Hidden;
        var tpl = Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales", cols: SavableColumns()),
            Sheet("Returns", SheetMatchModes.Exact, "Returns", required: false, cols: SavableColumns()));

        using var stream = Save(wb);
        var result = new ImportTemplateTestService().Test(tpl, stream, "file.xlsx");

        Assert.Equal(new[] { "Sales", "Returns" }, result.Sheets.Select(s => s.Label));
        Assert.Contains(result.Sheets[1].Problems, p => p.Contains("optional") && p.Contains("skipped"));
        Assert.Contains(result.Notes, n => n.Contains("Notes") && n.Contains("isn't used"));
        Assert.Contains(result.Notes, n => n.Contains("Lookup") && n.Contains("hidden"));
    }

    [Fact]
    public void An_incomplete_template_can_be_tested_but_not_saved()
    {
        using var wb = Book(("Data", File(Row(GoodHeaders), Row("1", "A", "S", "2"))));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, "Data", cols: SavableColumns()));   // SalesInvoice needs far more than these columns

        Assert.NotEmpty(ImportTemplateValidator.Validate(tpl));         // save: blocked
        Assert.Empty(ImportTemplateValidator.ValidateStructure(tpl));   // test: allowed

        using var stream = Save(wb);
        var result = new ImportTemplateTestService().Test(tpl, stream, "file.xlsx");

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.StartsWith("Can't be saved yet:"));
        Assert.Single(Assert.Single(result.Sheets).Rows);
    }

    [Fact]
    public void A_structurally_broken_template_still_blocks_the_tester()
    {
        using var wb = Book(("Data", File(Row(GoodHeaders))));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Exact, value: null));   // Exact needs the text to match

        using var stream = Save(wb);
        var result = new ImportTemplateTestService().Test(tpl, stream, "file.xlsx");

        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.Sheets);
    }
}

// Required groups are checked per sheet: the importer reads every non-Ignore sheet on its own, so groups are NOT
// pooled across sheets. Save enforces this (Validate); the chips use the same rule (MissingRequiredGroups).
public class PooledRequiredGroupTests
{
    private static ImportTemplateColumnEditDto Col(string field, string header) => new()
    {
        FieldKey = field,
        HeaderText = header
    };

    private static ImportTemplateSheetEditDto Sheet(string label, bool required, params (string Field, string Header)[] cols) => new()
    {
        SheetLabel = label,
        SheetMatchMode = SheetMatchModes.Exact,
        SheetMatchValue = label,
        IsRequired = required,
        Columns = cols.Select(c => Col(c.Field, c.Header)).ToList()
    };

    private static ImportTemplateEditDto Tpl(params ImportTemplateSheetEditDto[] sheets) => new()
    {
        ImportType = "SalesInvoice",
        Sheets = sheets.ToList()
    };

    private static ImportTemplateSheetEditDto Header(bool required = true) => Sheet("Header", required,
        ("InvoiceCode", "Invoice No."), ("InvoiceDate", "Date"), ("SalesManName", "Salesman"), ("CustomerCode", "Customer"));

    private static ImportTemplateSheetEditDto Lines(bool required = true) => Sheet("Lines", required,
        ("InvoiceCode", "Invoice No."), ("SkuCode", "SKU"), ("Quantity", "Qty"), ("UnitOfMeasure", "UOM"));

    [Fact]
    public void Required_groups_split_across_sheets_are_not_pooled()
    {
        var errors = ImportTemplateValidator.Validate(Tpl(sheets: new[] { Header(), Lines() }));

        Assert.Contains(errors, e => e.StartsWith("Sheet \"Header\":") && e.Contains("SKU Code"));
        Assert.Contains(errors, e => e.StartsWith("Sheet \"Lines\":") && e.Contains("Invoice Date"));
        Assert.Contains(errors, e => e.Contains("Each sheet is read on its own"));
    }

    [Fact]
    public void An_optional_sheet_must_be_complete_on_its_own_too()
    {
        var errors = ImportTemplateValidator.Validate(Tpl(sheets: new[] { Header(), Lines(required: false) }));

        Assert.Contains(errors, e => e.StartsWith("Sheet \"Lines\":"));
    }

    [Fact]
    public void An_ignored_sheet_is_never_read_so_it_has_nothing_to_cover()
    {
        var ignored = Lines();
        ignored.SheetMatchMode = SheetMatchModes.Ignore;
        ignored.Columns.Clear();   // Ignore sheets can't hold columns

        var errors = ImportTemplateValidator.Validate(Tpl(sheets: new[] { Header(), ignored }));

        Assert.Contains(errors, e => e.Contains("SKU Code"));                       // Header is still incomplete
        Assert.DoesNotContain(errors, e => e.Contains("Lines"));                    // but the ignored sheet adds nothing
        Assert.Empty(ImportTemplateValidator.MissingRequiredGroups(ignored, "SalesInvoice"));
    }

    [Fact]
    public void A_single_incomplete_sheet_reports_its_gaps_without_a_sheet_prefix()
    {
        var errors = ImportTemplateValidator.Validate(Tpl(sheets: new[] { Header() }));

        Assert.Contains(errors, e => e.Contains("SKU Code"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Sheet "));
    }

    [Fact]
    public void Chips_and_save_use_the_same_rule()
    {
        var tpl = Tpl(sheets: new[] { Header() });

        Assert.Equal(
            ImportTemplateValidator.MissingRequiredGroups(tpl.Sheets[0], tpl.ImportType),
            ImportTemplateValidator.Validate(tpl));
    }
}