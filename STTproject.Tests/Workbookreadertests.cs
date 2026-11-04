using ClosedXML.Excel;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;
using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Features.User.SalesInvoice.Services;
using STTproject.Models;
using Xunit;

namespace STTproject.Tests.ImportTemplate;

// Step 5a: the database-free workbook reader. It turns a ResolvedWorkbook into logical rows (one shared row budget,
// workbook order, source identity on every row) or into the reasons the workbook can't be read. The importer itself
// needs a database, so flat-file compatibility is checked here, at the point where the rows are produced.
public class WorkbookReaderTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static ImportTemplateColumnEditDto Col(string field, string header, bool required = true) => new()
    {
        FieldKey = field, HeaderText = header, IsRequired = required
    };

    // Everything a Sales Invoice sheet needs on its own.
    private static List<ImportTemplateColumnEditDto> FlatColumns() => new()
    {
        Col("InvoiceCode", "Invoice No."), Col("InvoiceDate", "Date"), Col("SalesManName", "Salesman"),
        Col("CustomerCode", "Customer"), Col("SkuCode", "SKU"), Col("Quantity", "Qty"), Col("UnitOfMeasure", "UOM"),
    };

    private static readonly string[] FlatHeaders = { "Invoice No.", "Date", "Salesman", "Customer", "SKU", "Qty", "UOM" };

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
        Columns = cols ?? FlatColumns()
    };

    private static ImportTemplateEditDto Tpl(params ImportTemplateSheetEditDto[] sheets) => new()
    {
        ImportType = "SalesInvoice",
        Sheets = sheets.ToList()
    };

    private static string[] Row(params string[] cells) => cells;

    private static string[] Line(string invoice, string sku) => Row(invoice, "2026-01-05", "Ana", "C1", sku, "2", "Case");

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

    private static string[][] FlatSheet(int dataRows, string invoicePrefix = "INV") =>
        new[] { Row(FlatHeaders) }
            .Concat(Enumerable.Range(1, dataRows).Select(i => Line($"{invoicePrefix}{i}", $"SKU{i}")))
            .ToArray();

    private static WorkbookReadPlan PlanFor(ImportTemplateEditDto tpl, XLWorkbook wb, int maxRows = int.MaxValue) =>
        WorkbookReader.Plan(WorkbookResolver.Resolve(tpl, wb), "SalesInvoice", maxRows);

    // ── Logical rows ─────────────────────────────────────────────────────

    [Fact]
    public void A_flat_sheet_yields_every_data_row_with_its_source_and_fields()
    {
        using var wb = Book(("Sales", FlatSheet(3)));
        var plan = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb);

        Assert.Empty(plan.Errors);
        var rows = plan.Rows!.Rows().ToList();

        Assert.Equal(new[] { 2, 3, 4 }, rows.Select(r => r.RowNumber));
        Assert.All(rows, r => Assert.Equal(1, r.SheetIndex));
        Assert.Equal(new SourceRowKey(1, 3), rows[1].Source);
        Assert.Equal("INV2", rows[1].Cell("InvoiceCode")!.GetString());
        Assert.Equal("SKU2", rows[1].Cell("SkuCode")!.GetString());
    }

    [Fact]
    public void An_unmapped_field_is_absent_not_an_error()
    {
        using var wb = Book(("Sales", FlatSheet(1)));
        var row = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb).Rows!.Rows().Single();

        Assert.True(row.Has("InvoiceCode"));
        Assert.False(row.Has("NetAmount"));
        Assert.Null(row.Cell("NetAmount"));
        Assert.Null(row.Rule("NetAmount"));
        Assert.Contains("SkuCode", row.FieldKeys);
    }

    [Fact]
    public void A_field_carries_the_rule_of_the_alias_that_matched()
    {
        var cols = FlatColumns();
        cols.Single(c => c.FieldKey == "InvoiceCode").RuleType = "StripLeadingCode";
        using var wb = Book(("Sales", FlatSheet(1)));
        var row = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales", cols: cols)), wb).Rows!.Rows().Single();

        Assert.Equal("StripLeadingCode", row.Rule("InvoiceCode")!.RuleType);
        Assert.Equal(ColumnRuleTypes.Direct, row.Rule("SkuCode")!.RuleType);
    }

    [Fact]
    public void Enumerating_twice_gives_the_same_rows_so_a_pre_scan_sees_what_the_real_read_sees()
    {
        using var wb = Book(("Sales", FlatSheet(5)));
        var rows = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb, maxRows: 3).Rows!;

        Assert.Equal(rows.Rows().Select(r => r.Source), rows.Rows().Select(r => r.Source));
        Assert.Equal(3, rows.Rows().Count());
    }

    // ── Source identity ──────────────────────────────────────────────────

    [Fact]
    public void Source_identity_is_the_workbook_position_with_hidden_sheets_counted()
    {
        using var wb = Book(("Lookup", new[] { Row("x") }), ("Sales", FlatSheet(1)));
        wb.Worksheet("Lookup").Visibility = XLWorksheetVisibility.Hidden;

        var row = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb).Rows!.Rows().Single();

        Assert.Equal(2, row.SheetIndex);
        Assert.Equal(new SourceRowKey(2, 2), row.Source);
    }

    [Fact]
    public void The_same_row_number_on_two_sheets_stays_two_different_sources()
    {
        using var wb = Book(("Sales", FlatSheet(1)), ("Returns", FlatSheet(1)));
        var plan = PlanFor(Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns")), wb);

        var sources = plan.Rows!.Rows().Select(r => r.Source).ToList();

        Assert.Equal(new[] { new SourceRowKey(1, 2), new SourceRowKey(2, 2) }, sources);
    }

    // ── Shared row budget and order ──────────────────────────────────────

    [Fact]
    public void Sheets_are_read_in_workbook_order_not_template_order()
    {
        using var wb = Book(("Sales", FlatSheet(1, "S")), ("Returns", FlatSheet(1, "R")));
        var plan = PlanFor(Tpl(
            Sheet("Returns", SheetMatchModes.Exact, "Returns"),     // template lists Returns first
            Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb);

        Assert.Equal(new[] { "Sales", "Returns" }, plan.Rows!.Sheets.Select(s => s.Inspection.Name));
        Assert.Equal(new[] { "S1", "R1" }, plan.Rows.Rows().Select(r => r.Cell("InvoiceCode")!.GetString()));
    }

    [Fact]
    public void One_row_budget_is_shared_across_sheets()
    {
        using var wb = Book(("Sales", FlatSheet(3, "S")), ("Returns", FlatSheet(3, "R")));
        var plan = PlanFor(Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns")), wb, maxRows: 4);

        var read = plan.Rows!.Rows().Select(r => r.Cell("InvoiceCode")!.GetString()).ToList();

        Assert.Equal(new[] { "S1", "S2", "S3", "R1" }, read);   // not 4 from each sheet
    }

    [Fact]
    public void Blank_rows_use_the_budget_as_they_always_did()
    {
        using var wb = Book(("Sales", new[] { Row(FlatHeaders), Line("A", "S1"), Array.Empty<string>(), Line("B", "S2") }));
        var rows = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb, maxRows: 2).Rows!.Rows().ToList();

        Assert.Equal(new[] { 2, 3 }, rows.Select(r => r.RowNumber));   // the blank row 3 counted, so row 4 is out
    }

    [Fact]
    public void A_zero_budget_yields_nothing()
    {
        using var wb = Book(("Sales", FlatSheet(3)));
        var plan = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb, maxRows: 0);

        Assert.Empty(plan.Rows!.Rows());
    }

    [Fact]
    public void A_header_only_sheet_is_kept_but_reads_no_rows_and_uses_none_of_the_budget()
    {
        var headerCols = new List<ImportTemplateColumnEditDto>
        {
            Col("InvoiceCode", "Invoice No."), Col("InvoiceDate", "Date"), Col("SalesManName", "Salesman"), Col("CustomerCode", "Customer")
        };
        using var wb = Book(
            ("Header", new[] { Row("Invoice No.", "Date", "Salesman", "Customer"), Row("A", "d", "Ana", "C1"), Row("B", "d", "Ana", "C1"), Row("C", "d", "Ana", "C1") }),
            ("Lines", FlatSheet(3)));
        var resolved = WorkbookResolver.Resolve(Tpl(
            Sheet("Header", SheetMatchModes.Exact, "Header", cols: headerCols),
            Sheet("Lines", SheetMatchModes.Exact, "Lines")), wb);
        Assert.True(resolved.IsFullyReadable);

        // Built directly: Plan refuses split workbooks until the join exists, but the budget rule is decided now.
        var rows = new WorkbookRows(resolved.ReadableSheets.OrderBy(s => s.Inspection.SheetIndex).ToList(), maxRows: 2);

        Assert.Equal(new[] { "Header", "Lines" }, rows.Sheets.Select(s => s.Inspection.Name));
        Assert.Equal(new[] { "Lines" }, rows.LineSheets.Select(s => s.Inspection.Name));
        var read = rows.Rows().ToList();
        Assert.Equal(2, read.Count);                                  // the header sheet's three rows didn't use the budget
        Assert.All(read, r => Assert.Equal(2, r.SheetIndex));
    }

    [Theory]
    [InlineData("SkuCode", true)]
    [InlineData("ItemName", true)]
    [InlineData("CaseQuantity", true)]
    [InlineData("CustomerCode", false)]
    [InlineData("InvoiceDate", false)]
    public void A_sheet_is_line_bearing_when_it_maps_a_line_field(string field, bool expected)
    {
        var cols = new List<ImportTemplateColumnEditDto> { Col("InvoiceCode", "Invoice No."), Col(field, "Other") };
        using var wb = Book(("Data", new[] { Row("Invoice No.", "Other"), Row("A", "x") }));
        var sheet = Assert.Single(WorkbookResolver.Resolve(Tpl(Sheet("S", SheetMatchModes.Exact, "Data", cols: cols)), wb).Sheets);

        Assert.Equal(expected, WorkbookReader.IsLineBearing(sheet));
    }

    // ── Flat-file compatibility with the old single-sheet read ───────────

    // Same iteration the importer used to do by hand: rows after the header, up to maxRows of them.
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(100)]
    [InlineData(4)]
    [InlineData(1)]
    public void A_flat_workbook_reads_the_same_rows_cells_and_rules_as_the_old_single_sheet_loop(int maxRows)
    {
        using var wb = Book(("Data", FlatSheet(6)));
        var tpl = Tpl(Sheet("Sales", SheetMatchModes.Any));

        var old = TemplateSheetResolver.Resolve(tpl, wb, _ => { })!;
        var used = old.Worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var headerRow = old.FirstDataRow - 1;
        var limit = maxRows >= int.MaxValue - headerRow ? int.MaxValue : headerRow + maxRows;
        var expectedRows = Enumerable.Range(old.FirstDataRow, Math.Max(0, Math.Min(used, limit) - old.FirstDataRow + 1)).ToList();

        var rows = PlanFor(tpl, wb, maxRows).Rows!.Rows().ToList();

        Assert.Equal(expectedRows, rows.Select(r => r.RowNumber));
        Assert.Equal(old.Headers.Keys.OrderBy(k => k), rows[0].FieldKeys.OrderBy(k => k));

        foreach (var row in rows)
            foreach (var (field, column) in old.Headers)
            {
                Assert.Equal(old.Worksheet.Cell(row.RowNumber, column).GetString(), row.Cell(field)!.GetString());
                Assert.Equal(old.Columns[field].RuleType, row.Rule(field)!.RuleType);
            }
    }

    // ── Plan: strict policy ──────────────────────────────────────────────

    [Fact]
    public void A_clean_flat_workbook_gets_rows_and_no_errors()
    {
        using var wb = Book(("Sales", FlatSheet(2)));
        var plan = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb);

        Assert.Empty(plan.Errors);
        Assert.NotNull(plan.Rows);
        Assert.Equal(new[] { "Sales" }, plan.Sheets.Select(s => s.Inspection.Name));
        Assert.Contains("InvoiceCode", plan.MappedFields);
    }

    [Fact]
    public void A_missing_required_sheet_gives_errors_and_no_rows()
    {
        using var wb = Book(("Sales", FlatSheet(1)));
        var plan = PlanFor(Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns")), wb);

        Assert.Null(plan.Rows);
        Assert.Contains(plan.Errors, e => e.Contains("Returns") && e.Contains("was not found"));
        Assert.Single(plan.Sheets);   // what did resolve is still reported, for the results page
    }

    [Fact]
    public void A_missing_optional_sheet_only_warns()
    {
        using var wb = Book(("Sales", FlatSheet(1)));
        var plan = PlanFor(Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns", required: false)), wb);

        Assert.Empty(plan.Errors);
        Assert.NotNull(plan.Rows);
        Assert.Contains(plan.Warnings, w => w.Contains("Returns"));
    }

    [Fact]
    public void A_matched_sheet_with_a_column_problem_fails_the_workbook_under_the_current_strict_policy()
    {
        using var wb = Book(("Sales", FlatSheet(1)), ("Returns", new[] { Row("SKU", "Qty"), Row("S", "1") }));
        var plan = PlanFor(Tpl(
            Sheet("Sales", SheetMatchModes.Exact, "Sales"),
            Sheet("Returns", SheetMatchModes.Exact, "Returns", headerMode: HeaderRowModes.Fixed, headerRow: 1)), wb);

        Assert.Null(plan.Rows);
        Assert.Contains(plan.Errors, e => e.StartsWith("Sheet \"Returns\":") && e.Contains("Invoice No."));
        Assert.Equal(new[] { "Sales" }, plan.Sheets.Select(s => s.Inspection.Name));
    }

    [Fact]
    public void An_empty_matched_sheet_is_reported_with_its_name()
    {
        using var wb = Book(("Sales", Array.Empty<string[]>()));
        var plan = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales")), wb);

        Assert.Null(plan.Rows);
        Assert.Contains(plan.Errors, e => e.Contains("Sales") && e.Contains("empty"));
    }

    [Fact]
    public void A_truncated_assignment_gives_errors_and_no_rows()
    {
        using var wb = Book(Enumerable.Range(1, 7).Select(i => ($"S{i}", FlatSheet(1))).ToArray());
        var plan = PlanFor(Tpl(Enumerable.Range(1, 7)
            .Select(i => Sheet($"T{i}", SheetMatchModes.Contains, "S")).ToArray()), wb);

        Assert.Null(plan.Rows);
        Assert.Empty(plan.Sheets);
        Assert.Contains(plan.Errors, e => e == WorkbookMatcher.TruncatedMessage);
    }

    // ── Plan: required fields (the registry is the one definition) ───────

    [Fact]
    public void Missing_required_fields_are_named_the_way_the_registry_names_them()
    {
        var cols = FlatColumns().Where(c => c.FieldKey != "SalesManName" && c.FieldKey != "InvoiceDate").ToList();
        using var wb = Book(("Sales", new[] { Row("Invoice No.", "Customer", "SKU", "Qty", "UOM"), Row("A", "C1", "S", "1", "Case") }));
        var plan = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales", cols: cols)), wb);

        Assert.Null(plan.Rows);
        var message = Assert.Single(plan.Errors);
        Assert.StartsWith("Missing required columns:", message);
        Assert.Contains("Invoice Date is required.", message);
        Assert.Contains("Salesman is required.", message);
    }

    [Fact]
    public void Quantity_without_a_unit_of_measure_is_refused()
    {
        var cols = FlatColumns().Where(c => c.FieldKey != "UnitOfMeasure").ToList();
        using var wb = Book(("Sales", new[] { Row("Invoice No.", "Date", "Salesman", "Customer", "SKU", "Qty"), Row("A", "d", "Ana", "C1", "S", "1") }));
        var plan = PlanFor(Tpl(Sheet("Sales", SheetMatchModes.Exact, "Sales", cols: cols)), wb);

        Assert.Null(plan.Rows);
        Assert.Contains(plan.Errors, e => e.Contains("Unit of Measure"));
    }

    [Fact]
    public void The_legacy_validator_delegates_to_the_registry_and_says_the_same_thing()
    {
        var mapped = new Dictionary<string, int> { ["InvoiceCode"] = 1, ["SkuCode"] = 2, ["Quantity"] = 3 };

        var (isValid, message) = InvoiceDataValidator.ValidateRequiredHeaders(mapped);

        Assert.False(isValid);
        Assert.Equal(
            WorkbookReader.MissingColumnsMessage(ImportFieldRegistry.MissingRequired("SalesInvoice", mapped.Keys)),
            message);
        Assert.Contains("Unit of Measure", message);
    }

    [Fact]
    public void A_complete_set_of_fields_passes_the_legacy_validator()
    {
        var mapped = FlatColumns().Select((c, i) => (c.FieldKey!, i + 1)).ToDictionary(x => x.Item1, x => x.Item2);

        Assert.True(InvoiceDataValidator.ValidateRequiredHeaders(mapped).IsValid);
    }

    // ── Plan: split workbooks are refused until the join exists ──────────

    [Fact]
    public void Invoices_split_across_a_header_sheet_and_a_detail_sheet_are_refused_not_half_read()
    {
        var headerCols = new List<ImportTemplateColumnEditDto>
        {
            Col("InvoiceCode", "Invoice No."), Col("InvoiceDate", "Date"), Col("SalesManName", "Salesman"), Col("CustomerCode", "Customer")
        };
        var lineCols = new List<ImportTemplateColumnEditDto>
        {
            Col("InvoiceCode", "Invoice No."), Col("SkuCode", "SKU"), Col("Quantity", "Qty"), Col("UnitOfMeasure", "UOM")
        };

        using var wb = Book(
            ("Header", new[] { Row("Invoice No.", "Date", "Salesman", "Customer"), Row("A", "d", "Ana", "C1") }),
            ("Lines", new[] { Row("Invoice No.", "SKU", "Qty", "UOM"), Row("A", "S", "1", "Case") }));

        var plan = PlanFor(Tpl(
            Sheet("Header", SheetMatchModes.Exact, "Header", cols: headerCols),
            Sheet("Lines", SheetMatchModes.Exact, "Lines", cols: lineCols)), wb);

        Assert.Null(plan.Rows);
        Assert.Equal(2, plan.Sheets.Count);                                        // both resolved fine
        Assert.DoesNotContain(plan.Errors, e => e.StartsWith("Missing required"));  // together they'd be complete
        Assert.Contains(plan.Errors, e => e.Contains("Header") && e.Contains("on its own") && e.Contains("split"));
        Assert.Contains(plan.Errors, e => e.Contains("Lines") && e.Contains("on its own"));
    }

    [Fact]
    public void Two_complete_sheets_of_the_same_shape_are_fine()
    {
        using var wb = Book(("Jan", FlatSheet(2, "J")), ("Feb", FlatSheet(2, "F")));
        var plan = PlanFor(Tpl(
            Sheet("Jan", SheetMatchModes.Exact, "Jan"),
            Sheet("Feb", SheetMatchModes.Exact, "Feb")), wb);

        Assert.Empty(plan.Errors);
        Assert.Equal(4, plan.Rows!.Rows().Count());
    }
}