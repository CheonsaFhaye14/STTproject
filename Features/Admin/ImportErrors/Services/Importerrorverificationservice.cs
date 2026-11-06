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
        /// Later uploads the admin can verify against: same subdistributor and import type, uploaded after the problem was
        /// first detected, and not themselves linked to this error. Newest first.
        /// </summary>
        Task<IReadOnlyList<VerificationCandidateDto>> GetCandidatesAsync(int importErrorId, CancellationToken ct = default);

        /// <summary>
        /// Re-reads the stored file of <paramref name="verificationBatchId"/>, validates it with the importer's own rules (nothing is
        /// logged, no batch is created) and resolves the error only when the same problem is gone AND every previously affected
        /// invoice is in the file. Every other outcome leaves the error Pending.
        /// </summary>
        Task<VerificationResultDto> VerifyAndResolveAsync(int importErrorId, int verificationBatchId, CancellationToken ct = default);
    }

    public sealed class ImportErrorVerificationService : IImportErrorVerificationService
    {
        /// <summary>Stored in ImportError.ResolutionType. Must not contain "auto": the pages treat that as an automatic resolution.</summary>
        public const string VerifiedResolutionType = "Verified by re-import";

        private const int MaxCandidates = 50;

        private readonly IDbContextFactory<EntrielContext> _factory;
        private readonly IImportFileStore _files;
        private readonly ImportSalesInvoiceService _importer;
        private readonly IImportErrorDetailsService _details;
        private readonly IUserContextService _user;

        public ImportErrorVerificationService(
            IDbContextFactory<EntrielContext> factory,
            IImportFileStore files,
            ImportSalesInvoiceService importer,
            IImportErrorDetailsService details,
            IUserContextService user)
        {
            _factory = factory;
            _files = files;
            _importer = importer;
            _details = details;
            _user = user;
        }

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

        public async Task<VerificationResultDto> VerifyAndResolveAsync(int importErrorId, int verificationBatchId, CancellationToken ct = default)
        {
            var detail = await _details.GetAsync(importErrorId, ct);
            if (detail is null) return Result(VerificationOutcome.Unverifiable, "This error no longer exists.");
            if (!ImportErrorPresentation.IsPending(detail.Status))
                return Result(VerificationOutcome.NotPending, "This error is no longer Pending, so nothing was changed.");
            if (!detail.CanVerify)
                return Result(VerificationOutcome.Unverifiable,
                    "Entriel can't verify this error automatically: it needs a sales invoice error with a complete list of affected invoices.");

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

            var bytes = await _files.GetOriginalFileAsync(verificationBatchId, ct);
            if (bytes is null)
                return Result(VerificationOutcome.Unverifiable, "The stored file for batch #" + verificationBatchId + " was not found. It may never have been saved, or it was cleaned up.");

            // Validate-only run: no file name means no batch, no stored file and nothing written to the import log.
            // It uses the current template and master data, which is exactly what a fix has to satisfy.
            using var stream = new MemoryStream(bytes);
            var run = await _importer.PrepareFromExcelAsync(stream, subdId, userId, null, int.MaxValue, ct);

            if (run.TotalRowsProcessed == 0)
                return Result(VerificationOutcome.Unverifiable,
                    "Entriel could not read any rows from that file with the current template, so nothing could be checked.");

            // 1. Is the same problem still reported? Same fingerprint rule the import log uses to decide two errors are the same.
            var issues = new List<VerificationIssueDto>();
            foreach (var entry in ImportRowErrorAggregator.Aggregate(run, subdId))
            {
                var identity = entry.Identity ?? new ErrorIdentity(entry.ErrorMessage);
                var fingerprint = ImportErrorFingerprint.Compute(err.ImportType, subdId, entry.ErrorType, entry.FieldName, identity);
                if (!string.Equals(fingerprint, err.Fingerprint, StringComparison.Ordinal)) continue;

                var evidence = ImportErrorDetailsService.ParseEvidence(
                    JsonSerializer.Serialize(entry.BatchData ?? entry.Data), verificationBatchId, string.Empty, null);
                var reason = "Still invalid: " + detail.Value;

                if (evidence.Rows.Count == 0)
                    issues.Add(new VerificationIssueDto(null, null, null, reason + " (" + entry.Occurrences + " occurrences)"));
                foreach (var r in evidence.Rows)
                    issues.Add(new VerificationIssueDto(r.Invoice, r.Sheet, r.RowNumber, reason));
            }
            var stillInvalid = issues.Count;

            // 2. Is every previously affected invoice actually in this file? Absence is not proof of a fix.
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in run.RawValuesByRow.Values)
                if (row.TryGetValue("InvoiceCode", out var code) && !string.IsNullOrWhiteSpace(code))
                    present.Add(code.Trim());

            var missing = detail.AffectedInvoiceCodes.Where(i => !present.Contains(i.Trim())).ToList();
            foreach (var m in missing)
                issues.Add(new VerificationIssueDto(m, null, null, "Not found in this file, so it could not be checked."));

            if (stillInvalid > 0)
                return Result(VerificationOutcome.StillInvalid,
                    "The same problem is still in batch #" + verificationBatchId + ". The error stays Pending.", detail.AffectedInvoiceCodes.Count, issues);
            if (missing.Count > 0)
                return Result(VerificationOutcome.RecordsNotFound,
                    missing.Count + " of " + detail.AffectedInvoiceCodes.Count + " affected invoices are not in batch #" + verificationBatchId +
                    ". Entriel only resolves an error when every affected invoice has been checked. The error stays Pending.",
                    detail.AffectedInvoiceCodes.Count, issues);

            // 3. Everything passed. Re-read the error so a change made meanwhile is never overwritten.
            await using var write = await _factory.CreateDbContextAsync(ct);
            var e = await write.ImportErrors.FirstOrDefaultAsync(x => x.ImportErrorId == importErrorId, ct);
            if (e is null || !ImportErrorPresentation.IsPending(e.Status))
                return Result(VerificationOutcome.NotPending, "This error was changed by someone else, so nothing was changed.");

            e.Status = ImportErrorStatus.Resolved;
            e.ResolutionType = VerifiedResolutionType;
            e.ResolvedAt = DateTime.UtcNow;
            e.ResolvedBy = userId;
            e.VerifiedImportBatchId = verificationBatchId;
            await write.SaveChangesAsync(ct);

            return Result(VerificationOutcome.Verified,
                "All " + detail.AffectedInvoiceCodes.Count + " affected invoice(s) passed in batch #" + verificationBatchId + ".",
                detail.AffectedInvoiceCodes.Count);
        }

        private static VerificationResultDto Result(VerificationOutcome outcome, string message, int checkedInvoices = 0, List<VerificationIssueDto>? issues = null) =>
            new() { Outcome = outcome, Message = message, InvoicesChecked = checkedInvoices, Issues = issues ?? new() };
    }
}