using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportErrors.DTOs;
using STTproject.Services;

namespace STTproject.Features.Admin.ImportErrors.Services
{
    public interface IImportErrorsExportService
    {
        Task<ImportErrorExportResult> ExportAsync(ImportErrorExportRequest request, CancellationToken ct = default);

        /// <summary>Most original files one export may contain (config key ImportErrorExport:MaxFiles, default 200).</summary>
        int MaxFiles { get; }

        /// <summary>Distinct files the request would export, using the same scope rules as ExportAsync.</summary>
        Task<int> GetFileCountAsync(ImportErrorExportRequest request, CancellationToken ct = default);

        /// <summary>One page of uploaded files for the picker. Independent of the error filters.</summary>
        Task<ImportFilePage> GetFilesAsync(ImportFileFilter filter, int page, int pageSize, CancellationToken ct = default);

        /// <summary>Up to <paramref name="limit"/> ImportBatchIds matching the filter (for "select all matching").</summary>
        Task<IReadOnlyList<int>> GetMatchingFileIdsAsync(ImportFileFilter filter, int limit, CancellationToken ct = default);

        Task<ImportFileFilterOptions> GetFileFilterOptionsAsync(CancellationToken ct = default);
    }

    public class ImportErrorsExportService : IImportErrorsExportService
    {
        private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private const int MaxCellChars = 32000; // Excel's hard limit is 32,767 per cell

        private readonly IDbContextFactory<EntrielContext> _factory;
        private readonly IImportFileStore _files;
        private readonly IUserContextService _user;
        private readonly int _maxFiles;

        public ImportErrorsExportService(IDbContextFactory<EntrielContext> factory, IImportFileStore files,
            IUserContextService user, IConfiguration configuration)
        {
            _factory = factory;
            _files = files;
            _user = user;

            // Interim limit: the ZIP is built in memory. Raise it once exports are streamed or run in the background.
            _maxFiles = int.TryParse(configuration["ImportErrorExport:MaxFiles"], out var configured) && configured > 0
                ? configured : 200;
        }

        public int MaxFiles => _maxFiles;

        // ---- neutral shapes: everything schema-specific is read once, in LoadAsync ----

        /// <summary>One affected Excel row, read from ImportErrorBatch.DataJson.</summary>
        /// <summary>RowData keeps the source file's own headers and cell text, in column order.</summary>
        private sealed record AffectedRow(string? Sheet, int? RowNumber, string? Invoice,
            IReadOnlyList<KeyValuePair<string, string>>? RowData);

        /// <summary>Everything stored for one (error, batch) pair: its rows, every affected invoice, and whether the row list was cut at the storage limit.</summary>
        private sealed record PairRows(List<AffectedRow> Rows, bool Truncated, List<string> Invoices);

        /// <summary>One line of the Affected Rows sheet, before the dynamic source columns are laid out.</summary>
        private sealed record AffectedEntry(int ErrorId, int BatchId, string? Sheet, int? Row, string? Note,
            IReadOnlyList<KeyValuePair<string, string>>? Data);

        private sealed record ErrorRec(int Id, string ErrorType, string Message, string? Field, string Value,
            string Status, string? ResolutionType, int? ResolvedBy, DateTime? ResolvedAt,
            int BatchId, string BatchFile, int? SourceRow, int? PreviousId);

        private sealed record LinkRec(int ErrorId, int BatchId, string FileName, string? Subdistributor,
            int? UploadedBy, DateTime UploadedAt, int Occurrences, DateTime? FirstSeen, DateTime? LastSeen, string? DataJson);

        private sealed record Sheet(string Name, string[] Headers, List<object?[]> Rows, int MaxColWidth = 60);

        /// <summary>One original file to export: one per ImportBatch.</summary>
        private sealed record FileRef(int BatchId, string FileName);

        public async Task<ImportErrorExportResult> ExportAsync(ImportErrorExportRequest request, CancellationToken ct = default)
        {
            // Rule 8: only a signed-in user can export, and only files tied to the errors they selected
            // are ever read (batch ids come from the database, never from the request).
            var userId = _user.UserId ?? 0;
            if (userId <= 0)
                throw new ImportErrorExportException("Your session could not be verified. Please sign in again.");

            var wantsReport = request.Content != ImportErrorExportContent.OriginalFiles;
            var wantsFiles = request.Content != ImportErrorExportContent.ErrorReport;
            var filesFromErrors = wantsFiles && request.FileScope == ImportErrorExportFileScope.RelatedToErrors;

            await using var ctx = await _factory.CreateDbContextAsync(ct);
            // Errors are needed for the report, and to find "files related to the selected errors".
            // "All files" and "Selected files" without a report never touch the error tables.
            var errors = new List<ErrorRec>();
            var links = new List<LinkRec>();
            if (wantsReport || filesFromErrors)
            {
                (errors, links) = await LoadAsync(ctx, request, ct);

                if (errors.Count == 0)
                    throw new ImportErrorExportException("There are no import errors to export for this selection.");
            }

            var stamp = DateTime.UtcNow;
            var result = new ImportErrorExportResult { ErrorCount = errors.Count };

            // ---- the report ----
            List<Sheet>? sheets = null;
            if (wantsReport)
            {
                var names = await LoadNamesAsync(ctx, errors, links, userId, ct);
                sheets = BuildSheets(errors, links, names, userId, stamp, request);
            }

            if (!wantsFiles)
            {
                if (request.Format == ImportErrorExportFormat.Xlsx)
                {
                    result.Bytes = BuildXlsx(sheets!);
                    result.FileName = "Entriel_Import_Error_Report.xlsx";
                    result.ContentType = XlsxType;
                }
                else
                {
                    result.Bytes = Zip(zip => AddReport(zip, sheets!, request.Format, xlsxName: null, csvFolder: string.Empty));
                    result.FileName = "Entriel_Import_Error_Report.zip";
                    result.ContentType = "application/zip";
                }
                return result;
            }

            // ---- original files: one per physical batch, even when many errors point at it ----
            var batches = await ResolveFilesAsync(ctx, request, links, ct);

            // A single file with no report goes out untouched, not inside a one-item ZIP.
            if (!wantsReport && batches.Count == 1)
            {
                var only = batches[0];
                result.Bytes = await _files.GetOriginalFileAsync(only.BatchId, ct)
                    ?? throw new ImportErrorExportException($"The original file \"{only.FileName}\" could not be found.");
                result.FileName = SafeName(only.FileName);
                result.ContentType = ContentTypeFor(only.FileName);
                result.OriginalFileCount = 1;
                return result;
            }

            var missing = new List<string>();
            var found = 0;
            // "Both" puts the folder at Original_Files/; originals-only wraps everything in Entriel_Original_Files/.
            var filesRoot = wantsReport ? "Original_Files" : "Entriel_Original_Files";

            var bytesOut = await ZipAsync(async zip =>
            {
                if (wantsReport) AddReport(zip, sheets!, request.Format, xlsxName: "Error_Report.xlsx", csvFolder: "Error_Report/");

                foreach (var b in batches)
                {
                    ct.ThrowIfCancellationRequested();
                    var bytes = await _files.GetOriginalFileAsync(b.BatchId, ct);
                    if (bytes == null) { missing.Add($"{b.FileName} (batch #{b.BatchId})"); continue; }

                    // Batch folders are kept even when file names are identical. Bytes are written as stored.
                    AddEntry(zip, $"{filesRoot}/Batch_{b.BatchId}/{SafeName(b.FileName)}", bytes);
                    found++;
                }

                if (missing.Count > 0)
                    AddEntry(zip, "Missing_Files.txt", Encoding.UTF8.GetBytes(
                        "These original files were not found on the server and are not in this export. "
                        + "Files uploaded before original-file storage was added were never kept.\r\n\r\n"
                        + string.Join("\r\n", missing)));
            });

            if (found == 0 && !wantsReport)
                throw new ImportErrorExportException("The original files for this selection could not be found.");

            result.Bytes = bytesOut;
            result.FileName = wantsReport ? $"Entriel_Export_{ToPh(stamp):yyyy-MM-dd}.zip" : "Entriel_Original_Files.zip";
            result.ContentType = "application/zip";
            result.OriginalFileCount = found;
            result.MissingFiles = missing;
            return result;
        }

        // =====================================================================================
        // Original files: scope, limit, and the file picker
        // =====================================================================================

        private int RequireSignedIn()
        {
            var userId = _user.UserId ?? 0;
            if (userId <= 0)
                throw new ImportErrorExportException("Your session could not be verified. Please sign in again.");
            return userId;
        }

        private ImportErrorExportException TooManyFiles(int count, string advice) =>
            new($"This export would include {count:N0} files, but the limit is {_maxFiles:N0} per export. {advice}");

        /// <summary>
        /// The distinct ImportBatches to export, decided on the server from the request's scope. Batch ids from the request are
        /// only ever used to look rows up again: a selected id that no longer exists or no longer matches the filter is refused.
        /// The limit counts distinct batches, never error-to-batch links, and is checked before any file is read.
        /// </summary>
        private async Task<List<FileRef>> ResolveFilesAsync(
            EntrielContext ctx, ImportErrorExportRequest request, List<LinkRec> links, CancellationToken ct)
        {
            switch (request.FileScope)
            {
                case ImportErrorExportFileScope.Selected:
                    {
                        var ids = (request.SelectedBatchIds ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
                        if (ids.Count == 0)
                            throw new ImportErrorExportException("Select at least one file to export.");
                        if (ids.Count > _maxFiles)
                            throw TooManyFiles(ids.Count, "Narrow your selection.");

                        var found = await FilteredFiles(ctx, request.FileFilter ?? new ImportFileFilter())
                            .Where(b => ids.Contains(b.ImportBatchId))
                            .OrderBy(b => b.ImportBatchId)
                            .Select(b => new FileRef(b.ImportBatchId, b.FileName))
                            .ToListAsync(ct);

                        if (found.Count != ids.Count)
                            throw new ImportErrorExportException(
                                "Some of the selected files no longer exist or no longer match the filters. Reopen the file list and select them again.");
                        return found;
                    }

                case ImportErrorExportFileScope.All:
                    {
                        var all = await ctx.ImportBatches.AsNoTracking()
                            .OrderBy(b => b.ImportBatchId)
                            .Select(b => new FileRef(b.ImportBatchId, b.FileName))
                            .Take(_maxFiles + 1)
                            .ToListAsync(ct);

                        if (all.Count == 0)
                            throw new ImportErrorExportException("There are no uploaded files to export.");
                        if (all.Count > _maxFiles)
                        {
                            var total = await ctx.ImportBatches.AsNoTracking().CountAsync(ct);
                            throw TooManyFiles(total, "Choose \"Select specific files\" and narrow the filters.");
                        }
                        return all;
                    }

                default:
                    {
                        // One entry per physical batch, even when many selected errors appeared in it.
                        var related = links.GroupBy(l => l.BatchId)
                            .Select(g => new FileRef(g.Key, g.First().FileName))
                            .OrderBy(f => f.BatchId)
                            .ToList();

                        if (related.Count == 0)
                            throw new ImportErrorExportException("None of the selected errors is linked to an uploaded file.");
                        if (related.Count > _maxFiles)
                            throw TooManyFiles(related.Count, "Narrow the error filters.");
                        return related;
                    }
            }
        }

        public async Task<int> GetFileCountAsync(ImportErrorExportRequest request, CancellationToken ct = default)
        {
            RequireSignedIn();
            await using var ctx = await _factory.CreateDbContextAsync(ct);

            switch (request.FileScope)
            {
                case ImportErrorExportFileScope.Selected:
                    return (request.SelectedBatchIds ?? new List<int>()).Where(i => i > 0).Distinct().Count();

                case ImportErrorExportFileScope.All:
                    return await ctx.ImportBatches.AsNoTracking().CountAsync(ct);

                default:
                    return await ResolveErrors(ctx, request)
                        .SelectMany(e => e.ImportErrorBatches)
                        .Select(l => l.ImportBatchId)
                        .Distinct()
                        .CountAsync(ct);
            }
        }

        public async Task<ImportFilePage> GetFilesAsync(ImportFileFilter filter, int page, int pageSize, CancellationToken ct = default)
        {
            RequireSignedIn();
            pageSize = Math.Clamp(pageSize, 1, 100);

            await using var ctx = await _factory.CreateDbContextAsync(ct);
            var q = FilteredFiles(ctx, filter ?? new ImportFileFilter());
            var total = await q.CountAsync(ct);

            var rows = await q
                .OrderByDescending(b => b.UploadedAt).ThenByDescending(b => b.ImportBatchId)
                .Skip((Math.Max(page, 1) - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new
                {
                    b.ImportBatchId,
                    b.FileName,
                    b.ImportType,
                    b.BatchStatus,
                    b.UploadedAt,
                    UploadedByName = b.UploadedByNavigation.Username,
                    SubdName = b.SubDistributor != null ? b.SubDistributor.SubdName : null
                })
                .ToListAsync(ct);

            return new ImportFilePage
            {
                Total = total,
                Items = rows.Select(r => new ImportFileListItemDto
                {
                    ImportBatchId = r.ImportBatchId,
                    FileName = r.FileName,
                    ImportType = r.ImportType,
                    BatchStatus = r.BatchStatus,
                    UploadedAtPht = ToPh(r.UploadedAt),
                    UploadedByName = r.UploadedByName,
                    SubDistributorName = r.SubdName
                }).ToList()
            };
        }

        public async Task<IReadOnlyList<int>> GetMatchingFileIdsAsync(ImportFileFilter filter, int limit, CancellationToken ct = default)
        {
            RequireSignedIn();
            await using var ctx = await _factory.CreateDbContextAsync(ct);
            return await FilteredFiles(ctx, filter ?? new ImportFileFilter())
                .OrderByDescending(b => b.UploadedAt).ThenByDescending(b => b.ImportBatchId)
                .Select(b => b.ImportBatchId)
                .Take(Math.Clamp(limit, 1, 10_000))
                .ToListAsync(ct);
        }

        public async Task<ImportFileFilterOptions> GetFileFilterOptionsAsync(CancellationToken ct = default)
        {
            RequireSignedIn();
            await using var ctx = await _factory.CreateDbContextAsync(ct);

            var subds = await ctx.ImportBatches.AsNoTracking()
                .Where(b => b.SubDistributorId != null && b.SubDistributor != null)
                .Select(b => new { Id = b.SubDistributorId!.Value, Name = b.SubDistributor!.SubdName })
                .Distinct()
                .ToListAsync(ct);

            var uploaders = await ctx.ImportBatches.AsNoTracking()
                .Select(b => new { Id = b.UploadedBy, Name = b.UploadedByNavigation.Username })
                .Distinct()
                .ToListAsync(ct);

            var types = await ctx.ImportBatches.AsNoTracking()
                .Select(b => b.ImportType).Distinct().OrderBy(t => t).ToListAsync(ct);

            return new ImportFileFilterOptions
            {
                SubDistributors = subds.OrderBy(x => x.Name)
                    .Select(x => new ImportFileOption { Id = x.Id, Name = x.Name }).ToList(),
                Uploaders = uploaders.Select(x => new ImportFileOption { Id = x.Id, Name = string.IsNullOrWhiteSpace(x.Name) ? $"User #{x.Id}" : x.Name })
                    .OrderBy(x => x.Name).ToList(),
                ImportTypes = types
            };
        }

        /// <summary>All uploaded batches matching the picker filters, with or without errors.</summary>
        internal static IQueryable<ImportBatch> FilteredFiles(EntrielContext ctx, ImportFileFilter f)
        {
            var q = ctx.ImportBatches.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim();
                q = q.Where(b => b.FileName.Contains(s));
            }
            if (f.SubDistributorId.HasValue) q = q.Where(b => b.SubDistributorId == f.SubDistributorId);
            if (f.UploadedBy.HasValue) q = q.Where(b => b.UploadedBy == f.UploadedBy);
            if (!string.IsNullOrWhiteSpace(f.ImportType))
            {
                var type = f.ImportType.Trim();
                q = q.Where(b => b.ImportType == type);
            }
            if (f.UploadedFrom.HasValue)
            {
                var from = PhDateToUtc(f.UploadedFrom.Value);
                q = q.Where(b => b.UploadedAt >= from);
            }
            if (f.UploadedTo.HasValue)
            {
                var to = PhDateToUtc(f.UploadedTo.Value.AddDays(1));   // inclusive end date
                q = q.Where(b => b.UploadedAt < to);
            }
            return q;
        }

        /// <summary>Midnight of a Philippine calendar date, as UTC.</summary>
        private static DateTime PhDateToUtc(DateTime phDate) =>
            DateTime.SpecifyKind(phDate.Date, DateTimeKind.Unspecified).AddHours(-8);

        // =====================================================================================
        // Data access. The one remaining ASSUMPTION is the User name property (see LoadNamesAsync).
        // If the build complains, fix them here and nowhere else.
        // =====================================================================================

        private static async Task<(List<ErrorRec> Errors, List<LinkRec> Links)> LoadAsync(
            EntrielContext ctx, ImportErrorExportRequest request, CancellationToken ct)
        {
            var errorsQuery = ResolveErrors(ctx, request);

            var raw = await errorsQuery
                .OrderBy(e => e.ImportErrorId)
                .Select(e => new
                {
                    e.ImportErrorId,
                    e.ErrorType,
                    e.ErrorMessage,
                    e.FieldName,
                    e.DataJson,
                    e.Status,
                    e.ResolutionType,
                    e.ResolvedAt,
                    e.ResolvedBy,
                    e.SourceRowNumber,
                    e.PreviousImportErrorId,
                    e.ImportBatchId,
                    BatchFile = e.ImportBatch.FileName
                })
                .ToListAsync(ct);

            var errors = raw.Select(r => new ErrorRec(
                r.ImportErrorId, r.ErrorType, r.ErrorMessage, r.FieldName,
                ProblemValue(r.ErrorType, r.DataJson),
                r.Status, r.ResolutionType, r.ResolvedBy, r.ResolvedAt,
                r.ImportBatchId, r.BatchFile, r.SourceRowNumber, r.PreviousImportErrorId)).ToList();

            var ids = errorsQuery.Select(e => e.ImportErrorId);
            var linkRows = await ctx.ImportErrorBatches.AsNoTracking()
                .Where(l => ids.Contains(l.ImportError.ImportErrorId))
                .Select(l => new
                {
                    ErrorId = l.ImportError.ImportErrorId,
                    BatchId = l.ImportBatch.ImportBatchId,
                    l.ImportBatch.FileName,
                    Subd = l.ImportBatch.SubDistributor != null ? l.ImportBatch.SubDistributor.SubdName : null,
                    UploadedBy = (int?)l.ImportBatch.UploadedBy,
                    l.ImportBatch.UploadedAt,
                    l.Occurrences,
                    FirstSeen = (DateTime?)l.FirstSeenAt,
                    LastSeen = (DateTime?)l.LastSeenAt,
                    // Linked evidence first; the legacy per-batch DataJson is the fallback (older rows, or evidence not linked yet).
                    DataJson = l.Evidence != null && l.Evidence.DataJson != null ? l.Evidence.DataJson : l.DataJson
                })
                .ToListAsync(ct);

            var links = linkRows.Select(l => new LinkRec(l.ErrorId, l.BatchId, l.FileName, l.Subd,
                l.UploadedBy, l.UploadedAt, l.Occurrences, l.FirstSeen, l.LastSeen, l.DataJson)).ToList();

            return (errors, links);
        }

        /// <summary>Display names for uploaders, resolvers and the exporting admin.</summary>
        private static async Task<Dictionary<int, string>> LoadNamesAsync(
            EntrielContext ctx, List<ErrorRec> errors, List<LinkRec> links, int exportedBy, CancellationToken ct)
        {
            var ids = errors.Where(e => e.ResolvedBy.HasValue).Select(e => e.ResolvedBy!.Value)
                .Concat(links.Where(l => l.UploadedBy.HasValue).Select(l => l.UploadedBy!.Value))
                .Append(exportedBy)
                .Distinct().ToList();

            return await ctx.Users.AsNoTracking()
                .Where(u => ids.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => u.Username, ct);
        }

        private static IQueryable<ImportError> ResolveErrors(EntrielContext ctx, ImportErrorExportRequest r)
        {
            if (r.AllErrors) return ctx.ImportErrors.AsNoTracking();

            var f = r.Filter ?? throw new ImportErrorExportException(
                "The current filters could not be read. Close this window and open Export again, or choose \"All import errors\".");

            // "All Errors" view: the filter is already about errors.
            if (!r.IsImportBatchView)
                return ImportErrorsService.FilteredErrors(ctx, f, includeStatus: true);

            // "By Import File" view: the filter picks files; export the errors found in those files.
            var batchIds = ImportErrorsService.FilteredBatches(ctx, f).Select(b => b.ImportBatchId);
            return ctx.ImportErrors.AsNoTracking()
                .Where(e => e.ImportErrorBatches.Any(l => batchIds.Contains(l.ImportBatch.ImportBatchId)));
        }

        /// <summary>
        /// Reads the evidence of one (error, batch) pair from ImportErrorBatch.DataJson. Evidence written before full row
        /// snapshots existed only has "SampleRows" (sheet, row, invoice), so those are used when "AffectedRows" is missing.
        /// If a batch has neither, the error's own SourceRowNumber is used for the batch that first recorded it.
        /// Each (sheet, row) appears once. Nothing is invented: a row without a stored snapshot has RowData = null.
        /// </summary>
        private static PairRows ParseAffectedRows(LinkRec link, ErrorRec? error)
        {
            var rows = new List<AffectedRow>();
            var invoices = new List<string>();
            var truncated = false;

            if (!string.IsNullOrWhiteSpace(link.DataJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(link.DataJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var root = doc.RootElement;

                        // Set when the stored row list hit ImportLogService.MaxAffectedRows.
                        truncated = TryProp(root, "Truncated", out var flag) && flag.ValueKind == JsonValueKind.True;

                        // Newer evidence lists every affected invoice; older evidence only has a count here.
                        if (TryProp(root, "AffectedInvoices", out var inv) && inv.ValueKind == JsonValueKind.Array)
                            foreach (var i in inv.EnumerateArray())
                                if (i.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(i.GetString()))
                                    invoices.Add(i.GetString()!);

                        var seen = new HashSet<(string?, int)>();
                        foreach (var listName in new[] { "AffectedRows", "SampleRows" })
                        {
                            if (!TryProp(root, listName, out var list) || list.ValueKind != JsonValueKind.Array) continue;

                            foreach (var item in list.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.Object) continue;

                                int? rowNumber = TryProp(item, "RowNumber", out var rn) && rn.ValueKind == JsonValueKind.Number && rn.TryGetInt32(out var n)
                                    ? n : null;
                                var sheetName = Text(item, "SheetName");

                                // One entry per distinct Excel row per error per batch.
                                if (rowNumber.HasValue && !seen.Add((sheetName, rowNumber.Value))) continue;

                                rows.Add(new AffectedRow(
                                    sheetName,
                                    rowNumber,
                                    Text(item, "InvoiceNumber") ?? Text(item, "InvoiceCode"),
                                    ReadRowData(item)));
                            }

                            if (rows.Count > 0) break;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Unreadable evidence: fall through to the fallback below.
                }
            }

            if (rows.Count == 0 && error is { SourceRow: not null } && error.BatchId == link.BatchId)
                rows.Add(new AffectedRow(null, error.SourceRow, null, null));

            return new PairRows(rows, truncated, invoices);
        }

        /// <summary>The stored row snapshot as header/value pairs in the order they were saved, or null when the row has none.</summary>
        private static List<KeyValuePair<string, string>>? ReadRowData(JsonElement item)
        {
            if (!TryProp(item, "RowData", out var data) || data.ValueKind != JsonValueKind.Object) return null;

            var list = new List<KeyValuePair<string, string>>();
            foreach (var p in data.EnumerateObject())
            {
                var text = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                    _ => p.Value.ToString()
                };
                list.Add(new KeyValuePair<string, string>(p.Name, text));
            }
            return list;
        }

        /// <summary>
        /// The value that is actually wrong. Row-level errors store it as "Value". File-level errors (a missing sheet, an
        /// unreadable file, ...) have no cell value, so it is read from what they do store. Never the error message again.
        /// </summary>
        private static string ProblemValue(string errorType, string? dataJson)
        {
            var direct = ImportErrorPresentation.ExtractProblemValue(dataJson, string.Empty);
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            if (string.IsNullOrWhiteSpace(dataJson)) return string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return string.Empty;

                bool Is(string type) => string.Equals(errorType, type, StringComparison.OrdinalIgnoreCase);
                var sheet = Text(root, "SheetName");
                var detail = Text(root, "Detail");

                if (Is(ImportErrorTypes.FileUnreadable))
                {
                    var file = Text(root, "FileName");
                    var code = Text(root, "DiagnosticCode");
                    var reason = code switch
                    {
                        "InvalidExcelFile" => "not a valid or readable Excel workbook",
                        "StreamNotReadable" => "the upload could not be read",
                        _ => Text(root, "DiagnosticMessage") ?? code ?? "the file could not be read"
                    };
                    return string.IsNullOrWhiteSpace(file) ? $"File could not be read: {reason}" : $"{file}: {reason}";
                }

                if (Is(ImportErrorTypes.TemplateSelectionRequired))
                {
                    var names = TextList(root, "CandidateNames");
                    return names is null ? (Text(root, "ImportType") ?? string.Empty) : $"Candidate templates: {names}";
                }

                if (Is(ImportErrorTypes.TemplateNotFound))
                {
                    var subd = Text(root, "SubDistributorId");
                    return $"No active {Text(root, "ImportType") ?? "import"} template" + (subd is null ? "" : $" (subdistributor #{subd})");
                }

                if (Is(ImportErrorTypes.NoInvoiceRows))
                {
                    var read = TextList(root, "SheetsRead");
                    return read is null ? string.Empty : $"Sheets read: {read}";
                }

                if (Is(ImportErrorTypes.MissingRequiredHeaders) || Is(ImportErrorTypes.AmbiguousColumns))
                    return sheet is null ? detail ?? string.Empty : detail is null ? sheet : $"{sheet}: {detail}";

                // RequiredSheetMissing, AmbiguousSheet and the other sheet-level errors: the sheet name.
                return sheet ?? detail ?? Text(root, "Context") ?? string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string? TextList(JsonElement obj, string name)
        {
            if (!TryProp(obj, name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
            var items = v.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var joined = string.Join("; ", items);
            return joined.Length == 0 ? null : joined;
        }

        /// <summary>The error type as an admin reads it. The technical type stays in its own column.</summary>
        private static string FriendlyType(string? errorType)
        {
            bool Is(string type) => string.Equals(errorType, type, StringComparison.OrdinalIgnoreCase);
            if (Is(ImportErrorTypes.SkuNotFound)) return "Item not found";
            if (Is(ImportErrorTypes.CustomerNotFound)) return "Customer not found";
            if (Is(ImportErrorTypes.UomNotFound)) return "Unit of measure not found";
            return ImportErrorPresentation.Title(errorType);
        }

        private static bool TryProp(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
            value = default;
            return false;
        }

        private static string? Text(JsonElement obj, string name)
        {
            if (!TryProp(obj, name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                or JsonValueKind.Object or JsonValueKind.Array) return null;
            var s = v.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        // =====================================================================================
        // Report sheets
        // =====================================================================================

        private static readonly string[] AffectedFixedHeaders = { "Error ID", "Batch ID", "Sheet Name", "Excel Row Number", "Data Note" };

        private static List<Sheet> BuildSheets(List<ErrorRec> errors, List<LinkRec> links,
            Dictionary<int, string> names, int exportedBy, DateTime exportedAt, ImportErrorExportRequest req)
        {
            // One label for every person column, so a missing or deleted account always reads the same way.
            string UserLabel(int? id) =>
                !id.HasValue ? "Unknown user"
                : names.TryGetValue(id.Value, out var n) && !string.IsNullOrWhiteSpace(n) ? n
                : $"Unknown user (#{id.Value})";

            var byError = links.ToLookup(l => l.ErrorId);
            var errorById = errors.ToDictionary(e => e.Id);

            // Evidence per (error, batch), read once and shared by Error Details, Summary and Affected Rows.
            var affected = new Dictionary<(int ErrorId, int BatchId), PairRows>();
            foreach (var l in links)
                affected[(l.ErrorId, l.BatchId)] = ParseAffectedRows(l, errorById.GetValueOrDefault(l.ErrorId));

            var sheets = new List<Sheet>();

            // ---- Summary (optional) ----
            if (req.IncludeSummary)
            {
                int Count(string key) => errors.Count(e => ImportErrorPresentation.StatusKey(e.Status) == key);
                var pending = Count(ImportErrorPresentation.Pending);
                var ignored = Count(ImportErrorPresentation.Ignored);
                var resolved = Count(ImportErrorPresentation.Resolved);

                var subds = links.Select(l => l.Subdistributor)
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())
                    .Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

                var rows = new List<object?[]>
                {
                    new object?[] { "Total Errors", errors.Count },
                    new object?[] { "Pending", pending },
                    new object?[] { "Ignored", ignored },
                    new object?[] { "Resolved", resolved },
                };

                // Only appears if a stored status is none of the three, so the counts can never silently stop adding up.
                var other = errors.Count - pending - ignored - resolved;
                if (other > 0) rows.Add(new object?[] { "Other Status", other });

                rows.Add(new object?[] { "Total Occurrences", links.Sum(l => l.Occurrences) });
                rows.Add(new object?[] { "Affected Subdistributors",
                    subds.Count == 0 ? "None" : $"{subds.Count}: {string.Join("; ", subds)}" });
                rows.Add(new object?[] { "Exported By", UserLabel(exportedBy) });
                rows.Add(new object?[] { "Export Date (PHT)", ToPh(exportedAt) });

                // Say so when the Affected Rows sheet cannot be the complete picture.
                if (req.IncludeAffectedRows)
                {
                    var pairs = links.DistinctBy(l => (l.ErrorId, l.BatchId)).Select(l => affected[(l.ErrorId, l.BatchId)]).ToList();
                    var cut = pairs.Count(p => p.Truncated);
                    var noSnapshot = pairs.Count(p => p.Rows.Count == 0 || p.Rows.Any(r => r.RowData is null));

                    if (cut > 0)
                        rows.Add(new object?[] { "Affected Rows Note",
                            $"{cut:N0} error/batch combination(s) reached the {ImportLogService.MaxAffectedRows:N0}-row storage limit, so their row lists are incomplete." });
                    if (noSnapshot > 0)
                        rows.Add(new object?[] { "Original Row Data Note",
                            $"{noSnapshot:N0} error/batch combination(s) have no stored original row data (older imports or file-level errors)." });
                }

                sheets.Add(new Sheet("Summary", new[] { "Information", "Value" }, rows));
            }

            // ---- Error Details (always; one row per unique error) ----
            sheets.Add(new Sheet("Error Details", new[]
            {
                "Error ID", "Error Type", "Error Message", "Field Name", "Problematic Value", "Subdistributor", "Status",
                "Total Occurrences", "Affected Invoices", "First Detected (PHT)", "Last Detected (PHT)",
                "Resolution Type", "Resolution Note", "Resolved By", "Resolved At (PHT)", "Previous Error ID",
                "Error Type (Technical)"
            }, errors.Select(e =>
            {
                var l = byError[e.Id].ToList();

                // Ignored and Resolved are different actions. Only a Resolved error shows resolution data; nothing is
                // inferred for Pending or Ignored errors because no audit trail is stored for them.
                var isResolved = ImportErrorPresentation.StatusKey(e.Status) == ImportErrorPresentation.Resolved;

                var invoices = l.SelectMany(x =>
                    {
                        var p = affected[(x.ErrorId, x.BatchId)];
                        return p.Invoices.Concat(p.Rows.Select(r => r.Invoice ?? string.Empty));
                    })
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal).ToList();

                return new object?[]
                {
                    e.Id,
                    FriendlyType(e.ErrorType),
                    e.Message,
                    ImportErrorPresentation.FieldLabel(e.Field),
                    e.Value,
                    string.Join("; ", l.Select(x => x.Subdistributor).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
                    ImportErrorPresentation.StatusText(e.Status),
                    l.Sum(x => x.Occurrences),
                    string.Join(", ", invoices),
                    l.Count == 0 ? null : ToPh(l.Min(x => x.FirstSeen ?? x.UploadedAt)),
                    l.Count == 0 ? null : ToPh(l.Max(x => x.LastSeen ?? x.UploadedAt)),
                    isResolved ? e.ResolutionType : null,
                    null, // ImportError has no resolution-note column yet; fill this in once one exists.
                    isResolved && e.ResolvedBy.HasValue ? UserLabel(e.ResolvedBy) : null,
                    isResolved ? ToPh(e.ResolvedAt) : null,
                    e.PreviousId,
                    e.ErrorType
                };
            }).ToList()));

            // ---- Import History (optional; one row per ImportErrorBatch) ----
            if (req.IncludeImportHistory)
            {
                sheets.Add(new Sheet("Import History", new[]
                {
                    "Error ID", "Batch ID", "Original Filename", "Subdistributor", "Uploaded By", "Uploaded At (PHT)",
                    "Occurrences", "First Seen (PHT)", "Last Seen (PHT)"
                }, links.OrderBy(l => l.ErrorId).ThenBy(l => l.UploadedAt).Select(l => new object?[]
                {
                    l.ErrorId, l.BatchId, l.FileName, l.Subdistributor, UserLabel(l.UploadedBy), ToPh(l.UploadedAt),
                    l.Occurrences, ToPh(l.FirstSeen), ToPh(l.LastSeen)
                }).ToList()));
            }

            // ---- Affected Rows (optional; the stored JSON becomes real columns) ----
            if (req.IncludeAffectedRows)
                sheets.Add(BuildAffectedRowsSheet(links, affected));

            return sheets;
        }

        /// <summary>
        /// One line per distinct affected Excel row per error per batch. The original file's own headers become columns, built
        /// from whatever the stored snapshots contain (different subdistributor templates give different columns), so nothing
        /// is hardcoded. The storage format is untouched: this only reshapes it at export time.
        /// </summary>
        private static Sheet BuildAffectedRowsSheet(List<LinkRec> links, Dictionary<(int ErrorId, int BatchId), PairRows> affected)
        {
            var entries = new List<AffectedEntry>();

            foreach (var l in links.OrderBy(l => l.ErrorId).ThenBy(l => l.BatchId).DistinctBy(l => (l.ErrorId, l.BatchId)))
            {
                var pair = affected[(l.ErrorId, l.BatchId)];

                if (pair.Rows.Count == 0)
                {
                    entries.Add(new AffectedEntry(l.ErrorId, l.BatchId, null, null,
                        "No row-level details were stored for this batch (a file-level error, or an older import).", null));
                    continue;
                }

                // Never imply the list is complete when it is not.
                string? partial = null;
                if (pair.Truncated)
                    partial = $"Truncated: only {pair.Rows.Count:N0} of {l.Occurrences:N0} affected rows were stored (limit {ImportLogService.MaxAffectedRows:N0}).";
                else if (pair.Rows.Count < l.Occurrences)
                    partial = $"Partial list: {pair.Rows.Count:N0} of {l.Occurrences:N0} affected rows were stored for this batch.";

                foreach (var r in pair.Rows.OrderBy(r => r.Sheet, StringComparer.Ordinal).ThenBy(r => r.RowNumber))
                {
                    var notes = new List<string>();
                    if (r.RowData is null) notes.Add("Original row data is not available (no row snapshot was stored for this import).");
                    if (partial is not null) notes.Add(partial);

                    entries.Add(new AffectedEntry(l.ErrorId, l.BatchId, r.Sheet, r.RowNumber,
                        notes.Count == 0 ? null : string.Join(" ", notes), r.RowData));
                }
            }

            // Combined column set: source headers in the order they are first met. A file that lacks a header leaves that cell blank.
            var headers = new List<string>(AffectedFixedHeaders);
            var columnOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var shown = new HashSet<string>(StringComparer.Ordinal);

            foreach (var e in entries)
            {
                if (e.Data is null) continue;
                foreach (var kv in e.Data)
                {
                    if (columnOf.ContainsKey(kv.Key)) continue;

                    // A source column that happens to share a name with one of ours is labelled, never merged.
                    var display = AffectedFixedHeaders.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) ? kv.Key + " (source)" : kv.Key;
                    for (var n = 2; !shown.Add(display); n++) display = $"{kv.Key} ({n})";

                    columnOf[kv.Key] = headers.Count;
                    headers.Add(display);
                }
            }

            var rows = new List<object?[]>(entries.Count);
            foreach (var e in entries)
            {
                var row = new object?[headers.Count];
                row[0] = e.ErrorId;
                row[1] = e.BatchId;
                row[2] = e.Sheet;
                row[3] = e.Row;
                row[4] = e.Note;

                if (e.Data is not null)
                    foreach (var kv in e.Data)
                        row[columnOf[kv.Key]] = kv.Value;

                rows.Add(row);
            }

            return new Sheet("Affected Rows", headers.ToArray(), rows, MaxColWidth: 45);
        }

        // =====================================================================================
        // Writers
        // =====================================================================================

        private const int ExcelMaxRows = 1_048_576;

        private static byte[] BuildXlsx(List<Sheet> sheets)
        {
            using var wb = new XLWorkbook();
            foreach (var s in sheets)
            {
                if (s.Rows.Count + 1 > ExcelMaxRows)
                    throw new ImportErrorExportException(
                        $"The \"{s.Name}\" sheet would need {s.Rows.Count + 1:N0} rows, more than an Excel sheet can hold. " +
                        "Narrow the filters, or choose the CSV format.");

                var ws = wb.Worksheets.Add(s.Name);
                var cols = s.Headers.Length;

                for (var c = 0; c < cols; c++)
                {
                    var h = ws.Cell(1, c + 1);
                    h.Value = s.Headers[c];
                    h.Style.Font.Bold = true;
                    h.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFE9E2");
                }

                for (var r = 0; r < s.Rows.Count; r++)
                    for (var c = 0; c < cols; c++)
                        WriteCell(ws.Cell(r + 2, c + 1), s.Rows[r][c]);

                // Same look on every sheet: wrapped text, top-aligned, filter on the header row, header row frozen.
                var used = ws.Range(1, 1, s.Rows.Count + 1, cols);
                used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                used.Style.Alignment.WrapText = true;
                ws.Range(1, 1, 1, cols).SetAutoFilter();

                // Readable widths, capped so one long value does not make a column enormous (wrapping handles the rest).
                for (var c = 0; c < cols; c++)
                {
                    var longest = s.Headers[c].Length;
                    foreach (var row in s.Rows.Take(200))
                        longest = Math.Max(longest, CellText(row[c]).Length);
                    ws.Column(c + 1).Width = Math.Min(s.MaxColWidth, Math.Max(10, longest + 2));
                }

                ws.SheetView.FreezeRows(1);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteCell(IXLCell cell, object? v)
        {
            switch (v)
            {
                case null: return;
                case DateTime d:
                    cell.Value = d;
                    cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    return;
                case int i: cell.Value = i; return;
                default: cell.Value = Safe(Truncate(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty)); return;
            }
        }

        private static byte[] BuildCsv(Sheet s)
        {
            var sb = new StringBuilder();
            AppendCsvLine(sb, s.Headers);
            foreach (var row in s.Rows)
                AppendCsvLine(sb, row.Select(c => Safe(CellText(c))));

            // BOM so Excel opens the file as UTF-8.
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        private static void AppendCsvLine(StringBuilder sb, IEnumerable<string> cells)
        {
            sb.Append(string.Join(",", cells.Select(c =>
                c.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + c.Replace("\"", "\"\"") + "\"" : c)));
            sb.Append("\r\n");
        }

        /// <summary>Puts the report into a ZIP: one XLSX (when xlsxName is given) or one CSV per sheet.</summary>
        private static void AddReport(ZipArchive zip, List<Sheet> sheets, ImportErrorExportFormat format, string? xlsxName, string csvFolder)
        {
            if (format == ImportErrorExportFormat.Xlsx)
                AddEntry(zip, xlsxName ?? "Error_Report.xlsx", BuildXlsx(sheets));
            else
                foreach (var s in sheets)
                    AddEntry(zip, $"{csvFolder}{s.Name.Replace(' ', '_')}.csv", BuildCsv(s));
        }

        // ---- helpers ----

        private static byte[] Zip(Action<ZipArchive> fill)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) fill(zip);
            return ms.ToArray();
        }

        private static async Task<byte[]> ZipAsync(Func<ZipArchive, Task> fill)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) await fill(zip);
            return ms.ToArray();
        }

        private static string CellText(object? v) => v switch
        {
            null => string.Empty,
            DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty
        };

        /// <summary>Stored timestamps are UTC (ImportLogService writes DateTime.UtcNow). The report shows Philippine time (UTC+8, no DST).</summary>
        private static DateTime ToPh(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(8);

        private static DateTime? ToPh(DateTime? utc) => utc.HasValue ? ToPh(utc.Value) : null;

        private const string TruncationNotice = " ... [cut off: longer than Excel allows in one cell]";

        /// <summary>Excel cells hold at most 32,767 characters. A longer value is cut, and says so in the cell.</summary>
        private static string Truncate(string s) =>
            s.Length <= MaxCellChars ? s : s[..(MaxCellChars - TruncationNotice.Length)] + TruncationNotice;

        /// <summary>Values come from uploaded spreadsheets. Defuse anything Excel would run as a formula.</summary>
        private static string Safe(string s) =>
            s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0 ? "'" + s : s;

        private static string SafeName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var name = string.Concat((fileName ?? string.Empty).Trim().Select(ch => invalid.Contains(ch) ? '_' : ch));
            return string.IsNullOrWhiteSpace(name) ? "file.xlsx" : name;
        }

        private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => XlsxType,
            ".xls" => "application/vnd.ms-excel",
            ".csv" => "text/csv",
            _ => "application/octet-stream"
        };

        private static void AddEntry(ZipArchive zip, string path, byte[] bytes)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
            using var s = entry.Open();
            s.Write(bytes, 0, bytes.Length);
        }
    }
}