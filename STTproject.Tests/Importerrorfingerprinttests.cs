using STTproject.Features.User.SalesInvoice.DTOs;
using STTproject.Features.User.SalesInvoice.Services;
using STTproject.Services;
using Xunit;

// Adjust this namespace to match your test project.
namespace STTproject.Tests.SalesInvoice;

public class ImportErrorFingerprintTests
{
    private const string Type = "SalesInvoice";

    private static string Fp(
        string importType = Type, int? subd = 7, string errorType = ImportErrorTypes.SkuNotFound,
        string? field = "SkuCode", string? value = "ABC123", string? context = null) =>
        ImportErrorFingerprint.Compute(importType, subd, errorType, field, new ErrorIdentity(value, context));

    // ---- shape and stability ----

    [Fact]
    public void Is_a_64_character_upper_case_hex_string()
    {
        var fp = Fp();
        Assert.Equal(64, fp.Length);
        Assert.Matches("^[0-9A-F]{64}$", fp);
    }

    [Fact]
    public void The_same_input_always_gives_the_same_fingerprint()
    {
        Assert.Equal(Fp(), Fp());
    }

    [Fact]
    public void Case_and_spacing_do_not_change_it()
    {
        Assert.Equal(Fp(value: "abc 123"), Fp(value: "  ABC   123 "));
        Assert.Equal(Fp(field: "skucode"), Fp(field: "SkuCode"));
        Assert.Equal(Fp(context: "sku-a"), Fp(context: " SKU-A "));
    }

    [Fact]
    public void Missing_and_blank_parts_are_the_same()
    {
        Assert.Equal(Fp(value: null), Fp(value: "   "));
        Assert.Equal(Fp(context: null), Fp(context: ""));
    }

    // ---- what must keep errors apart ----

    [Fact]
    public void Every_part_of_the_identity_matters()
    {
        var baseline = Fp();
        Assert.NotEqual(baseline, Fp(subd: 8));
        Assert.NotEqual(baseline, Fp(importType: "MapItem"));
        Assert.NotEqual(baseline, Fp(errorType: ImportErrorTypes.CustomerNotFound));
        Assert.NotEqual(baseline, Fp(field: "ItemName"));
        Assert.NotEqual(baseline, Fp(value: "XYZ789"));
        Assert.NotEqual(baseline, Fp(context: "SKU-A"));
    }

    [Fact]
    public void No_sub_distributor_is_not_the_same_as_sub_distributor_zero()
    {
        Assert.NotEqual(Fp(subd: null), Fp(subd: 0));
    }

    [Fact]
    public void A_value_cannot_shift_the_boundary_between_two_parts()
    {
        // Without sanitising the separator these two would hash the same text.
        var a = ImportErrorFingerprint.Compute(Type, 7, "T", "F", new ErrorIdentity("A\u001FB", "C"));
        var b = ImportErrorFingerprint.Compute(Type, 7, "T", "F", new ErrorIdentity("A", "B\u001FC"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Values_are_compared_up_to_the_normalization_cap()
    {
        var shared = new string('X', ImportErrorFingerprint.MaxPartLength);
        Assert.Equal(Fp(value: shared + "1"), Fp(value: shared + "2"));   // documented: only the first 200 characters count
    }

    // ---- the aggregator feeds the fingerprint ----

    private static ImportSalesInvoiceIssue Issue(int row, string invoice, string? code, string? field, string? value, string message = "msg") =>
        new(RowNumber: row, InvoiceNumber: invoice, Header: message, Body: string.Empty, Footer: string.Empty,
            ColumnName: field, SuggestionCount: null, IsHoldNote: false, SheetIndex: 1,
            Code: code, Value: value, Context: null);

    private static ImportErrorEntry Single(params ImportSalesInvoiceIssue[] issues)
    {
        var result = new ImportSalesInvoiceResult();
        result.GetOrAddSheet(1, "STORE");
        foreach (var i in issues) result.AddIssue(i);
        return Assert.Single(ImportRowErrorAggregator.Aggregate(result, subDistributorId: 7));
    }

    private static string FingerprintOf(ImportErrorEntry e) =>
        ImportErrorFingerprint.Compute(Type, 7, e.ErrorType, e.FieldName, e.Identity ?? new ErrorIdentity(e.ErrorMessage));

    [Fact]
    public void Entries_carry_their_identity_and_their_occurrences()
    {
        var e = Single(
            Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"),
            Issue(2, "B", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"));

        Assert.Equal("ABC123", e.Identity!.Value);
        Assert.Equal(2, e.Occurrences);
    }

    [Fact]
    public void The_same_problem_in_two_different_files_has_the_same_fingerprint()
    {
        // Different rows, invoices and message wording: only type, field and value identify the problem.
        var first = Single(Issue(5, "INV-1", ImportErrorTypes.SkuNotFound, "SkuCode", "abc123", "SKU not found on row 5"));
        var second = Single(
                   Issue(40, "INV-99", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123", "SKU not found on row 40"),
                   Issue(41, "INV-99", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123", "SKU not found on row 41"));

        Assert.Equal(FingerprintOf(first), FingerprintOf(second));
    }

    [Fact]
    public void A_different_sku_is_a_different_fingerprint()
    {
        var a = Single(Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "ABC123"));
        var b = Single(Issue(1, "A", ImportErrorTypes.SkuNotFound, "SkuCode", "XYZ789"));
        Assert.NotEqual(FingerprintOf(a), FingerprintOf(b));
    }

    [Fact]
    public void A_classified_error_without_a_value_is_not_identified_by_its_message()
    {
        // The message of a classified issue may name the row or invoice; it must not leak into the identity.
        var a = Single(Issue(1, "", ImportErrorTypes.InvoiceCodeMissing, "InvoiceCode", null, "Row 1: invoice code missing"));
        var b = Single(Issue(9, "", ImportErrorTypes.InvoiceCodeMissing, "InvoiceCode", null, "Row 9: invoice code missing"));
        Assert.Equal(FingerprintOf(a), FingerprintOf(b));
    }

    [Fact]
    public void An_unclassified_error_is_identified_by_its_message()
    {
        var a = Single(Issue(1, "A", null, null, null, "Something new went wrong"));
        var b = Single(Issue(2, "B", null, null, null, "Something else went wrong"));
        Assert.Equal(ImportErrorTypes.UnclassifiedError, a.ErrorType);
        Assert.NotEqual(FingerprintOf(a), FingerprintOf(b));
    }

    [Fact]
    public void A_file_level_entry_without_identity_falls_back_to_its_message()
    {
        var withMessage = new ImportErrorEntry(ImportErrorTypes.RequiredSheetMissing, "Sheet 'STORE' is missing");
        var other = new ImportErrorEntry(ImportErrorTypes.RequiredSheetMissing, "Sheet 'MAIN' is missing");
        Assert.Null(withMessage.Identity);
        Assert.NotEqual(FingerprintOf(withMessage), FingerprintOf(other));
    }
}