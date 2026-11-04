using ClosedXML.Excel;
using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Features.User.SalesInvoice.Services;
using STTproject.Models;
using Xunit;

namespace STTproject.Tests.SalesInvoice;

// Step 3: where a row came from. Row numbers alone collide across sheets, so every issue, error row, item and
// report line carries the worksheet's position in the original workbook as well.
public class SourceIdentityTests
{
    private static SourceRowKey K(int sheet, int row) => new(sheet, row);

    private static ImportSalesInvoiceResult SingleSheet()
    {
        var r = new ImportSalesInvoiceResult();
        r.GetOrAddSheet(1, "Sales");
        return r;
    }

    private static ImportSalesInvoiceResult TwoSheets()
    {
        var r = new ImportSalesInvoiceResult();
        r.GetOrAddSheet(1, "Sales");
        r.GetOrAddSheet(3, "Free Goods");   // position 2 is a hidden sheet in the original workbook
        return r;
    }

    // ── Display helper ───────────────────────────────────────────────────

    [Fact]
    public void A_single_sheet_file_keeps_the_plain_row_wording()
    {
        var r = SingleSheet();

        Assert.Equal("Row 5", r.FormatSource(K(1, 5)));
        Assert.Equal("row 5", r.FormatRows(new[] { K(1, 5) }));
        Assert.Equal("rows 3, 4 & 9", r.FormatRows(new[] { K(1, 9), K(1, 3), K(1, 4) }));
    }

    [Fact]
    public void A_multi_sheet_file_names_the_sheet()
    {
        var r = TwoSheets();

        Assert.Equal("Sales!5", r.FormatSource(K(1, 5)));
        Assert.Equal("Free Goods!5", r.FormatSource(K(3, 5)));
        Assert.Equal("rows Sales!5 & Free Goods!5", r.FormatRows(new[] { K(3, 5), K(1, 5) }));
    }

    [Fact]
    public void A_long_list_is_cut_after_five()
    {
        var r = SingleSheet();
        var rows = Enumerable.Range(1, 7).Select(i => K(1, i));

        Assert.Equal("rows 1, 2, 3, 4, 5 and 2 more", r.FormatRows(rows));
    }

    [Fact]
    public void A_file_level_source_reads_as_the_file()
    {
        Assert.Equal("File", TwoSheets().FormatSource(default));
        Assert.True(default(SourceRowKey).IsFileLevel);
    }

    // ── Issues ───────────────────────────────────────────────────────────

    [Fact]
    public void An_error_added_with_a_source_keeps_its_sheet()
    {
        var r = TwoSheets();
        r.AddError(K(3, 7), "INV1", "SKU 'X' was not found.", "SkuCode");

        var issue = Assert.Single(r.Issues);
        Assert.Equal(3, issue.SheetIndex);
        Assert.Equal(7, issue.RowNumber);
        Assert.Equal(K(3, 7), issue.Source);
        Assert.Equal("SkuCode", issue.ColumnName);
    }

    [Fact]
    public void An_error_added_by_row_number_alone_is_file_level()
    {
        var r = new ImportSalesInvoiceResult();
        r.AddError(0, string.Empty, "Import file is missing or unreadable.");

        Assert.True(Assert.Single(r.Issues).Source.IsFileLevel);
    }

    [Fact]
    public void The_same_row_number_on_two_sheets_is_two_different_sources()
    {
        var r = TwoSheets();
        r.AddError(K(1, 5), "INV1", "msg");
        r.AddError(K(3, 5), "INV1", "msg");

        Assert.Equal(2, r.Issues.Select(i => i.Source).Distinct().Count());
        // grouping by row number alone is exactly the collision this step removes
        Assert.Single(r.Issues.Select(i => i.RowNumber).Distinct());
    }

    // ── Aggregated items keep every source ───────────────────────────────

    private static (InputItemModel Item, bool IsFreeItem) Raw(int sheet, int row, int qty, bool free = false) =>
        (new InputItemModel
        {
            ItemCode = "SKU1",
            ItemName = "Widget",
            SubdItemId = 10,
            ItemsUomId = 20,
            UomName = "Case",
            Quantity = qty,
            Amount = qty * 5m,
            LineItemId = row,
            SourceRowNumber = row,
            SourceSheetIndex = sheet,
            SourceRows = new List<SourceRowKey> { K(sheet, row) }
        }, free);

    [Fact]
    public void Rows_from_one_sheet_aggregate_with_that_sheet_and_all_rows()
    {
        var items = ImportSalesInvoiceService.BuildAggregatedItems(new() { Raw(1, 5, 2), Raw(1, 9, 3) }, "Invoice");

        var item = Assert.Single(items);
        Assert.Equal(5, item.Quantity);
        Assert.Equal(1, item.SourceSheetIndex);
        Assert.Equal(new[] { K(1, 5), K(1, 9) }, item.SourceRows);
    }

    [Fact]
    public void An_item_spanning_two_sheets_has_no_single_sheet_and_keeps_both_sources()
    {
        var items = ImportSalesInvoiceService.BuildAggregatedItems(new() { Raw(3, 2, 1), Raw(1, 8, 2) }, "Invoice");

        var item = Assert.Single(items);
        Assert.Equal(3, item.Quantity);
        Assert.Null(item.SourceSheetIndex);                       // never the lowest sheet index
        Assert.Equal(new[] { K(1, 8), K(3, 2) }, item.SourceRows); // ordered by sheet, then row
    }

    // ── Error report ─────────────────────────────────────────────────────

    private static XLWorkbook Report(ImportSalesInvoiceResult result, params ErrorReportEntry[] entries)
    {
        var bytes = new DownloadTemplateService(null!).GenerateErrorReportExcel(result, entries);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    [Fact]
    public void A_single_sheet_report_is_one_errors_sheet_with_the_source_row()
    {
        var r = SingleSheet();
        var sales = r.GetOrAddSheet(1, "Sales");
        sales.RawHeaders = new() { "Invoice", "Qty" };
        sales.RawRows[5] = new[] { "INV1", "2" };

        using var wb = Report(r, new ErrorReportEntry(5, "SKU", "bad sku", 1));

        var ws = Assert.Single(wb.Worksheets);
        Assert.Equal("Errors", ws.Name);
        Assert.Equal("Qty", ws.Cell(1, 2).GetString());
        Assert.Equal("2", ws.Cell(2, 2).GetString());
        Assert.Equal("bad sku", ws.Cell(2, 4).GetString());
        Assert.Equal(5, ws.Cell(2, 5).GetValue<int>());
    }

    [Fact]
    public void A_multi_sheet_report_has_one_sheet_per_source_each_with_its_own_columns_and_row()
    {
        var r = TwoSheets();
        var salesSheet = r.Sheets[1];
        var freeSheet = r.Sheets[3];
        salesSheet.RawHeaders = new() { "Invoice", "Qty" };
        freeSheet.RawHeaders = new() { "Doc", "Free Qty" };
        salesSheet.RawRows[5] = new[] { "INV1", "2" };      // same row number on both sheets
        freeSheet.RawRows[5] = new[] { "INV1", "1" };

        using var wb = Report(r,
            new ErrorReportEntry(5, "SKU", "bad sku", 1),
            new ErrorReportEntry(5, "UOM", "bad uom", 3));

        Assert.Equal(new[] { "Sales", "Free Goods" }, wb.Worksheets.Select(w => w.Name));

        var salesTab = wb.Worksheet("Sales");
        Assert.Equal("Qty", salesTab.Cell(1, 2).GetString());
        Assert.Equal("2", salesTab.Cell(2, 2).GetString());
        Assert.Equal("bad sku", salesTab.Cell(2, 4).GetString());

        var freeTab = wb.Worksheet("Free Goods");
        Assert.Equal("Free Qty", freeTab.Cell(1, 2).GetString());
        Assert.Equal("1", freeTab.Cell(2, 2).GetString());
        Assert.Equal("bad uom", freeTab.Cell(2, 4).GetString());
    }
}