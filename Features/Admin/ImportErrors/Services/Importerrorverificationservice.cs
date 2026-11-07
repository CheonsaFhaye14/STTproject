using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportErrors.DTOs;
using STTproject.Features.User.SalesInvoice.Services;
using STTproject.Services;

namespace STTproject.Features.Admin.ImportErrors.Services
{
    public interface IImportErrorVerificationService
    {
        /// <summary>
        /// Option A (the Excel file was corrected). Later uploads the admin can verify against: same subdistributor and import type,
        /// uploaded after the problem was first detected, and not themselves linked to this error. Newest first.
        /// </summary>
        Task<IReadOnlyList<VerificationCandidateDto>> GetCandidatesAsync(int importErrorId, CancellationToken ct = default);

        /// <summary>
        /// Option A (the Excel file was corrected). Re-reads the stored file of <paramref name="verificationBatchId"/>, validates it with the
        /// importer's own rules (nothing is logged, no batch is created) and resolves the error only when the same problem is gone AND every
        /// previously affected invoice is in the file. Every other outcome leaves the error Pending.
        /// Resolves with ResolutionType "Verified by re-import" and records the batch in VerifiedImportBatchId.
        /// </summary>
        Task<VerificationResultDto> VerifyAndResolveAsync(int importErrorId, int verificationBatchId, CancellationToken ct = default);

        /// <summary>
        /// Option B (something was corrected inside Entriel: the import template or master data). Re-reads the stored ORIGINAL file(s) this
        /// error was seen in and validates them with the current template and master data, validate-only: no batch, no saved invoices,
        /// nothing written to the import log (the only thing saved is what the current template read for each affected row, as RecheckedRows in ImportError.DataJson). Resolves the error only when, in every linked file, the same problem is gone AND every
        /// affected invoice recorded for that file is still read from it. A missing stored file, or any other outcome, leaves the error Pending.
        /// Resolves with ResolutionType "Verified by recheck" and leaves VerifiedImportBatchId empty (no corrected file was involved).
        /// The files that were rechecked are the error's own ImportErrorBatch links, which never change once the error is Resolved;
        /// ImportErrorDetailsDto.RecheckedImportBatchIds exposes them, so a recheck is never confused with a corrected re-import batch.
        /// Only the selected error is resolved: other errors the revalidation finds are neither logged nor resolved.
        /// Nothing is imported: the invoices still have to be uploaded.
        /// </summary>
        Task<VerificationResultDto> RecheckWithCurrentSettingsAsync(int importErrorId, CancellationToken ct = default);
    }

    public sealed class ImportErrorVerificationService : IImportErrorVerificationService
    {
        /// <summary>Option A. Stored in ImportError.ResolutionType. Must not contain "auto": the pages treat that as an automatic resolution.</summary>
        public const string VerifiedResolutionType = ImportErrorPresentation.VerifiedByReimport;

        /// <summary>Option B. Stored in ImportError.ResolutionType. Must not contain "auto".</summary>
        public const string RecheckResolutionType = ImportErrorPresentation.VerifiedByRecheck;

        private const int MaxCandidates = 50;

        /// <summary>Every linked file is validated in full, so an error seen in more files than this is not rechecked automatically. Public so the page can explain the limit instead of offering a recheck that cannot run.</summary>
        public const int MaxRecheckBatches = 10;

        private readonly IDbContextFactory<EntrielContext> _factory;
        private readonly IImportFileStore _files;
        private readonly IInvoiceFileValidator _validator;
        private readonly IImportErrorDetailsService _details;
        private readonly IUserContextService _user;

        public ImportErrorVerificationService(
            IDbContextFactory<EntrielContext> factory,
            IImportFileStore files,
            IInvoiceFileValidator validator,
            IImportErrorDetailsService details,
            IUserContextService user)
        {
            _factory = factory;
            _files = files;
            _validator = validator;
            _details = details;
            _user = user;
        }

        private sealed record ErrorSnapshot(string Fingerprint, string ImportType, int SubDistributorId);

        public async Task<IReadOnlyList<VerificationCandidateDto>> GetCandidatesAsync(int importErrorId, CancellationToken ct = default)
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);

            var err = await ctx.ImportErrors.AsNoTracking()
                .Where(x => x.ImportErrorId == importErrorId)
                .Select(x => new { x.CreatedAt, x.ImportBatch.ImportType, x.ImportBatch.SubDistributorId })
                .FirstOrDefaultAsync(ct);
            if (err is null || err.SubDistributorId is null) return Array.Empty<VerificationCandidateDto>();

            var rows = await ctx.ImportBatches.AsNoTracking()
                .Where(b => b.ImportType == err.ImportType
                            && b.SubDistributorId == err.SubDistributorId
                            && b.UploadedAt > err.CreatedAt
                            && !b.ImportErrorBatches.Any(l => l.ImportErrorId == importErrorId))
                .OrderByDescending(b => b.UploadedAt).ThenByDescending(b => b.ImportBatchId)
                .Take(MaxCandidates)
                .Select(b => new { b.ImportBatchId, b.FileName, b.BatchStatus, UploadedBy = b.UploadedByNavigation.Username, b.UploadedAt })
                .ToListAsync(ct);

            return rows.Select(r => new VerificationCandidateDto(
                r.ImportBatchId, r.FileName, r.BatchStatus,
                string.IsNullOrWhiteSpace(r.UploadedBy) ? null : r.UploadedBy,
                DateTime.SpecifyKind(r.UploadedAt, DateTimeKind.Utc).AddHours(8))).ToList();
        }

        // ---------------------------------------------------------------- Option A: corrected Excel file

        public async Task<VerificationResultDto> VerifyAndResolveAsync(int importErrorId, int verificationBatchId, CancellationToken ct = default)
        {
            var (loaded, rejection) = await LoadVerifiableAsync(importErrorId, ct);
            if (rejection is not null) return rejection;
            var detail = loaded!;

            await using var ctx = await _factory.CreateDbContextAsync(ct);

            var err = await ctx.ImportErrors.AsNoTracking()
                .Where(x => x.ImportErrorId == importErrorId)
                .Select(x => new { x.Fingerprint, x.CreatedAt, x.ImportBatch.ImportType, x.ImportBatch.SubDistributorId })
                .FirstOrDefaultAsync(ct);
            var batch = await ctx.ImportBatches.AsNoTracking()
                .Where(b => b.ImportBatchId == verificationBatchId)
                .Select(b => new { b.ImportType, b.SubDistributorId, b.UploadedAt })
                .FirstOrDefaultAsync(ct);

            if (err is null || err.Fingerprint is null || err.SubDistributorId is not int subdId)
                return Result(VerificationOutcome.Unverifiable, "This error has no fingerprint or subdistributor, so it can't be verified.");
            if (batch is null)
                return Result(VerificationOutcome.Unverifiable, "That import batch doesn't exist.");
            if (!string.Equals(batch.ImportType, err.ImportType, StringComparison.OrdinalIgnoreCase) || batch.SubDistributorId != err.SubDistributorId)
                return Result(VerificationOutcome.Unverifiable, "Batch #" + verificationBatchId + " belongs to a different subdistributor or import type.");
            if (batch.UploadedAt <= err.CreatedAt)
                return Result(VerificationOutcome.Unverifiable, "Batch #" + verificationBatchId + " was uploaded before this problem was first detected, so it can't prove a fix.");
            if (await ctx.ImportErrorBatches.AnyAsync(l => l.ImportErrorId == importErrorId && l.ImportBatchId == verificationBatchId, ct))
                return Result(VerificationOutcome.StillInvalid, "Batch #" + verificationBatchId + " still contains this error.");

            var userId = _user.UserId ?? 0;
            if (userId <= 0)
                return Result(VerificationOutcome.Unverifiable, "Unable to identify the current user. Please sign in again.");

            var snapshot = new ErrorSnapshot(err.Fingerprint, err.ImportType, subdId);
            var failure = await CheckFileAsync(detail, snapshot, userId, verificationBatchId, detail.AffectedInvoiceCodes, null, null, ct);
            if (failure is not null) return failure;

            // The affected-invoice list came from these links (same read as `detail`). If they changed while the file was being checked,
            // the list is out of date and the check proved nothing about the new evidence.
            var linkedAtCheck = detail.Batches.Select(b => b.ImportBatchId).ToList();
            var outcome = await ResolveAsync(importErrorId, VerifiedResolutionType, verificationBatchId, userId, linkedAtCheck, ct);
            if (outcome != ResolveOutcome.Resolved) return ResolveRefusal(outcome);

            return Result(VerificationOutcome.Verified,
                "All " + detail.AffectedInvoiceCodes.Count + " affected invoice(s) passed in batch #" + verificationBatchId + ".",
                detail.AffectedInvoiceCodes.Count);
        }

        // ---------------------------------------------------------------- Option B: corrected template or master data

        public async Task<VerificationResultDto> RecheckWithCurrentSettingsAsync(int importErrorId, CancellationToken ct = default)
        {
            var (loaded, rejection) = await LoadVerifiableAsync(importErrorId, ct);
            if (rejection is not null) return rejection;
            var detail = loaded!;

            await using var ctx = await _factory.CreateDbContextAsync(ct);

            var raw = await ctx.ImportErrors.AsNoTracking()
                .Where(x => x.ImportErrorId == importErrorId)
                .Select(x => new { x.Fingerprint, x.ImportBatch.ImportType, x.ImportBatch.SubDistributorId })
                .FirstOrDefaultAsync(ct);
            if (raw is null || raw.Fingerprint is null || raw.SubDistributorId is not int subdId)
                return Result(VerificationOutcome.Unverifiable, "This error has no fingerprint or subdistributor, so it can't be verified.");
            var snapshot = new ErrorSnapshot(raw.Fingerprint, raw.ImportType, subdId);

            var userId = _user.UserId ?? 0;
            if (userId <= 0)
                return Result(VerificationOutcome.Unverifiable, "Unable to identify the current user. Please sign in again.");

            // Batch selection: every file this error was actually seen in (ImportErrorBatch links). No "uploaded after" rule and no
            // "not linked" rule here: the original file is exactly what a template or master data fix has to be proven against.
            var links = await ctx.ImportErrorBatches.AsNoTracking()
                .Where(l => l.ImportErrorId == importErrorId)
                .Select(l => new
                {
                    l.ImportBatchId,
                    l.ImportBatch.ImportType,
                    l.ImportBatch.SubDistributorId,
                    l.ImportBatch.FileName,
                    l.ImportBatch.UploadedAt,
                    // Linked evidence first; the legacy per-batch DataJson is the fallback.
                    DataJson = l.Evidence != null && l.Evidence.DataJson != null ? l.Evidence.DataJson : l.DataJson
                })
                .ToListAsync(ct);

            if (links.Count == 0)
                return Result(VerificationOutcome.Unverifiable, "This error is not linked to any import file, so there is nothing to recheck.");
            if (links.Count > MaxRecheckBatches)
                return Result(VerificationOutcome.Unverifiable,
                    "This error appeared in " + links.Count + " files. Entriel rechecks at most " + MaxRecheckBatches + " files at once, so it can't be rechecked automatically. " +
                    "Upload a corrected file and verify against it instead. The error stays Pending.");

            var plans = new List<(int BatchId, List<string> Invoices, List<ImportErrorAffectedRowDto> Rows)>();
            var attributed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Newest first: the same order the details page uses, and the file most likely to still be stored.
            foreach (var l in links.OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.ImportBatchId))
            {
                if (!string.Equals(l.ImportType, snapshot.ImportType, StringComparison.OrdinalIgnoreCase) || l.SubDistributorId != snapshot.SubDistributorId)
                    return Result(VerificationOutcome.Unverifiable, "Batch #" + l.ImportBatchId + " belongs to a different subdistributor or import type.");

                // The invoices this error affected in THIS file, from the evidence stored with the link.
                var ev = ImportErrorDetailsService.ParseEvidence(l.DataJson, l.ImportBatchId, l.FileName, null);
                if (ev.Truncated)
                    return Result(VerificationOutcome.Unverifiable, "The affected invoices recorded for batch #" + l.ImportBatchId + " are incomplete, so it can't be rechecked.");

                var codes = ev.Invoices
                    .Concat(ev.Rows.Select(r => r.Invoice).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i!))
                    .Select(i => i.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (codes.Count == 0)
                    return Result(VerificationOutcome.Unverifiable, "Batch #" + l.ImportBatchId + " has no recorded affected invoices, so Entriel can't prove they were checked.");

                foreach (var c in codes) attributed.Add(c);
                plans.Add((l.ImportBatchId, codes, ev.Rows.Where(r => r.IsReportedRow && r.RowNumber.HasValue).ToList()));
            }

            // The links read for the plans and the links the evidence (detail) was built from must be the same set.
            // A new upload can be linked to this still-open error at any moment (cross-batch deduplication).
            if (!detail.Batches.Select(b => b.ImportBatchId).ToHashSet().SetEquals(plans.Select(p => p.BatchId)))
                return Result(VerificationOutcome.Unverifiable,
                    "This error was seen in another file while it was being checked, so the result would be out of date. Nothing was changed: try again.");

            // Every invoice the details page lists must belong to one of the files being rechecked.
            if (detail.AffectedInvoiceCodes.Any(c => !attributed.Contains(c.Trim())))
                return Result(VerificationOutcome.Unverifiable, "Entriel could not tell which file each affected invoice came from, so it can't be rechecked.");

            // Every file is read, so the page can show what the current template reads for each row. The first file that does not pass
            // still decides the outcome, and nothing is resolved unless every file passed.
            VerificationResultDto? firstFailure = null;
            var rechecked = new List<RecheckedRow>();
            foreach (var plan in plans)
            {
                var failure = await CheckFileAsync(detail, snapshot, userId, plan.BatchId, plan.Invoices, plan.Rows, rechecked, ct);
                firstFailure ??= failure;
            }

            // What the current template read for each row, kept on the error itself so the page can show which rows are fixed.
            await SaveRecheckedRowsAsync(importErrorId, rechecked, userId, ct);

            if (firstFailure is not null) return firstFailure;

            // Resolves only if the error still links to exactly the batches that were just checked (see ResolveAsync).
            var outcome = await ResolveAsync(importErrorId, RecheckResolutionType, null, userId, plans.Select(p => p.BatchId).ToList(), ct);
            if (outcome != ResolveOutcome.Resolved) return ResolveRefusal(outcome);

            return Result(VerificationOutcome.Verified,
                "All " + detail.AffectedInvoiceCodes.Count + " affected invoice(s) passed when the original file(s) (" +
                string.Join(", ", plans.Select(p => "batch #" + p.BatchId)) + ") were rechecked with the current template and master data. " +
                "Nothing was imported or saved: upload the file again to import these invoices.",
                detail.AffectedInvoiceCodes.Count);
        }

        // ---------------------------------------------------------------- shared

        /// <summary>The checks both paths start with. Returns the error's details, or the result to hand back.</summary>
        private async Task<(ImportErrorDetailsDto? Detail, VerificationResultDto? Rejection)> LoadVerifiableAsync(int importErrorId, CancellationToken ct)
        {
            var detail = await _details.GetAsync(importErrorId, ct);
            if (detail is null) return (null, Result(VerificationOutcome.Unverifiable, "This error no longer exists."));
            if (!ImportErrorPresentation.IsPending(detail.Status))
                return (null, Result(VerificationOutcome.NotPending, "This error is no longer Pending, so nothing was changed."));
            if (!detail.CanVerify)
                return (null, Result(VerificationOutcome.Unverifiable,
                    "Entriel can't verify this error automatically: it needs a sales invoice error with a complete list of affected invoices."));
            return (detail, null);
        }

        /// <summary>
        /// The validate-only check of one stored file, shared by both paths. Returns null when the file passes, otherwise the result to hand back.
        /// Passing means: the file was readable, the same problem (same fingerprint) is no longer reported, and every invoice in
        /// <paramref name="expectedInvoices"/> is still read from the file. Writes nothing: the validator calls PrepareFromExcelAsync without a file name,
        /// so no batch is created, no file is stored and nothing is logged (see IInvoiceFileValidator). It uses the current template and master data.
        /// </summary>
        private async Task<VerificationResultDto?> CheckFileAsync(
            ImportErrorDetailsDto detail, ErrorSnapshot err, int userId, int batchId,
            IReadOnlyCollection<string> expectedInvoices, IReadOnlyCollection<ImportErrorAffectedRowDto>? recordRows, List<RecheckedRow>? sink, CancellationToken ct)
        {
            var bytes = await _files.GetOriginalFileAsync(batchId, ct);
            if (bytes is null)
                return Result(VerificationOutcome.Unverifiable, "The stored file for batch #" + batchId + " was not found. It may never have been saved, or it was cleaned up.");

            var run = await _validator.ValidateAsync(bytes, err.SubDistributorId, userId, ct);

            // Best effort: remember what the current template read for this file's affected rows. Never decides the result.
            if (recordRows is { Count: > 0 } && sink is not null)
                CollectRecheckedRows(detail, batchId, recordRows, run, sink);

            if (run.RowsRead == 0)
                return Result(VerificationOutcome.Unverifiable,
                    "Entriel could not read any rows from that file with the current template, so nothing could be checked.");

            // 1. Is the same problem still reported? Same fingerprint rule the import log uses to decide two errors are the same.
            var issues = new List<VerificationIssueDto>();
            foreach (var entry in run.Errors)
            {
                var identity = entry.Identity ?? new ErrorIdentity(entry.ErrorMessage);
                var fingerprint = ImportErrorFingerprint.Compute(err.ImportType, err.SubDistributorId, entry.ErrorType, entry.FieldName, identity);
                if (!string.Equals(fingerprint, err.Fingerprint, StringComparison.Ordinal)) continue;

                var evidence = ImportErrorDetailsService.ParseEvidence(
                    JsonSerializer.Serialize(entry.BatchData ?? entry.Data), batchId, string.Empty, null);
                var reason = "Still invalid: " + detail.Value;

                if (evidence.Rows.Count == 0)
                    issues.Add(new VerificationIssueDto(null, null, null, reason + " (" + entry.Occurrences + " occurrences)"));
                foreach (var r in evidence.Rows)
                    issues.Add(new VerificationIssueDto(r.Invoice, r.Sheet, r.RowNumber, reason));
            }
            var stillInvalid = issues.Count;

            // 2. Is every previously affected invoice actually in this file? Absence is not proof of a fix.
            var present = run.InvoiceCodes;

            var missing = expectedInvoices.Where(i => !present.Contains(i.Trim())).ToList();
            foreach (var m in missing)
                issues.Add(new VerificationIssueDto(m, null, null, "Not found in this file, so it could not be checked."));

            if (stillInvalid > 0)
                return Result(VerificationOutcome.StillInvalid,
                    "The same problem is still in batch #" + batchId + ". The error stays Pending.", expectedInvoices.Count, issues);
            if (missing.Count > 0)
                return Result(VerificationOutcome.RecordsNotFound,
                    missing.Count + " of " + expectedInvoices.Count + " affected invoices are not in batch #" + batchId +
                    ". Entriel only resolves an error when every affected invoice has been checked. The error stays Pending.",
                    expectedInvoices.Count, issues);

            return null;
        }

        /// <summary>One affected row as the current template read it: the stored file, where the row is and the value of the error's field.</summary>
        private sealed record RecheckedRow(int Batch, string? Sheet, int Row, string Value);

        /// <summary>Collects (in memory only) what the current template read for the field of this error on each affected row of one stored file.</summary>
        private static void CollectRecheckedRows(
            ImportErrorDetailsDto detail, int batchId, IReadOnlyCollection<ImportErrorAffectedRowDto> rows, InvoiceFileValidation run, List<RecheckedRow> sink)
        {
            var field = detail.FieldName;
            if (string.IsNullOrWhiteSpace(field) || run.Rows is null || run.Rows.Count == 0) return;

            var read = new Dictionary<(string, int), string>();
            foreach (var r in run.Rows)
            {
                foreach (var (name, value) in r.Values)
                {
                    if (!string.Equals(name, field, StringComparison.OrdinalIgnoreCase)) continue;
                    var text = (value ?? string.Empty).Trim();
                    read[(r.Sheet ?? string.Empty, r.RowNumber)] = text.Length <= 300 ? text : text[..300];
                    break;
                }
            }

            foreach (var row in rows)
            {
                // A row the importer did not keep is left out: nothing is said about it.
                if (read.TryGetValue((row.Sheet ?? string.Empty, row.RowNumber!.Value), out var value))
                    sink.Add(new RecheckedRow(batchId, string.IsNullOrEmpty(row.Sheet) ? null : row.Sheet, row.RowNumber.Value, value));
            }
        }

        /// <summary>
        /// Writes the collected rows into ImportError.DataJson as one new property, RecheckedRows ({ CheckedAt, CheckedBy, Rows }). Every other
        /// property of that JSON is kept exactly as it was, and nothing else is touched (not the batch links, the evidence or the status).
        /// Each Verify replaces the previous RecheckedRows. Best effort: a failure is logged and ignored, the verification result never depends on it.
        /// </summary>
        private async Task SaveRecheckedRowsAsync(int importErrorId, List<RecheckedRow> rows, int userId, CancellationToken ct)
        {
            if (rows.Count == 0) return;

            try
            {
                await using var ctx = await _factory.CreateDbContextAsync(ct);
                var error = await ctx.ImportErrors.FirstOrDefaultAsync(e => e.ImportErrorId == importErrorId, ct);
                if (error is null) return;

                var root = new System.Text.Json.Nodes.JsonObject();
                if (!string.IsNullOrWhiteSpace(error.DataJson))
                {
                    try { root = System.Text.Json.Nodes.JsonNode.Parse(error.DataJson) as System.Text.Json.Nodes.JsonObject ?? root; }
                    catch (JsonException) { return; }   // unreadable existing JSON: leave it alone rather than overwrite it
                }

                var list = new System.Text.Json.Nodes.JsonArray();
                foreach (var r in rows.OrderBy(r => r.Batch).ThenBy(r => r.Sheet, StringComparer.Ordinal).ThenBy(r => r.Row))
                    list.Add(new System.Text.Json.Nodes.JsonObject
                    {
                        ["Batch"] = r.Batch,
                        ["Sheet"] = r.Sheet,
                        ["Row"] = r.Row,
                        ["Value"] = r.Value
                    });

                root["RecheckedRows"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["CheckedAt"] = DateTime.UtcNow,
                    ["CheckedBy"] = userId > 0 ? (int?)userId : null,
                    ["Rows"] = list
                };

                error.DataJson = root.ToJsonString();
                await ctx.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not save the rechecked rows for error " + importErrorId + ": " + ex.Message);
            }
        }

        private enum ResolveOutcome { Resolved, NotPending, LinksChanged }

        /// <summary>
        /// Pending to Resolved, in ONE conditional UPDATE so nothing can slip in between the check and the write. It only touches the
        /// selected error, and only when (a) it is still Pending (a change made meanwhile, such as Ignore, is never overwritten) and
        /// (b) its linked batches are exactly <paramref name="checkedBatchIds"/>: a batch linked after the files were read has not been
        /// checked, so the error stays Pending. <paramref name="verifiedBatchId"/> is only set by Option A.
        /// Needs EF Core 7+ (ExecuteUpdateAsync). Relies on one ImportErrorBatch row per (error, batch), which ImportLogService maintains.
        /// </summary>
        private async Task<ResolveOutcome> ResolveAsync(
            int importErrorId, string resolutionType, int? verifiedBatchId, int userId,
            IReadOnlyCollection<int> checkedBatchIds, CancellationToken ct)
        {
            await using var write = await _factory.CreateDbContextAsync(ct);

            var allowed = checkedBatchIds.Distinct().ToList();
            var expectedLinks = allowed.Count;
            var now = DateTime.UtcNow;
            int? resolvedBy = userId;

            var changed = await write.ImportErrors
                .Where(e => e.ImportErrorId == importErrorId
                            && e.Status.ToLower() == ImportErrorPresentation.Pending
                            && e.ImportErrorBatches.Count() == expectedLinks
                            && !e.ImportErrorBatches.Any(l => !allowed.Contains(l.ImportBatchId)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, ImportErrorStatus.Resolved)
                    .SetProperty(e => e.ResolutionType, resolutionType)
                    .SetProperty(e => e.ResolvedAt, (DateTime?)now)
                    .SetProperty(e => e.ResolvedBy, resolvedBy)
                    .SetProperty(e => e.VerifiedImportBatchId, verifiedBatchId), ct);

            if (changed == 1) return ResolveOutcome.Resolved;

            // Nothing was written. Say why, so the admin is told the truth.
            var status = await write.ImportErrors.AsNoTracking()
                .Where(e => e.ImportErrorId == importErrorId).Select(e => e.Status).FirstOrDefaultAsync(ct);
            return status is not null && ImportErrorPresentation.IsPending(status) ? ResolveOutcome.LinksChanged : ResolveOutcome.NotPending;
        }

        private static VerificationResultDto ResolveRefusal(ResolveOutcome outcome) => outcome switch
        {
            ResolveOutcome.LinksChanged => Result(VerificationOutcome.Unverifiable,
                "This error was seen in another file while it was being checked, so the result is out of date. The error stays Pending: try again."),
            _ => Result(VerificationOutcome.NotPending, "This error was changed by someone else, so nothing was changed.")
        };

        private static VerificationResultDto Result(VerificationOutcome outcome, string message, int checkedInvoices = 0, List<VerificationIssueDto>? issues = null) =>
            new() { Outcome = outcome, Message = message, InvoicesChecked = checkedInvoices, Issues = issues ?? new() };
    }
}