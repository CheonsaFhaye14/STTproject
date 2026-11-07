using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportErrors.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;
using STTproject.Services;

namespace STTproject.Features.Admin.ImportErrors.Services
{
    public interface IImportErrorDetailsService
    {
        /// <summary>Null when the error doesn't exist.</summary>
        Task<ImportErrorDetailsDto?> GetAsync(int importErrorId, CancellationToken ct = default);

        /// <summary>The stored upload for a batch, or null when it was never saved or was cleaned up.</summary>
        Task<ImportOriginalFile?> GetOriginalFileAsync(int importBatchId, CancellationToken ct = default);

        /// <summary>Every batch this error appeared in (ImportErrorBatch), newest first, with the context a template is chosen by.</summary>
        Task<IReadOnlyList<FixContextDto>> GetFixContextsAsync(int importErrorId, CancellationToken ct = default);

        /// <summary>
        /// Active templates that could have read the given batch's file: the subdistributor's own templates when it has any,
        /// otherwise the global default(s). Empty when none exists, or when the batch is not linked to this error.
        /// </summary>
        Task<IReadOnlyList<FixTemplateOption>> FindFixTemplatesAsync(int importErrorId, int importBatchId, CancellationToken ct = default);

        /// <summary>Pending → Ignored. False when the error isn't Pending.</summary>
        Task<bool> IgnoreAsync(int importErrorId, CancellationToken ct = default);

        /// <summary>Ignored → Pending. False when the error isn't Ignored.</summary>
        Task<bool> ReopenAsync(int importErrorId, CancellationToken ct = default);

        /// <summary>Pending → Resolved, recording the chosen type, the admin and the time. False when the error isn't Pending.</summary>
        Task<bool> ResolveManuallyAsync(int importErrorId, string resolutionType, CancellationToken ct = default);
    }

    public sealed class ImportErrorDetailsService : IImportErrorDetailsService
    {
        /// <summary>Offered in the Resolve manually window. None may contain "auto": ImportErrorPresentation treats that as an automatic resolution.</summary>
        public static readonly IReadOnlyList<string> ManualResolutionTypes = new[]
        {
            "Source file corrected",
            "Master data corrected outside Entriel",
            "Not an actual error",
            "Other"
        };

        private readonly IDbContextFactory<EntrielContext> _factory;
        private readonly IImportFileStore _files;
        private readonly IUserContextService _user;
        private readonly IImportTemplateService _templates;

        public ImportErrorDetailsService(IDbContextFactory<EntrielContext> factory, IImportFileStore files, IUserContextService user,
            IImportTemplateService templates)
        {
            _factory = factory;
            _files = files;
            _user = user;
            _templates = templates;
        }

        public async Task<IReadOnlyList<FixContextDto>> GetFixContextsAsync(int importErrorId, CancellationToken ct = default)
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);

            var rows = await ctx.ImportErrorBatches.AsNoTracking()
                .Where(l => l.ImportError.ImportErrorId == importErrorId)
                .Select(l => new
                {
                    l.ImportBatch.ImportBatchId,
                    l.ImportBatch.SubDistributorId,
                    SubdName = l.ImportBatch.SubDistributor != null ? l.ImportBatch.SubDistributor.SubdName : null,
                    l.ImportBatch.ImportType,
                    l.ImportBatch.UploadedAt
                })
                .ToListAsync(ct);

            return rows
                .OrderByDescending(r => r.UploadedAt).ThenByDescending(r => r.ImportBatchId)
                .Select(r => new FixContextDto(r.ImportBatchId, r.SubDistributorId, r.SubdName, r.ImportType, ToPh(r.UploadedAt)))
                .ToList();
        }

        public async Task<IReadOnlyList<FixTemplateOption>> FindFixTemplatesAsync(int importErrorId, int importBatchId, CancellationToken ct = default)
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);

            // The batch must be one this error really appeared in; the context comes from that batch only.
            var src = await ctx.ImportErrorBatches.AsNoTracking()
                .Where(l => l.ImportError.ImportErrorId == importErrorId && l.ImportBatchId == importBatchId)
                .Select(l => new { l.ImportBatch.ImportType, l.ImportBatch.SubDistributorId })
                .FirstOrDefaultAsync(ct);
            if (src is null) return Array.Empty<FixTemplateOption>();

            var candidates = await _templates.GetCandidatesForImportAsync(src.ImportType, src.SubDistributorId, null, ct);
            var own = candidates.Where(c => !c.IsGlobalDefault).ToList();
            var pool = own.Count > 0 ? own : candidates.ToList();
            return pool.Select(c => new FixTemplateOption(c.ImportTemplateId, c.TemplateName, c.IsGlobalDefault)).ToList();
        }

        public async Task<ImportErrorDetailsDto?> GetAsync(int importErrorId, CancellationToken ct = default)
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);

            var e = await ctx.ImportErrors.AsNoTracking()
                .Where(x => x.ImportErrorId == importErrorId)
                .Select(x => new
                {
                    x.ImportErrorId,
                    x.ErrorType,
                    x.ErrorMessage,
                    x.FieldName,
                    x.DataJson,
                    x.Status,
                    x.ResolutionType,
                    x.ResolvedAt,
                    x.ResolvedBy,
                    x.SourceRowNumber,
                    x.ImportBatchId,
                    x.PreviousImportErrorId,
                    x.Fingerprint,
                    x.VerifiedImportBatchId,
                    ImportType = x.ImportBatch.ImportType,
                    SubDistributorId = x.ImportBatch.SubDistributorId,
                    SubDistributorName = x.ImportBatch.SubDistributor != null ? x.ImportBatch.SubDistributor.SubdName : null
                })
                .FirstOrDefaultAsync(ct);

            if (e is null) return null;

            var links = await ctx.ImportErrorBatches.AsNoTracking()
                .Where(l => l.ImportError.ImportErrorId == importErrorId)
                .Select(l => new
                {
                    BatchId = l.ImportBatch.ImportBatchId,
                    l.ImportBatch.FileName,
                    l.ImportBatch.ImportFileId,
                    l.ImportBatch.BatchStatus,
                    UploadedBy = (int?)l.ImportBatch.UploadedBy,
                    l.ImportBatch.UploadedAt,
                    l.Occurrences,
                    FirstSeen = (DateTime?)l.FirstSeenAt,
                    LastSeen = (DateTime?)l.LastSeenAt,
                    // Linked evidence first; the legacy per-batch DataJson is the fallback (older rows, or evidence not linked yet).
                    DataJson = l.Evidence != null && l.Evidence.DataJson != null ? l.Evidence.DataJson : l.DataJson
                })
                .ToListAsync(ct);

            // The earlier error this one follows, read only. It is never modified.
            var prev = e.PreviousImportErrorId is int pid
                ? await ctx.ImportErrors.AsNoTracking()
                    .Where(x => x.ImportErrorId == pid)
                    .Select(x => new
                    {
                        x.ImportErrorId,
                        x.Status,
                        x.ResolutionType,
                        x.ResolvedAt,
                        x.ResolvedBy,
                        Occurrences = x.ImportErrorBatches.Sum(b => (int?)b.Occurrences) ?? 0
                    })
                    .FirstOrDefaultAsync(ct)
                : null;

            // Display names (same assumption as the export service: Users has UserId and Username).
            var userIds = links.Where(l => l.UploadedBy.HasValue).Select(l => l.UploadedBy!.Value)
                .Concat(e.ResolvedBy.HasValue ? new[] { e.ResolvedBy.Value } : Array.Empty<int>())
                .Concat(prev?.ResolvedBy is int prevBy ? new[] { prevBy } : Array.Empty<int>())
                .Distinct().ToList();
            var names = userIds.Count == 0
                ? new Dictionary<int, string>()
                : await ctx.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.Username, ct);

            var ordered = links.OrderByDescending(l => l.UploadedAt).ThenByDescending(l => l.BatchId).ToList();

            var rows = new List<ImportErrorAffectedRowDto>();
            var reportedByBatch = new Dictionary<int, string?>();
            var invoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var truncated = false;
            foreach (var l in ordered)
            {
                var ev = ParseEvidence(l.DataJson, l.BatchId, l.FileName, l.BatchId == e.ImportBatchId ? e.SourceRowNumber : null);
                truncated |= ev.Truncated;
                reportedByBatch[l.BatchId] = ReportedRowsText(ev.Rows);
                foreach (var i in ev.Invoices) invoices.Add(i);
                foreach (var r in ev.Rows) if (!string.IsNullOrWhiteSpace(r.Invoice)) invoices.Add(r.Invoice!);
                rows.AddRange(ev.Rows.OrderBy(r => r.Sheet, StringComparer.Ordinal).ThenBy(r => r.RowNumber));
            }

            var batchInfo = ordered.ToDictionary(l => l.BatchId, l => (l.FileName, Uploaded: ToPh(l.UploadedAt)));
            var logicalRows = BuildLogicalRows(rows, batchInfo);
            AttachRecheckedRows(e.DataJson, logicalRows);
            var newest = ordered.FirstOrDefault();
            var correction = await GetCorrectionAsync(ctx, e.Status, e.ResolutionType, e.VerifiedImportBatchId, e.ImportType, e.SubDistributorId,
                newest?.BatchId, newest?.UploadedAt, ct);

            var batchDtos = ordered.Select(l => new ImportErrorBatchDetailDto
            {
                ImportBatchId = l.BatchId,
                FileName = l.FileName,
                ImportFileId = l.ImportFileId,
                ReportedRowsText = reportedByBatch.GetValueOrDefault(l.BatchId),
                BatchStatus = l.BatchStatus,
                UploadedByName = l.UploadedBy.HasValue && names.TryGetValue(l.UploadedBy.Value, out var un) ? un : null,
                UploadedAtPht = ToPh(l.UploadedAt),
                Occurrences = l.Occurrences,
                FirstSeenAtPht = ToPh(l.FirstSeen),
                LastSeenAtPht = ToPh(l.LastSeen)
            }).ToList();

            return new ImportErrorDetailsDto
            {
                ImportErrorId = e.ImportErrorId,
                ErrorType = e.ErrorType,
                FieldName = e.FieldName,
                Message = e.ErrorMessage,
                Value = ImportErrorPresentation.ExtractProblemValue(e.DataJson, e.ErrorMessage),
                Status = e.Status,
                ResolutionType = e.ResolutionType,
                ResolvedByName = e.ResolvedBy.HasValue && names.TryGetValue(e.ResolvedBy.Value, out var rn) ? rn : null,
                ResolvedAtPht = ToPh(e.ResolvedAt),
                SubDistributorId = e.SubDistributorId,
                SubDistributorName = e.SubDistributorName,
                Occurrences = links.Sum(l => l.Occurrences),
                AffectedInvoices = invoices.Count,
                AffectedInvoiceCodes = invoices.OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToList(),
                VerifiedImportBatchId = e.VerifiedImportBatchId,
                RecheckedImportBatchIds = ImportErrorPresentation.StatusKey(e.Status) == ImportErrorPresentation.Resolved && ImportErrorPresentation.IsVerifiedByRecheck(e.ResolutionType)
                    ? ordered.Select(l => l.BatchId).ToList()
                    : new List<int>(),
                // Verifiable only with complete evidence: a truncated list can't prove that every affected invoice passed.
                CanVerify = string.Equals(e.ImportType, "SalesInvoice", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(e.Fingerprint) && e.SubDistributorId.HasValue
                            && invoices.Count > 0 && !truncated,
                FirstSeenAtPht = ToPh(links.Count == 0 ? null : links.Min(l => l.FirstSeen ?? l.UploadedAt)),
                LastSeenAtPht = ToPh(links.Count == 0 ? null : links.Max(l => l.LastSeen ?? l.UploadedAt)),
                Batches = batchDtos,
                UploadGroups = BuildUploadGroups(batchDtos),
                Rows = rows,
                LogicalRows = logicalRows,
                Facts = BuildFacts(e.ErrorType, ImportErrorPresentation.ExtractProblemValue(e.DataJson, e.ErrorMessage), e.ErrorMessage, e.DataJson, rows),
                CorrectionState = correction.State,
                CorrectionNote = correction.Note,
                CorrectionBatchId = correction.BatchId,
                RowsTruncated = truncated,
                Recurrence = prev is null ? null : new ImportErrorRecurrenceDto
                {
                    PreviousImportErrorId = prev.ImportErrorId,
                    PreviousStatus = prev.Status,
                    PreviousResolutionType = prev.ResolutionType,
                    PreviousResolvedByName = prev.ResolvedBy.HasValue && names.TryGetValue(prev.ResolvedBy.Value, out var pn) ? pn : null,
                    PreviousResolvedAtPht = ToPh(prev.ResolvedAt),
                    PreviousOccurrences = prev.Occurrences
                }
            };
        }

        public async Task<ImportOriginalFile?> GetOriginalFileAsync(int importBatchId, CancellationToken ct = default)
        {
            var bytes = await _files.GetOriginalFileAsync(importBatchId, ct);
            if (bytes is null) return null;

            await using var ctx = await _factory.CreateDbContextAsync(ct);
            var name = await ctx.ImportBatches.AsNoTracking()
                .Where(b => b.ImportBatchId == importBatchId).Select(b => b.FileName).FirstOrDefaultAsync(ct)
                ?? $"import-{importBatchId}.xlsx";

            var type = Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
                ".csv" => "text/csv",
                _ => "application/octet-stream"
            };
            return new ImportOriginalFile(name, type, bytes);
        }

        // ---------------- status changes ----------------
        // Only the columns that exist today are used: Status, ResolutionType, ResolvedBy, ResolvedAt.
        // ImportError has no note or reason column and no audit trail, so none is stored.

        public async Task<bool> IgnoreAsync(int importErrorId, CancellationToken ct = default)
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);
            var e = await ctx.ImportErrors.FirstOrDefaultAsync(x => x.ImportErrorId == importErrorId, ct);
            if (e is null || !ImportErrorPresentation.IsPending(e.Status)) return false;

            e.Status = ImportErrorStatus.Ignored;
            e.ResolutionType = null;
            e.ResolvedAt = null;
            e.ResolvedBy = null;
            await ctx.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ReopenAsync(int importErrorId, CancellationToken ct = default)
        {
            await using var ctx = await _factory.CreateDbContextAsync(ct);
            var e = await ctx.ImportErrors.FirstOrDefaultAsync(x => x.ImportErrorId == importErrorId, ct);
            if (e is null || ImportErrorPresentation.StatusKey(e.Status) != ImportErrorPresentation.Ignored) return false;

            e.Status = ImportErrorStatus.Pending;
            await ctx.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ResolveManuallyAsync(int importErrorId, string resolutionType, CancellationToken ct = default)
        {
            if (!ManualResolutionTypes.Contains(resolutionType)) return false;

            // An error Entriel can verify must go through Verify and Resolve; a button click alone is not proof.
            var detail = await GetAsync(importErrorId, ct);
            if (detail?.CanVerify == true) return false;

            await using var ctx = await _factory.CreateDbContextAsync(ct);
            var e = await ctx.ImportErrors.FirstOrDefaultAsync(x => x.ImportErrorId == importErrorId, ct);
            if (e is null || !ImportErrorPresentation.IsPending(e.Status)) return false;

            e.Status = ImportErrorStatus.Resolved;
            e.ResolutionType = resolutionType;
            e.ResolvedAt = DateTime.UtcNow;
            e.ResolvedBy = _user.UserId;
            await ctx.SaveChangesAsync(ct);
            return true;
        }

        // ---------------- evidence parsing ----------------
        // Same storage format the export service reads (ImportErrorBatch.DataJson: AffectedRows / SampleRows / AffectedInvoices / Truncated).

        internal sealed record Evidence(List<ImportErrorAffectedRowDto> Rows, bool Truncated, List<string> Invoices);

        internal static Evidence ParseEvidence(string? json, int batchId, string file, int? fallbackRow)
        {
            var rows = new List<ImportErrorAffectedRowDto>();
            var invoices = new List<string>();
            var truncated = false;

            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        truncated = Prop(root, "Truncated", out var flag) && flag.ValueKind == JsonValueKind.True;

                        if (Prop(root, "AffectedInvoices", out var inv) && inv.ValueKind == JsonValueKind.Array)
                            foreach (var i in inv.EnumerateArray())
                                if (i.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(i.GetString()))
                                    invoices.Add(i.GetString()!);

                        var seen = new HashSet<(string?, int)>();
                        foreach (var listName in new[] { "AffectedRows", "SampleRows" })
                        {
                            if (!Prop(root, listName, out var list) || list.ValueKind != JsonValueKind.Array) continue;

                            foreach (var item in list.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.Object) continue;

                                int? rowNumber = Prop(item, "RowNumber", out var rn) && rn.ValueKind == JsonValueKind.Number && rn.TryGetInt32(out var n) ? n : null;
                                var sheet = Text(item, "SheetName");
                                if (rowNumber.HasValue && !seen.Add((sheet, rowNumber.Value))) continue;

                                rows.Add(new ImportErrorAffectedRowDto
                                {
                                    ImportBatchId = batchId,
                                    FileName = file,
                                    Sheet = sheet,
                                    RowNumber = rowNumber,
                                    Invoice = Text(item, "InvoiceNumber") ?? Text(item, "InvoiceCode"),
                                    // Evidence written before IsReportedRow existed has no mark: its rows are the reported ones.
                                    IsReportedRow = !(Prop(item, "IsReportedRow", out var reportedFlag) && reportedFlag.ValueKind == JsonValueKind.False),
                                    ProblemField = Text(item, "ProblemField"),
                                    // A present empty string means "the cell was blank"; a missing or null property means nothing was stored.
                                    HasProblemValue = Prop(item, "ProblemValue", out var pv) && pv.ValueKind == JsonValueKind.String,
                                    ProblemValue = Prop(item, "ProblemValue", out var pv2) && pv2.ValueKind == JsonValueKind.String ? pv2.GetString() : null,
                                    Details = ReadDetails(item),
                                    Data = ReadRowData(item)
                                });
                            }

                            if (rows.Count > 0) break;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Unreadable evidence: use the fallback below.
                }
            }

            if (rows.Count == 0 && fallbackRow.HasValue)
                rows.Add(new ImportErrorAffectedRowDto { ImportBatchId = batchId, FileName = file, RowNumber = fallbackRow });

            return new Evidence(rows, truncated, invoices);
        }

        // "Sales · 165, Sales · 170 and 3 more": the rows the importer reported in one attempt (context rows are not listed).
        private static string? ReportedRowsText(List<ImportErrorAffectedRowDto> rows)
        {
            var labels = rows
                .Where(r => r.IsReportedRow && r.RowNumber.HasValue)
                .OrderBy(r => r.Sheet, StringComparer.Ordinal).ThenBy(r => r.RowNumber)
                .Select(r => string.IsNullOrEmpty(r.Sheet) ? r.RowNumber!.Value.ToString() : r.Sheet + " · " + r.RowNumber!.Value)
                .ToList();

            if (labels.Count == 0) return null;
            return labels.Count <= 5
                ? string.Join(", ", labels)
                : string.Join(", ", labels.Take(5)) + " and " + (labels.Count - 5) + " more";
        }

        // Batches arrive newest first. Attempts that uploaded the identical file (same ImportFileId) share a group; a batch with
        // no shared file is a group of its own. Nothing is merged or hidden: every attempt stays in its group's Batches.
        private static List<ImportErrorUploadGroupDto> BuildUploadGroups(List<ImportErrorBatchDetailDto> batches)
        {
            var groups = new List<ImportErrorUploadGroupDto>();
            var byFile = new Dictionary<int, ImportErrorUploadGroupDto>();

            foreach (var b in batches)
            {
                if (b.ImportFileId is int fileId && byFile.TryGetValue(fileId, out var existing))
                {
                    existing.Batches.Add(b);
                    continue;
                }

                var group = new ImportErrorUploadGroupDto { ImportFileId = b.ImportFileId, FileName = b.FileName };
                group.Batches.Add(b);
                groups.Add(group);
                if (b.ImportFileId is int id) byFile[id] = group;
            }

            return groups;
        }

        // ---------------- logical rows ----------------

        // One entry per (sheet, row number) however many uploads listed it. Rows arrive newest upload first, so Snapshots are newest first.
        // A row with no row number cannot be matched to another, so it stays on its own.
        private static List<ImportErrorLogicalRowDto> BuildLogicalRows(
            List<ImportErrorAffectedRowDto> rows, Dictionary<int, (string File, DateTime Uploaded)> batchInfo)
        {
            var byKey = new Dictionary<(string, int), ImportErrorLogicalRowDto>();
            var result = new List<ImportErrorLogicalRowDto>();

            foreach (var r in rows)
            {
                ImportErrorLogicalRowDto? logical = null;
                if (r.RowNumber.HasValue && byKey.TryGetValue((r.Sheet ?? string.Empty, r.RowNumber.Value), out var found))
                    logical = found;

                if (logical is null)
                {
                    logical = new ImportErrorLogicalRowDto { Sheet = r.Sheet, RowNumber = r.RowNumber };
                    result.Add(logical);
                    if (r.RowNumber.HasValue) byKey[(r.Sheet ?? string.Empty, r.RowNumber.Value)] = logical;
                }

                batchInfo.TryGetValue(r.ImportBatchId, out var info);
                logical.Snapshots.Add(new ImportErrorRowSnapshotDto
                {
                    ImportBatchId = r.ImportBatchId,
                    FileName = r.FileName,
                    UploadedAtPht = info.Uploaded,
                    IsReportedRow = r.IsReportedRow,
                    ProblemValue = r.ProblemValue,
                    HasProblemValue = r.HasProblemValue,
                    Data = r.Data
                });

                logical.Invoice ??= r.Invoice;
                logical.IsReportedRow |= r.IsReportedRow;
                logical.ProblemField ??= r.ProblemField;
                if (!logical.HasProblemValue && r.HasProblemValue)
                {
                    logical.HasProblemValue = true;
                    logical.ProblemValue = r.ProblemValue;
                }
                logical.Data ??= r.Data;
                logical.Details ??= r.Details;
            }

            foreach (var l in result)
            {
                var first = l.Snapshots.FirstOrDefault(s => s.Data != null)?.Data;
                l.SnapshotsDiffer = first != null && l.Snapshots.Any(s => s.Data != null && !s.Data.SequenceEqual(first));
            }

            return result
                .OrderBy(l => l.Sheet, StringComparer.Ordinal)
                .ThenBy(l => l.RowNumber ?? int.MaxValue)
                .ToList();
        }

        // What the newest recheck read for each row's field: ImportError.DataJson > RecheckedRows. No such property (never rechecked, or older
        // data) or unreadable JSON: the rows simply have no checked value.
        private static void AttachRecheckedRows(string? dataJson, List<ImportErrorLogicalRowDto> logicalRows)
        {
            if (string.IsNullOrWhiteSpace(dataJson)) return;

            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !Prop(doc.RootElement, "RecheckedRows", out var rc) || rc.ValueKind != JsonValueKind.Object
                    || !Prop(rc, "Rows", out var list) || list.ValueKind != JsonValueKind.Array) return;

                DateTime? checkedAt = Prop(rc, "CheckedAt", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var dt)
                    ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null;

                // The same Excel row can come from several files; the newest file (highest batch id) wins. The list is stored in that order.
                var latest = new Dictionary<(string, int), string>();
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || !Prop(item, "Row", out var rn) || !rn.TryGetInt32(out var row)) continue;
                    latest[(Text(item, "Sheet") ?? string.Empty, row)] =
                        Prop(item, "Value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                }

                foreach (var l in logicalRows)
                {
                    if (!l.RowNumber.HasValue || !latest.TryGetValue((l.Sheet ?? string.Empty, l.RowNumber.Value), out var value)) continue;
                    l.HasCheckedValue = true;
                    l.CheckedValue = value;
                    l.CheckedAtPht = ToPh(checkedAt);
                }
            }
            catch (JsonException)
            {
                // unreadable: no checked values
            }
        }

        // ---------------- overview facts ----------------

        private static readonly Regex ItemNameInMessage = new(@"\(([^()]+)\)\.?\s*$", RegexOptions.Compiled);

        // Customer and unit-of-measure errors store more than their one Value. The newest reported row's structured details are used;
        // older evidence falls back to what the error itself stored (Value, Context, and the item name inside the message).
        private static List<ImportErrorFactDto> BuildFacts(string errorType, string value, string message, string? dataJson, List<ImportErrorAffectedRowDto> rows)
        {
            string? Detail(string key) => rows
                .Where(r => r.IsReportedRow && r.Details != null)
                .Select(r => r.Details!.TryGetValue(key, out var v) ? v : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            static string Show(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v.Trim();

            if (string.Equals(errorType, ImportErrorTypes.CustomerNotFound, StringComparison.OrdinalIgnoreCase))
            {
                var code = Detail("CustomerCode");
                var name = Detail("CustomerName");
                if (code is null && name is null)
                {
                    // Stored before details existed: Value is "code - name", or just one of them.
                    var parts = value.Split(" - ", 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2) { code = parts[0]; name = parts[1]; }
                    else return new List<ImportErrorFactDto> { new("Customer", Show(value)) };
                }
                return new List<ImportErrorFactDto> { new("Customer code", Show(code)), new("Customer name", Show(name)) };
            }

            if (string.Equals(errorType, ImportErrorTypes.UomNotFound, StringComparison.OrdinalIgnoreCase))
            {
                var itemCode = Detail("ItemCode") ?? ContextOf(dataJson);
                var itemName = Detail("ItemName");
                if (itemName is null)
                {
                    var m = ItemNameInMessage.Match(message ?? string.Empty);
                    if (m.Success) itemName = m.Groups[1].Value;
                }
                var uom = Detail("UploadedUom") ?? value;
                return new List<ImportErrorFactDto> { new("Item code", Show(itemCode)), new("Item name", Show(itemName)), new("Uploaded UOM", Show(uom)) };
            }

            return new List<ImportErrorFactDto>();
        }

        private static string? ContextOf(string? dataJson)
        {
            if (string.IsNullOrWhiteSpace(dataJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                return doc.RootElement.ValueKind == JsonValueKind.Object && Prop(doc.RootElement, "Context", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : null;
            }
            catch (JsonException) { return null; }
        }

        // ---------------- correction state ----------------

        // Judged only from what is already stored. "Corrected" needs a validation that actually ran: a corrected file that passed
        // verification (VerifiedImportBatchId) or a recheck with current settings. An error simply missing from a later upload's list is
        // NOT proof: that upload's rows are not kept, so it may never have contained the affected invoice.
        private static async Task<(string State, string Note, int? BatchId)> GetCorrectionAsync(
            EntrielContext ctx, string status, string? resolutionType, int? verifiedBatchId, string importType, int? subDistributorId,
            int? lastBatchId, DateTime? lastUploadedAt, CancellationToken ct)
        {
            if (ImportErrorPresentation.StatusKey(status) == ImportErrorPresentation.Resolved)
            {
                if (ImportErrorPresentation.IsVerifiedByReimport(resolutionType) && verifiedBatchId.HasValue)
                    return (ImportErrorCorrectionState.Corrected, $"A corrected file (upload #{verifiedBatchId}) passed verification.", verifiedBatchId);
                if (ImportErrorPresentation.IsVerifiedByRecheck(resolutionType))
                    return (ImportErrorCorrectionState.Corrected, "The original file was rechecked with the current settings and no longer reports this error.", null);
            }

            if (lastBatchId is null || lastUploadedAt is null)
                return (ImportErrorCorrectionState.PreviouslyReported, "No upload is linked to this error.", null);

            var uploaded = lastUploadedAt.Value;
            var id = lastBatchId.Value;

            // A later upload counts as checked when its file was read: it did not fail and it read at least one row.
            var laterChecked = await ctx.ImportBatches.AsNoTracking().CountAsync(b =>
                b.ImportType == importType && b.SubDistributorId == subDistributorId
                && b.BatchStatus != "Failed" && (b.TotalRows ?? 0) > 0
                && (b.UploadedAt > uploaded || (b.UploadedAt == uploaded && b.ImportBatchId > id)), ct);

            if (laterChecked == 0)
                return (ImportErrorCorrectionState.StillReported, "Reported in the most recent checked upload for this subdistributor.", null);

            var manual = ImportErrorPresentation.StatusKey(status) == ImportErrorPresentation.Resolved ? " It was marked resolved without a validation." : string.Empty;
            return (ImportErrorCorrectionState.PreviouslyReported,
                $"{laterChecked} later upload{(laterChecked == 1 ? string.Empty : "s")} of this import type {(laterChecked == 1 ? "was" : "were")} checked, but none verified this error as fixed.{manual}", null);
        }

        private static Dictionary<string, string?>? ReadDetails(JsonElement item)
        {
            if (!Prop(item, "Details", out var d) || d.ValueKind != JsonValueKind.Object) return null;
            return d.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }

        private static List<KeyValuePair<string, string>>? ReadRowData(JsonElement item)
        {
            if (!Prop(item, "RowData", out var data) || data.ValueKind != JsonValueKind.Object) return null;
            return data.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                _ => p.Value.ToString()
            })).ToList();
        }

        private static bool Prop(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
            value = default;
            return false;
        }

        private static string? Text(JsonElement obj, string name)
        {
            if (!Prop(obj, name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array) return null;
            var s = v.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static DateTime ToPh(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(8);
        private static DateTime? ToPh(DateTime? utc) => utc.HasValue ? ToPh(utc.Value) : null;
    }
}