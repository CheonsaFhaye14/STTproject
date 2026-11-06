using System.Text.Json;
using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Features.User.SalesInvoice.Services;
using STTproject.Services;
using Xunit;

// Adjust this namespace (and the project's reference to the web project) to match your test project.
namespace STTproject.Tests.SalesInvoice;

public class ImportRowErrorAggregatorTests
{
    private static ImportSalesInvoiceIssue Issue(
        int row, string invoice, string? code, string? field = null, string? value = null,
        string message = "msg", int sheet = 1, string? context = null, bool hold = false, int? suggestions = null) =>
        new(RowNumber: row, InvoiceNumber: invoice, Header: message, Body: string.Empty, Footer: string.Empty,
            ColumnName: field, SuggestionCount: suggestions, IsHoldNote: hold, SheetIndex: sheet,
            Code: code, Value: value, Context: context);

    private static List<ImportErrorEntry> Run(params ImportSalesInvoiceIssue[] issues) => Run(issues.AsEnumerable());

    private static List<ImportErrorEntry> Run(IEnumerable<ImportSalesInvoiceIssue> issues)
    {
        var result = new ImportSalesInvoiceResult();
        result.GetOrAddSheet(1, "STORE");
        foreach (var i in issues) result.AddIssue(i);
        return ImportRowErrorAggregator.Aggregate(result, subDistributorId: 7);
    }

    private static JsonElement Data(ImportErrorEntry e) => JsonSerializer.SerializeToElement(e.Data);
    private static int Int(ImportErrorEntry e, string name) => Data(e).GetProperty(name).GetInt32();

    // ---- classification and what is skipped ----

    [Fact]
    public void Uses_the_code_it_is_given_and_the_column_as_field()
    {
        var e = Assert.Single(Run(Issue(5, "INV1", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123")));
        Assert.Equal(ImportErrorTypes.SkuNotFound, e.ErrorType);
        Assert.Equal("SkuCode", e.FieldName);
    }

    [Fact]
    public void Hold_notes_are_not_logged()
    {
        var entries = Run(
            Issue(5, "INV1", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"),
            Issue(6, "INV1", code: null, message: "Invoice held: row 5 has an error", hold: true));
        Assert.Single(entries);
    }

    [Fact]
    public void An_issue_without_a_code_is_still_logged_as_unclassified()
    {
        var e = Assert.Single(Run(Issue(9, "INV2", code: null, message: "Something new went wrong")));
        Assert.Equal(ImportErrorTypes.UnclassifiedError, e.ErrorType);
        Assert.Equal("Something new went wrong", e.ErrorMessage);
    }

    [Fact]
    public void Unclassified_issues_with_different_messages_stay_separate()
    {
        var entries = Run(
            Issue(1, "A", code: null, message: "one"),
            Issue(2, "A", code: null, message: "two"));
        Assert.Equal(2, entries.Count);
    }

    // ---- grouping ----

    [Fact]
    public void Different_values_are_different_errors()
    {
        var entries = Run(
            Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"),
            Issue(2, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "XYZ789"));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Case_and_spacing_do_not_split_an_error_but_the_original_spellings_are_kept()
    {
        var e = Assert.Single(Run(
            Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "abc 123"),
            Issue(2, "B", ImportErrorTypes.SkuNotFound, "SkuCode", "  ABC   123 ")));
        Assert.Equal(2, Int(e, "Occurrences"));
        Assert.Equal(2, Data(e).GetProperty("Variants").GetArrayLength());
    }

    [Fact]
    public void Context_keeps_the_same_value_apart()
    {
        var entries = Run(
            Issue(1, "A", ImportErrorTypes.UomNotFound, "UOM", "BOX", context: "SKU-A"),
            Issue(2, "A", ImportErrorTypes.UomNotFound, "UOM", "BOX", context: "SKU-B"));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Same_value_in_different_fields_is_different()
    {
        var entries = Run(
            Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC"),
            Issue(2, "A", ImportErrorTypes.SkuNotFound, "ItemName", "ABC"));
        Assert.Equal(2, entries.Count);
    }

    // ---- counting ----

    [Fact]
    public void The_same_row_reported_by_two_layers_counts_once()
    {
        var e = Assert.Single(Run(
            Issue(12, "INV1", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"),
            Issue(12, "INV1", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123")));
        Assert.Equal(1, Int(e, "Occurrences"));
        Assert.Equal(12, e.SourceRowNumber);
    }

    [Fact]
    public void The_same_row_number_on_different_sheets_is_two_occurrences()
    {
        var e = Assert.Single(Run(
            Issue(5, "INV1", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC", sheet: 1),
            Issue(5, "INV2", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC", sheet: 2)));
        Assert.Equal(2, Int(e, "Occurrences"));
    }

    [Fact]
    public void Occurrences_and_affected_invoices_are_counted_separately()
    {
        var e = Assert.Single(Run(
            Issue(1, "INV1", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"),
            Issue(2, "INV1", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"),
            Issue(3, "inv2", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123")));
        Assert.Equal(3, Int(e, "Occurrences"));
        Assert.Equal(2, Int(e, "AffectedInvoices"));
        Assert.Null(e.SourceRowNumber);                    // aggregated: rows live in the data
        Assert.Equal(7, Int(e, "SubDistributorId"));
    }

    [Fact]
    public void Rows_without_an_invoice_code_count_as_occurrences_but_not_invoices()
    {
        var e = Assert.Single(Run(
            Issue(1, "", ImportErrorTypes.InvoiceCodeMissing, "InvoiceCode"),
            Issue(2, "", ImportErrorTypes.InvoiceCodeMissing, "InvoiceCode")));
        Assert.Equal(2, Int(e, "Occurrences"));
        Assert.Equal(0, Int(e, "AffectedInvoices"));
    }

    [Fact]
    public void Sample_rows_are_limited_but_the_count_is_not()
    {
        var issues = Enumerable.Range(1, 50)
            .Select(r => Issue(r, $"INV{r}", ImportErrorTypes.QuantityMissing, "Quantity"));
        var e = Assert.Single(Run(issues));
        Assert.Equal(50, Int(e, "Occurrences"));
        Assert.Equal(ImportRowErrorAggregator.MaxSampleRows, Data(e).GetProperty("SampleRows").GetArrayLength());
        Assert.Equal("STORE", Data(e).GetProperty("SampleRows")[0].GetProperty("SheetName").GetString());
    }

    // ---- no limit on distinct errors ----

    [Fact]
    public void Every_distinct_error_is_kept_however_many_there_are()
    {
        var issues = Enumerable.Range(1, 250)
            .Select(r => Issue(r, $"INV{r}", ImportErrorTypes.SkuNotFound, "SkuCode", $"SKU{r}"));
        var entries = Run(issues);

        Assert.Equal(250, entries.Count);
        Assert.Equal(250, entries.Select(e => Data(e).GetProperty("Value").GetString()).Distinct().Count());
        Assert.All(entries, e => Assert.Equal(ImportErrorTypes.SkuNotFound, e.ErrorType));
    }

    [Fact]
    public void Errors_beyond_the_old_limit_keep_aggregating_their_repeats()
    {
        // 250 distinct SKUs, each on two rows: nothing is dropped and the repeat counts stay right on every group.
        var issues = Enumerable.Range(1, 250).SelectMany(r => new[]
        {
            Issue(r, $"INV{r}", ImportErrorTypes.SkuNotFound, "SkuCode", $"SKU{r}"),
            Issue(r + 1000, $"INV{r}", ImportErrorTypes.SkuNotFound, "SkuCode", $"SKU{r}"),
        });
        var entries = Run(issues);

        Assert.Equal(250, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal(2, Int(e, "Occurrences"));
            Assert.Equal(1, Int(e, "AffectedInvoices"));
        });
    }

    [Fact]
    public void Errors_are_returned_in_the_order_they_were_first_seen()
    {
        var entries = Run(
            Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "FIRST"),
            Issue(2, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "SECOND"),
            Issue(3, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "FIRST"));

        Assert.Equal(new[] { "FIRST", "SECOND" }, entries.Select(e => Data(e).GetProperty("Value").GetString()));
    }

    [Fact]
    public void Per_error_detail_stays_bounded_even_though_the_error_count_is_not()
    {
        var one = Assert.Single(Run(Enumerable.Range(1, 60)
            .Select(r => Issue(r, $"INV{r}", ImportErrorTypes.SkuNotFound, "SkuCode", "SAME"))));
        Assert.Equal(60, Int(one, "Occurrences"));
        Assert.Equal(ImportRowErrorAggregator.MaxSampleRows, Data(one).GetProperty("SampleRows").GetArrayLength());

        var variants = Assert.Single(Run(Enumerable.Range(1, 12)
            .Select(r => Issue(r, "A", ImportErrorTypes.SkuNotFound, "SkuCode", r % 2 == 0 ? "abc" : "ABC" + new string(' ', r)))));
        Assert.True(Data(variants).GetProperty("Variants").GetArrayLength() <= ImportRowErrorAggregator.MaxVariants);
    }

    // ---- commit-time errors ----

    [Fact]
    public void Explicit_issues_are_aggregated_instead_of_the_results_own_issue_list()
    {
        var result = new ImportSalesInvoiceResult();
        result.GetOrAddSheet(1, "STORE");
        result.AddIssue(Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "PREP"));   // already logged at Check File

        var commit = new[] { Issue(2, "B", ImportErrorTypes.InvoiceSaveFailed, value: "db down") };
        var e = Assert.Single(ImportRowErrorAggregator.Aggregate(result, commit, 7));

        Assert.Equal(ImportErrorTypes.InvoiceSaveFailed, e.ErrorType);
        Assert.Equal("STORE", Data(e).GetProperty("SampleRows")[0].GetProperty("SheetName").GetString());
    }

    [Fact]
    public void The_same_save_failure_on_many_invoices_is_one_error_that_counts_them()
    {
        var e = Assert.Single(Run(Enumerable.Range(1, 30)
            .Select(r => Issue(r, $"INV{r}", ImportErrorTypes.InvoiceSaveFailed, value: "Timeout expired"))));

        Assert.Equal(30, Int(e, "Occurrences"));
        Assert.Equal(30, Int(e, "AffectedInvoices"));
    }

    [Fact]
    public void An_expected_rejection_such_as_a_duplicate_invoice_is_not_logged()
    {
        var entries = Run(
            Issue(1, "INV1", ImportRowErrorAggregator.InvoiceDuplicateCode, value: "INV1"),
            Issue(2, "INV2", ImportRowErrorAggregator.InvoiceDuplicateCode, value: "INV2"),
            Issue(3, "INV3", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC"));

        Assert.Equal(ImportErrorTypes.SkuNotFound, Assert.Single(entries).ErrorType);
    }

    [Fact]
    public void A_file_whose_only_problem_is_duplicate_invoices_logs_nothing()
    {
        Assert.Empty(Run(Issue(1, "INV1", ImportRowErrorAggregator.InvoiceDuplicateCode, value: "INV1")));
    }

    // ---- misc ----

    [Fact]
    public void Suggestion_count_is_kept()
    {
        var e = Assert.Single(Run(Issue(1, "A", ImportErrorTypes.CustomerNotFound, "CustomerCode", "ACME", suggestions: 3)));
        Assert.Equal(3, Int(e, "SuggestionCount"));
    }

    [Fact]
    public void No_issues_means_no_entries()
    {
        Assert.Empty(Run());
    }
}