using System.Text.Json;
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
                    l.ImportBatch.BatchStatus,
                    UploadedBy = (int?)l.ImportBatch.UploadedBy,
                    l.ImportBatch.UploadedAt,
                    l.Occurrences,
                    FirstSeen = (DateTime?)l.FirstSeenAt,
                    LastSeen = (DateTime?)l.LastSeenAt,
                    l.DataJson
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
            var invoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var truncated = false;
            foreach (var l in ordered)
            {
                var ev = ParseEvidence(l.DataJson, l.BatchId, l.FileName, l.BatchId == e.ImportBatchId ? e.SourceRowNumber : null);
                truncated |= ev.Truncated;
                foreach (var i in ev.Invoices) invoices.Add(i);
                foreach (var r in ev.Rows) if (!string.IsNullOrWhiteSpace(r.Invoice)) invoices.Add(r.Invoice!);
                rows.AddRange(ev.Rows.OrderBy(r => r.Sheet, StringComparer.Ordinal).ThenBy(r => r.RowNumber));
            }

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
                // Verifiable only with complete evidence: a truncated list can't prove that every affected invoice passed.
                CanVerify = string.Equals(e.ImportType, "SalesInvoice", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(e.Fingerprint) && e.SubDistributorId.HasValue
                            && invoices.Count > 0 && !truncated,
                FirstSeenAtPht = ToPh(links.Count == 0 ? null : links.Min(l => l.FirstSeen ?? l.UploadedAt)),
                LastSeenAtPht = ToPh(links.Count == 0 ? null : links.Max(l => l.LastSeen ?? l.UploadedAt)),
                Batches = ordered.Select(l => new ImportErrorBatchDetailDto
                {
                    ImportBatchId = l.BatchId,
                    FileName = l.FileName,
                    BatchStatus = l.BatchStatus,
                    UploadedByName = l.UploadedBy.HasValue && names.TryGetValue(l.UploadedBy.Value, out var un) ? un : null,
                    UploadedAtPht = ToPh(l.UploadedAt),
                    Occurrences = l.Occurrences,
                    FirstSeenAtPht = ToPh(l.FirstSeen),
                    LastSeenAtPht = ToPh(l.LastSeen)
                }).ToList(),
                Rows = rows,
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