using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Services;

public static class ImportBatchStatus
{
    public const string Prepared = "Prepared";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string PartiallyCommitted = "PartiallyCommitted";
    public const string Committed = "Committed";
}

public static class ImportErrorStatus
{
    public const string Pending = "Pending";
    public const string Resolved = "Resolved";
    public const string Ignored = "Ignored";
}

/// <summary>Error types are added here one phase at a time, as each one is actually logged.</summary>
public static class ImportErrorTypes
{
    public const string FileUnreadable = "FileUnreadable";

    // Phase 6: template and sheet preparation. Each one stops the file from being read.
    public const string TemplateNotFound = "TemplateNotFound";
    public const string TemplateSelectionRequired = "TemplateSelectionRequired";
    public const string RequiredSheetMissing = "RequiredSheetMissing";
    public const string AmbiguousSheet = "AmbiguousSheet";
    public const string TruncatedSheetMatch = "TruncatedSheetMatch";
    public const string HeaderRowNotFound = "HeaderRowNotFound";
    public const string EmptySheet = "EmptySheet";
    public const string AmbiguousColumns = "AmbiguousColumns";
    public const string MissingRequiredHeaders = "MissingRequiredHeaders";
    public const string NoReadableSheet = "NoReadableSheet";
    public const string NoInvoiceRows = "NoInvoiceRows";

    // Phase 7: row-level and invoice-level errors found while preparing a readable file. They never fail the batch.
    public const string InvoiceCodeMissing = "InvoiceCodeMissing";
    public const string InvoiceDateMissing = "InvoiceDateMissing";
    public const string InvoiceDateInvalid = "InvoiceDateInvalid";
    public const string OrderTypeInvalid = "OrderTypeInvalid";
    public const string CustomerMissing = "CustomerMissing";
    public const string CustomerNotFound = "CustomerNotFound";
    public const string SkuMissing = "SkuMissing";
    public const string SkuNotFound = "SkuNotFound";
    public const string QuantityMissing = "QuantityMissing";
    public const string QuantityInvalid = "QuantityInvalid";
    public const string UomMissing = "UomMissing";
    public const string UomNotFound = "UomNotFound";
    public const string NoQuantityColumnValue = "NoQuantityColumnValue";
    public const string InvoiceNoValidLines = "InvoiceNoValidLines";
    public const string InvoiceInconsistent = "InvoiceInconsistent";
    public const string InvoiceHeaderInvalid = "InvoiceHeaderInvalid";
    /// <summary>A row whose salesman name is blank. Logged once per such row, so one master error covers all of them.</summary>
    public const string SalesmanMissing = "SalesmanMissing";
    public const string UnexpectedPrepareError = "UnexpectedPrepareError";

    // Phase 8: failures when an invoice is actually saved (commit). Logged once per commit, apart from the Check File errors.
    // A duplicate invoice number is deliberately not here: it is an expected rejection, shown in the results but never logged.
    public const string InvoiceSaveFailed = "InvoiceSaveFailed";
    public const string UnexpectedCommitError = "UnexpectedCommitError";
    /// <summary>Safety net: a row issue raised without a code. Logged so it is seen and can be given one.</summary>
    public const string UnclassifiedError = "UnclassifiedError";
}

/// <summary>
/// One error to save together with the others of its batch (see <see cref="ImportLogService.LogErrorsAsync"/>).
/// <paramref name="Identity"/> says which problem this is across batches; when null (file-level errors) the message is used.
/// <paramref name="Occurrences"/> is how many rows this entry covers in its own batch.
/// <paramref name="Data"/> is the summary kept on the ImportError itself (its first sighting). <paramref name="BatchData"/> is
/// this batch's own evidence, kept on ImportErrorBatch.DataJson (for sales invoices: every affected row with all its original
/// columns). When BatchData is null, Data is stored on both.
/// </summary>
public sealed record ImportErrorEntry(
    string ErrorType,
    string ErrorMessage,
    string? FieldName = null,
    int? SourceRowNumber = null,
    object? Data = null,
    ErrorIdentity? Identity = null,
    int Occurrences = 1,
    object? BatchData = null);

/// <summary>
/// How many errors a <see cref="ImportLogService.LogErrorsAsync"/> call was asked to save, and how many it did.
/// Of those saved, <paramref name="Created"/> became new ImportError records and <paramref name="Linked"/> were attached to an
/// error an earlier import (or an earlier call for the same batch) had already recorded.
/// </summary>
public sealed record ImportErrorLogOutcome(int Requested, int Saved, int Created = 0, int Linked = 0)
{
    public int Failed => Requested - Saved;
}

/// <summary>
/// Records import attempts (ImportBatch) and their errors (ImportError).
/// Best-effort by design: a logging failure is written to ILogger and never reaches the import.
/// Every call uses its own DbContext, so a failed import can't lose or poison its own log.
/// Stateless, so it is safe to register as a singleton.
/// </summary>
public sealed class ImportLogService
{
    // Column limits from the ImportBatch / ImportError tables.
    private const int FileNameMax = 255;
    private const int ImportTypeMax = 50;
    private const int PrincipalMax = 50;
    private const int ErrorTypeMax = 100;
    private const int ErrorMessageMax = 1000;
    private const int FieldNameMax = 100;

    // LogErrorsAsync saves in chunks so one failure can't lose the whole list. Each chunk is its own transaction.
    private const int ErrorChunkSize = 500;
    /// <summary>Most complete row snapshots kept per error per batch. Occurrences keep counting past it; the evidence is flagged Truncated.</summary>
    public const int MaxAffectedRows = 10_000;

    private const int MaxConsecutiveChunkFailures = 3;   // a database that is down shouldn't be tried once per chunk forever

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IDbContextFactory<EntrielContext> _contextFactory;
    private readonly ILogger<ImportLogService> _logger;

    // Configuration key ImportEvidence:WriteLegacyDataJson (default true). While true, ImportErrorBatch.DataJson is written exactly as
    // before, next to the new ImportErrorEvidence row. Only switch it off after evidence retrieval and display are verified.
    private readonly bool _writeLegacyDataJson;

    public ImportLogService(
        IDbContextFactory<EntrielContext> contextFactory, ILogger<ImportLogService> logger, IConfiguration? configuration = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _writeLegacyDataJson = !bool.TryParse(configuration?["ImportEvidence:WriteLegacyDataJson"], out var write) || write;
    }

    /// <summary>
    /// Creates a new batch with status Prepared. One call = one import attempt; nothing is ever reused.
    /// Returns null when the batch could not be written, in which case callers skip all further logging for this attempt.
    /// </summary>
    public async Task<int?> CreateBatchAsync(
        string importType,
        string fileName,
        int? subDistributorId,
        string? principal,
        int uploadedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = _contextFactory.CreateDbContext();
            var id = await NextIdAsync(context, "ImportBatchIdSequence", cancellationToken);

            context.ImportBatches.Add(new ImportBatch
            {
                ImportBatchId = id,
                ImportType = Truncate(importType, ImportTypeMax),
                FileName = Truncate(string.IsNullOrWhiteSpace(fileName) ? "(unknown)" : fileName, FileNameMax),
                SubDistributorId = subDistributorId,
                Principal = principal is null ? null : Truncate(principal, PrincipalMax),
                BatchStatus = ImportBatchStatus.Prepared,
                UploadedBy = uploadedBy,
                UploadedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync(cancellationToken);
            return id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Import log: could not create a batch for {ImportType} file {FileName}.", importType, fileName);
            return null;
        }
    }

    /// <summary>
    /// Saves one error against a batch. Same deduplication as <see cref="LogErrorsAsync"/> (one call with one entry); the
    /// message is the error's identity, so keep it free of row numbers and other per-file detail. <paramref name="data"/> is
    /// serialized to DataJson (keep it small and free of stack traces). Pass a null <paramref name="sourceRowNumber"/> for
    /// file-level and aggregated errors.
    /// </summary>
    public async Task LogErrorAsync(
        int importBatchId,
        string errorType,
        string errorMessage,
        string? fieldName = null,
        int? sourceRowNumber = null,
        object? data = null,
        CancellationToken cancellationToken = default)
    {
        await LogErrorsAsync(
            importBatchId,
            new[] { new ImportErrorEntry(errorType, errorMessage, fieldName, sourceRowNumber, data) },
            cancellationToken);
    }

    /// <summary>
    /// Saves many errors of one batch, in chunks of <see cref="ErrorChunkSize"/>, deduplicating against earlier batches:
    ///   - the same unresolved error (Pending or Ignored) already exists  -> the batch is linked to it (ImportErrorBatch row);
    ///                                                                       an Ignored error stays Ignored
    ///   - it only exists as Resolved                                     -> a new Pending error, PreviousImportErrorId pointing at it
    ///   - it doesn't exist                                               -> a new Pending error
    /// Either way the batch's own occurrences and evidence go on its ImportErrorBatch row, so nothing from an earlier batch is
    /// overwritten. Each chunk looks every fingerprint up in one query, reserves ids only for the errors it really creates, and is
    /// saved in its own transaction: a chunk that fails loses only itself; chunks already saved stay, later chunks are still tried.
    /// A failed chunk is not retried, except after a unique-index violation (a concurrent import created the same error first):
    /// that save rolled back completely, so one retry simply finds the winner and links to it. After
    /// <see cref="MaxConsecutiveChunkFailures"/> failed chunks in a row the rest is skipped and counted as not saved.
    /// Same best-effort rule as before: nothing is thrown (except cancellation). The outcome says how many were saved.
    /// </summary>
    public async Task<ImportErrorLogOutcome> LogErrorsAsync(
        int importBatchId,
        IReadOnlyCollection<ImportErrorEntry> errors,
        CancellationToken cancellationToken = default)
    {
        if (errors.Count == 0) return new ImportErrorLogOutcome(0, 0);

        var all = errors as IReadOnlyList<ImportErrorEntry> ?? errors.ToList();

        BatchIdentity? batch;
        try
        {
            batch = await LoadBatchIdentityAsync(importBatchId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Import log: could not read batch {ImportBatchId}; its {Count} errors were not recorded.", importBatchId, all.Count);
            return new ImportErrorLogOutcome(all.Count, 0);
        }

        if (batch is null)
        {
            _logger.LogError("Import log: batch {ImportBatchId} was not found; its {Count} errors were not recorded.", importBatchId, all.Count);
            return new ImportErrorLogOutcome(all.Count, 0);
        }

        var prepared = all.Select(e => PrepareEntry(batch, e)).ToList();
        var saved = 0;
        var created = 0;
        var linked = 0;
        var consecutiveFailures = 0;

        for (var offset = 0; offset < prepared.Count; offset += ErrorChunkSize)
        {
            if (consecutiveFailures >= MaxConsecutiveChunkFailures)
            {
                _logger.LogError(
                    "Import log: stopped saving errors for batch {ImportBatchId} after {Failures} failed chunks in a row; the remaining {Remaining} were not attempted.",
                    importBatchId, consecutiveFailures, prepared.Count - offset);
                break;
            }

            var chunk = prepared.GetRange(offset, Math.Min(ErrorChunkSize, prepared.Count - offset));

            try
            {
                var result = await SaveChunkWithRaceRetryAsync(importBatchId, chunk, cancellationToken);
                saved += chunk.Count;
                created += result.Created;
                linked += chunk.Count - result.Created;
                consecutiveFailures = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                consecutiveFailures++;
                _logger.LogError(ex,
                    "Import log: could not save errors {First}-{Last} of {Total} for batch {ImportBatchId}; these {Count} were not recorded.",
                    offset + 1, offset + chunk.Count, prepared.Count, importBatchId, chunk.Count);
            }
        }

        var outcome = new ImportErrorLogOutcome(all.Count, saved, created, linked);
        if (outcome.Failed > 0)
            _logger.LogWarning(
                "Import log: batch {ImportBatchId} saved {Saved} of {Requested} errors; {Failed} were not recorded.",
                importBatchId, outcome.Saved, outcome.Requested, outcome.Failed);

        return outcome;
    }

    private sealed record BatchIdentity(string ImportType, int? SubDistributorId);

    private sealed record PreparedError(ImportErrorEntry Entry, string Fingerprint);

    private async Task<BatchIdentity?> LoadBatchIdentityAsync(int importBatchId, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.ImportBatches
            .AsNoTracking()
            .Where(b => b.ImportBatchId == importBatchId)
            .Select(b => new BatchIdentity(b.ImportType, b.SubDistributorId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static PreparedError PrepareEntry(BatchIdentity batch, ImportErrorEntry entry)
    {
        // File-level errors carry no identity of their own, so their message is it.
        var identity = entry.Identity ?? new ErrorIdentity(entry.ErrorMessage);
        var fingerprint = ImportErrorFingerprint.Compute(batch.ImportType, batch.SubDistributorId, entry.ErrorType, entry.FieldName, identity);
        return new PreparedError(entry, fingerprint);
    }

    // A unique violation means a concurrent import created the same error between our lookup and our save. The failed save
    // rolled back as a whole, so doing the chunk once more (which now finds that error and links to it) can't save anything twice.
    private async Task<(int Created, int Linked)> SaveChunkWithRaceRetryAsync(
        int importBatchId, List<PreparedError> chunk, CancellationToken cancellationToken)
    {
        try
        {
            return await SaveChunkAsync(importBatchId, chunk, cancellationToken);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation("Import log: batch {ImportBatchId} raced another import on the same error; retrying its chunk once.", importBatchId);
            return await SaveChunkAsync(importBatchId, chunk, cancellationToken);
        }
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } };

    private async Task<(int Created, int Linked)> SaveChunkAsync(
        int importBatchId, List<PreparedError> chunk, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory.CreateDbContext();
        var fingerprints = chunk.Select(c => c.Fingerprint).Distinct().ToList();

        // 1. Open errors (Pending or Ignored) for these fingerprints. The unique index allows at most one per fingerprint.
        var open = await context.ImportErrors
            .AsNoTracking()
            .Where(e => e.Fingerprint != null && fingerprints.Contains(e.Fingerprint) && e.Status != ImportErrorStatus.Resolved)
            .Select(e => new { e.Fingerprint, e.ImportErrorId })
            .ToListAsync(cancellationToken);

        var errorIdByFingerprint = open.ToDictionary(e => e.Fingerprint!, e => e.ImportErrorId, StringComparer.Ordinal);
        var missing = fingerprints.Where(f => !errorIdByFingerprint.ContainsKey(f)).ToList();

        // 2. For the ones that don't exist as open errors: was it Resolved before? Then this is a recurrence.
        var previousByFingerprint = new Dictionary<string, int>(StringComparer.Ordinal);
        if (missing.Count > 0)
        {
            var resolved = await context.ImportErrors
                .AsNoTracking()
                .Where(e => e.Fingerprint != null && missing.Contains(e.Fingerprint) && e.Status == ImportErrorStatus.Resolved)
                .GroupBy(e => e.Fingerprint!)
                .Select(g => new { Fingerprint = g.Key, ImportErrorId = g.Max(e => e.ImportErrorId) })
                .ToListAsync(cancellationToken);

            foreach (var r in resolved) previousByFingerprint[r.Fingerprint] = r.ImportErrorId;
        }

        // 3. Links this batch already has to the errors that exist (it can log more than once: Check File, then Commit).
        var joinByErrorId = new Dictionary<int, ImportErrorBatch>();
        if (errorIdByFingerprint.Count > 0)
        {
            var existingIds = errorIdByFingerprint.Values.ToList();
            var joins = await context.ImportErrorBatches
                .Include(j => j.Evidence)
                .Where(j => j.ImportBatchId == importBatchId && existingIds.Contains(j.ImportErrorId))
                .ToListAsync(cancellationToken);

            foreach (var j in joins) joinByErrorId[j.ImportErrorId] = j;
        }

        // 4. Ids only for the errors this chunk really creates, so a normal run leaves no gaps in the sequence.
        long firstId = 0, increment = 1;
        if (missing.Count > 0)
            (firstId, increment) = await ReserveErrorIdsAsync(context, missing.Count, cancellationToken);

        var now = DateTime.UtcNow;
        var nextNew = 0;
        var created = 0;
        var createdErrorIds = new HashSet<int>();

        foreach (var item in chunk)
        {
            var entry = item.Entry;

            if (!errorIdByFingerprint.TryGetValue(item.Fingerprint, out var errorId))
            {
                errorId = checked((int)(firstId + nextNew++ * increment));
                int? previousId = previousByFingerprint.TryGetValue(item.Fingerprint, out var p) ? p : null;

                context.ImportErrors.Add(BuildError(
                    errorId, importBatchId, entry.ErrorType, entry.ErrorMessage, entry.FieldName, entry.SourceRowNumber,
                    entry.Data, item.Fingerprint, previousId, now));

                errorIdByFingerprint[item.Fingerprint] = errorId;
                createdErrorIds.Add(errorId);
                created++;
            }

            if (joinByErrorId.TryGetValue(errorId, out var join))
            {
                // The same error again in the same batch: count it and add any rows not stored yet (same sheet + row = same snapshot).
                join.Occurrences += entry.Occurrences;
                join.LastSeenAt = now;
                if (entry.BatchData is not null)
                    join.DataJson = MergeBatchData(join.DataJson ?? join.Evidence?.DataJson, entry.BatchData, join.Occurrences);
            }
            else
            {
                join = new ImportErrorBatch
                {
                    ImportErrorId = errorId,
                    ImportBatchId = importBatchId,
                    Occurrences = entry.Occurrences,
                    DataJson = SerializeOrNull(entry.BatchData ?? entry.Data),
                    FirstSeenAt = now,
                    LastSeenAt = now
                };
                context.ImportErrorBatches.Add(join);
                joinByErrorId[errorId] = join;
            }
        }

        // Evidence rows are added in the same SaveChanges (same transaction) as the errors and links they belong to.
        await AttachEvidenceAsync(context, joinByErrorId, createdErrorIds, now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return (created, chunk.Count - created);
    }

    private readonly record struct EvidenceKey(int ImportErrorId, string Hash);

    /// <summary>
    /// Points every ImportErrorBatch of this chunk at an ImportErrorEvidence row for its current evidence. One row exists per
    /// (error, evidence hash): identical evidence is reused, changed evidence gets a NEW row, and an evidence row that is already
    /// stored is never modified or deleted (a link that now has different evidence is simply repointed). The legacy DataJson is
    /// left as it is unless ImportEvidence:WriteLegacyDataJson is false.
    /// A concurrent import inserting the same (error, hash) first violates the unique index; the chunk rolls back and the
    /// existing single retry then finds that row.
    /// </summary>
    private async Task AttachEvidenceAsync(
        EntrielContext context,
        Dictionary<int, ImportErrorBatch> joins,
        HashSet<int> createdErrorIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var work = new List<(ImportErrorBatch Join, EvidenceKey Key, string Json)>();
        foreach (var join in joins.Values)
        {
            // DataJson is the evidence as it will be stored; when it was not stored (legacy writing off), the linked evidence is the same thing.
            var json = join.DataJson ?? join.Evidence?.DataJson;
            if (string.IsNullOrWhiteSpace(json)) continue;

            work.Add((join, new EvidenceKey(join.ImportErrorId, ImportEvidenceHasher.Hash(json)), json));
        }

        if (work.Count == 0) return;

        // Evidence that is already stored for these errors. A brand-new error cannot have any yet.
        var known = new Dictionary<EvidenceKey, int>();
        var existingErrorIds = work.Select(w => w.Key.ImportErrorId).Where(id => !createdErrorIds.Contains(id)).Distinct().ToList();
        if (existingErrorIds.Count > 0)
        {
            var hashes = work.Select(w => w.Key.Hash).Distinct().ToList();
            var found = await context.ImportErrorEvidences
                .AsNoTracking()
                .Where(e => existingErrorIds.Contains(e.ImportErrorId) && hashes.Contains(e.EvidenceHash))
                .Select(e => new { e.ImportErrorId, e.EvidenceHash, e.EvidenceId })
                .ToListAsync(cancellationToken);

            foreach (var f in found) known[new EvidenceKey(f.ImportErrorId, f.EvidenceHash)] = f.EvidenceId;
        }

        // New evidence: one row per distinct (error, hash), ids reserved in one round trip.
        var toCreate = work.Where(w => !known.ContainsKey(w.Key)).Select(w => w.Key).Distinct().ToList();
        var createdRows = new Dictionary<EvidenceKey, ImportErrorEvidence>();
        if (toCreate.Count > 0)
        {
            var (firstId, increment) = await ReserveIdsAsync(context, "ImportErrorEvidenceIdSequence", toCreate.Count, cancellationToken);
            var next = 0;
            foreach (var key in toCreate)
            {
                var row = new ImportErrorEvidence
                {
                    EvidenceId = checked((int)(firstId + next++ * increment)),
                    ImportErrorId = key.ImportErrorId,
                    EvidenceHash = key.Hash,
                    DataJson = work.First(w => w.Key == key).Json,
                    CreatedAt = now
                };
                context.ImportErrorEvidences.Add(row);
                createdRows[key] = row;
            }
        }

        foreach (var (join, key, _) in work)
        {
            if (createdRows.TryGetValue(key, out var created)) join.Evidence = created;
            else join.EvidenceId = known[key];

            if (!_writeLegacyDataJson) join.DataJson = null;
        }
    }

    /// <summary>
    /// Sets the batch status, and TotalRows when one is given (null leaves it unchanged).
    /// When <paramref name="onlyIfCurrentStatus"/> is given the change is applied only if the batch is currently in one of those
    /// statuses, so a late close can never overwrite Failed or Committed.
    /// </summary>
    public async Task CompleteBatchAsync(
        int importBatchId,
        string status,
        int? totalRows = null,
        IReadOnlyCollection<string>? onlyIfCurrentStatus = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = _contextFactory.CreateDbContext();
            var batch = await context.ImportBatches
                .FirstOrDefaultAsync(b => b.ImportBatchId == importBatchId, cancellationToken);

            if (batch is null)
            {
                _logger.LogWarning("Import log: batch {ImportBatchId} was not found, so its status was not set to {Status}.", importBatchId, status);
                return;
            }

            if (onlyIfCurrentStatus is not null && !onlyIfCurrentStatus.Contains(batch.BatchStatus))
                return;

            batch.BatchStatus = Truncate(status, 50);
            if (totalRows.HasValue) batch.TotalRows = totalRows.Value;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Import log: could not set batch {ImportBatchId} to {Status}.", importBatchId, status);
        }
    }

    // Plain ADO.NET on purpose: EF wraps raw SQL in a subquery when it composes a query over it,
    // and SQL Server rejects NEXT VALUE FOR inside a subquery.
    private static async Task<int> NextIdAsync(EntrielContext context, string sequenceName, CancellationToken cancellationToken)
    {
        var sql = sequenceName switch
        {
            "ImportBatchIdSequence" => "SELECT NEXT VALUE FOR dbo.ImportBatchIdSequence",
            "ImportErrorIdSequence" => "SELECT NEXT VALUE FOR dbo.ImportErrorIdSequence",
            _ => throw new ArgumentOutOfRangeException(nameof(sequenceName))
        };

        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Reserves <count> consecutive ids from the error sequence in ONE round trip (sp_sequence_get_range needs the same
    // UPDATE permission on the sequence as NEXT VALUE FOR). The ids are first, first + increment, ... so the increment
    // is read back rather than assumed. A range that wrapped past the sequence's end would no longer be unique, so it throws.
    private static Task<(long First, long Increment)> ReserveErrorIdsAsync(
        EntrielContext context, int count, CancellationToken cancellationToken) =>
        ReserveIdsAsync(context, "ImportErrorIdSequence", count, cancellationToken);

    // Same range reservation for any of the sequences below. The name is checked against this list, never built from input.
    private static async Task<(long First, long Increment)> ReserveIdsAsync(
        EntrielContext context, string sequenceName, int count, CancellationToken cancellationToken)
    {
        var qualifiedName = sequenceName switch
        {
            "ImportErrorIdSequence" => "dbo.ImportErrorIdSequence",
            "ImportErrorEvidenceIdSequence" => "dbo.ImportErrorEvidenceIdSequence",
            _ => throw new ArgumentOutOfRangeException(nameof(sequenceName))
        };

        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            DECLARE @first sql_variant, @increment sql_variant, @cycles int;
            EXEC sys.sp_sequence_get_range
                @sequence_name = @sequence,
                @range_size = @count,
                @range_first_value = @first OUTPUT,
                @range_cycle_count = @cycles OUTPUT,
                @sequence_increment = @increment OUTPUT;
            SELECT CONVERT(bigint, @first), CONVERT(bigint, @increment), @cycles;
            """;

        var size = command.CreateParameter();
        size.ParameterName = "@count";
        size.Value = (long)count;
        command.Parameters.Add(size);

        var sequence = command.CreateParameter();
        sequence.ParameterName = "@sequence";
        sequence.Value = qualifiedName;
        command.Parameters.Add(sequence);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The " + sequenceName + " did not return a range.");

        var first = reader.GetInt64(0);
        var increment = reader.GetInt64(1);
        if (reader.GetInt32(2) != 0)
            throw new InvalidOperationException("The " + sequenceName + " wrapped around while reserving ids, so they would not be unique.");

        return (first, increment);
    }

    private static string? SerializeOrNull(object? data) =>
        data is null ? null : JsonSerializer.Serialize(data, JsonOptions);

    /// <summary>
    /// Adds the incoming affected rows to a batch's stored evidence without duplicating a sheet + row that is already there.
    /// Everything else already stored (including evidence in the older format) is kept; Occurrences and AffectedInvoices are refreshed.
    /// </summary>
    private static string? MergeBatchData(string? existingJson, object incoming, int occurrences)
    {
        if (JsonSerializer.SerializeToNode(incoming, JsonOptions) is not JsonObject add)
            return existingJson;

        JsonObject? existing = null;
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try { existing = JsonNode.Parse(existingJson) as JsonObject; }
            catch (JsonException) { /* unreadable old evidence: replaced by the new one below */ }
        }
        if (existing is null) return add.ToJsonString(JsonOptions);

        if (existing["AffectedRows"] is not JsonArray rows)
        {
            rows = new JsonArray();
            existing["AffectedRows"] = rows;
        }

        var seen = new HashSet<(int Sheet, int Row)>();
        foreach (var r in rows)
            if (RowKey(r) is { } k) seen.Add(k);

        var truncated = existing["Truncated"] is JsonValue tv && tv.TryGetValue<bool>(out var wasTruncated) && wasTruncated;

        if (add["AffectedRows"] is JsonArray incomingRows)
        {
            foreach (var r in incomingRows)
            {
                if (RowKey(r) is not { } key || !seen.Add(key)) continue;
                if (rows.Count >= MaxAffectedRows) { truncated = true; break; }
                rows.Add(r!.DeepClone());
            }
        }

        if (truncated) existing["Truncated"] = true;
        existing["Occurrences"] = occurrences;

        var invoices = new SortedSet<string>(StringComparer.Ordinal);
        CollectStrings(existing["AffectedInvoices"], invoices);
        CollectStrings(add["AffectedInvoices"], invoices);
        existing["AffectedInvoices"] = new JsonArray(invoices.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());

        return existing.ToJsonString(JsonOptions);
    }

    private static (int Sheet, int Row)? RowKey(JsonNode? node) =>
        node is JsonObject o
        && o["SheetIndex"] is JsonValue s && s.TryGetValue<int>(out var sheet)
        && o["RowNumber"] is JsonValue r && r.TryGetValue<int>(out var row)
            ? (sheet, row)
            : null;

    private static void CollectStrings(JsonNode? node, ISet<string> into)
    {
        if (node is not JsonArray array) return;
        foreach (var n in array)
            if (n is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
                into.Add(text);
    }

    // DataJson on the error itself is a snapshot of its first sighting; the per-batch truth (and the counts) is on ImportErrorBatch.
    private static ImportError BuildError(
        int id, int importBatchId, string errorType, string errorMessage, string? fieldName, int? sourceRowNumber, object? data,
        string fingerprint, int? previousImportErrorId, DateTime createdAt) =>
        new()
        {
            ImportErrorId = id,
            ImportBatchId = importBatchId,                       // the batch that first recorded it
            ErrorType = Truncate(errorType, ErrorTypeMax),
            ErrorMessage = Truncate(string.IsNullOrWhiteSpace(errorMessage) ? "(no message)" : errorMessage, ErrorMessageMax),
            FieldName = fieldName is null ? null : Truncate(fieldName, FieldNameMax),
            Status = ImportErrorStatus.Pending,
            SourceRowNumber = sourceRowNumber,
            DataJson = data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            Fingerprint = fingerprint,
            PreviousImportErrorId = previousImportErrorId,
            CreatedAt = createdAt
        };

    // ErrorType and FieldName are varchar, so keep the ellipsis plain ASCII.
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 3)] + "...";
}