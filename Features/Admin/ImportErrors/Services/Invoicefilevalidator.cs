using STTproject.Features.User.SalesInvoice.Services;
using STTproject.Services;

namespace STTproject.Features.Admin.ImportErrors.Services
{
    /// <summary>
    /// What a validate-only read of one stored file found, in terms the verification service needs and nothing more.
    /// </summary>
    /// <param name="RowsRead">Rows the importer read from the file with the current template. 0 means nothing could be checked
    /// (unreadable file, no usable template, missing sheet or headers).</param>
    /// <param name="Errors">The row-level errors the importer would log for this file, already aggregated the same way the import log
    /// aggregates them, so their fingerprints can be compared with a stored error's fingerprint.</param>
    /// <param name="InvoiceCodes">Every invoice number the importer read from the file (trimmed, case-insensitive).</param>
    /// <param name="Rows">Every data row the importer read, with the value it read for each template field. Used to show what the current template
    /// reads for the rows of an error; empty when none were kept.</param>
    public sealed record InvoiceFileValidation(
        int RowsRead,
        IReadOnlyList<ImportErrorEntry> Errors,
        IReadOnlySet<string> InvoiceCodes,
        IReadOnlyList<ValidatedRow>? Rows = null);

    /// <summary>One row as the importer read it: its sheet name, Excel row number and the value of each template field (blank cells are empty strings).</summary>
    public sealed record ValidatedRow(string? Sheet, int RowNumber, IReadOnlyDictionary<string, string?> Values);

    /// <summary>
    /// Validates the bytes of a sales invoice workbook with the importer's own rules, using the CURRENT template and master data.
    /// Contract: validate-only. Implementations must not create an ImportBatch, store the file, save or commit invoices,
    /// or write anything to the import log. This is what makes "recheck" safe to run on an error that is still Pending.
    /// </summary>
    public interface IInvoiceFileValidator
    {
        Task<InvoiceFileValidation> ValidateAsync(byte[] fileBytes, int subDistributorId, int userId, CancellationToken ct = default);
    }

    /// <summary>
    /// Production validator. Calls ImportSalesInvoiceService.PrepareFromExcelAsync with no template override, no row cap and,
    /// crucially, NO file name. In that overload a null file name means "not a real import" (the Template Tester's preview):
    /// CreateBatchAsync, ImportFileStore.SaveAsync, LogErrorsAsync and CompleteBatchAsync are all skipped because the batch id stays null.
    /// CommitPreparedInvoicesAsync is never called, so no invoice is saved. The only database work left is reading master data,
    /// the template, prices and "does this invoice number already exist".
    /// </summary>
    public sealed class SalesInvoiceFileValidator : IInvoiceFileValidator
    {
        private readonly ImportSalesInvoiceService _importer;

        public SalesInvoiceFileValidator(ImportSalesInvoiceService importer) => _importer = importer;

        public async Task<InvoiceFileValidation> ValidateAsync(byte[] fileBytes, int subDistributorId, int userId, CancellationToken ct = default)
        {
            using var stream = new MemoryStream(fileBytes, writable: false);

            var run = await _importer.PrepareFromExcelAsync(
                stream,
                subDistributorId,
                userId,
                template: null,            // resolve the current template, exactly like a real import would
                maxRows: int.MaxValue,
                cancellationToken: ct,
                fileName: null);           // null = validate-only: no batch, no stored file, nothing logged

            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in run.RawValuesByRow.Values)
                if (row.TryGetValue("InvoiceCode", out var code) && !string.IsNullOrWhiteSpace(code))
                    codes.Add(code.Trim());

            // Same aggregation the import log uses, so fingerprints match. Materialized here: nothing is saved from it.
            var errors = ImportRowErrorAggregator.Aggregate(run, subDistributorId).ToList();

            var rows = new List<ValidatedRow>();
            foreach (var (key, values) in run.RawValuesByRow)
            {
                if (key.IsFileLevel || key.RowNumber <= 0) continue;
                rows.Add(new ValidatedRow(run.Sheets.TryGetValue(key.SheetIndex, out var sheet) ? sheet.Name : null, key.RowNumber, values));
            }

            return new InvoiceFileValidation(run.TotalRowsProcessed, errors, codes, rows);
        }
    }
}