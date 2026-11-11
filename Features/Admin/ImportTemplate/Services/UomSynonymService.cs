using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using STTproject.Data;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Validators;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public interface IUomSynonymService
{
    /// <summary>
    /// The synonyms an import for this subdistributor uses: its own groups first, then the global ones it inherits
    /// (minus any group or spelling it opted out of). Throws when they can't be read, so an import stops instead of
    /// matching units differently than the admin set up.
    /// </summary>
    Task<UomSynonymMap> GetMapAsync(int? subDistributorId, CancellationToken ct = default);

    /// <summary>
    /// The groups of one scope for the admin modal (null = global). For a subdistributor it also returns the
    /// global groups it can inherit and what it has opted out of.
    /// </summary>
    Task<UomSynonymScopeDto> GetScopeAsync(int? subDistributorId, CancellationToken ct = default);

    Task<UomSynonymSaveResult> SaveScopeAsync(UomSynonymScopeDto dto, int userId, CancellationToken ct = default);

    /// <summary>Subdistributors with their own groups or opt-outs, for the modal's "With changes" list.</summary>
    Task<List<UomSynonymSubdSummaryDto>> GetSubdSummariesAsync(CancellationToken ct = default);
}

public sealed class UomSynonymService : IUomSynonymService
{
    private const string CacheKey = "import:uom-synonyms";

    // Evicted on every save on this server. The expiry only matters when more than one server runs the app.
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<EntrielContext> _factory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UomSynonymService> _logger;

    public UomSynonymService(IDbContextFactory<EntrielContext> factory, IMemoryCache cache, ILogger<UomSynonymService> logger)
    {
        _factory = factory;
        _cache = cache;
        _logger = logger;
    }

    private sealed record CachedSpelling(int Id, string Text);
    private sealed record CachedGroup(int Id, int? SubDistributorId, bool IsActive, IReadOnlyList<CachedSpelling> Spellings);
    private sealed record CachedExclusion(int SubDistributorId, int GroupId, int? SynonymId);
    private sealed record CachedData(IReadOnlyList<CachedGroup> Groups, IReadOnlyList<CachedExclusion> Exclusions);

    // ── import side ──────────────────────────────────────────────────

    public async Task<UomSynonymMap> GetMapAsync(int? subDistributorId, CancellationToken ct = default)
    {
        CachedData data;
        try
        {
            data = await GetCachedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "UOM synonyms could not be loaded.");
            throw;
        }

        var exclusions = subDistributorId is int id
            ? data.Exclusions.Where(e => e.SubDistributorId == id).ToList()
            : new List<CachedExclusion>();
        var offGroups = exclusions.Where(e => e.SynonymId is null).Select(e => e.GroupId).ToHashSet();
        var offSpellings = exclusions.Where(e => e.SynonymId is not null).Select(e => e.SynonymId!.Value).ToHashSet();

        var globals = data.Groups
            .Where(g => g.SubDistributorId is null && g.IsActive && !offGroups.Contains(g.Id))
            .Select(g => g.Spellings.Where(s => !offSpellings.Contains(s.Id)).Select(s => s.Text));

        IEnumerable<IEnumerable<string>> own = subDistributorId is null
            ? Array.Empty<IEnumerable<string>>()
            : data.Groups.Where(g => g.SubDistributorId == subDistributorId && g.IsActive).Select(g => g.Spellings.Select(s => s.Text));

        return UomSynonymMap.Build(own, globals);
    }

    private async Task<CachedData> GetCachedAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out CachedData? cached) && cached is not null)
            return cached;

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var groups = await ctx.UomSynonymGroups
            .AsNoTracking()
            .OrderBy(g => g.SortOrder).ThenBy(g => g.UomSynonymGroupId)
            .Select(g => new
            {
                g.UomSynonymGroupId,
                g.SubDistributorId,
                g.IsActive,
                Spellings = g.UomSynonyms.OrderBy(s => s.SortOrder)
                    .Select(s => new { s.UomSynonymId, s.SynonymText }).ToList()
            })
            .ToListAsync(ct);

        var exclusions = await ctx.UomSynonymExclusions
            .AsNoTracking()
            .Select(e => new CachedExclusion(e.SubDistributorId, e.UomSynonymGroupId, e.UomSynonymId))
            .ToListAsync(ct);

        var data = new CachedData(
            groups.Select(g => new CachedGroup(g.UomSynonymGroupId, g.SubDistributorId, g.IsActive,
                g.Spellings.Select(s => new CachedSpelling(s.UomSynonymId, s.SynonymText)).ToList())).ToList(),
            exclusions);

        _cache.Set(CacheKey, data, CacheFor);
        return data;
    }

    // ── admin modal ──────────────────────────────────────────────────

    public async Task<UomSynonymScopeDto> GetScopeAsync(int? subDistributorId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var rows = await ctx.UomSynonymGroups
            .AsNoTracking()
            .Include(g => g.UomSynonyms)
            .Where(g => g.SubDistributorId == subDistributorId || (subDistributorId != null && g.SubDistributorId == null))
            .OrderBy(g => g.SortOrder).ThenBy(g => g.UomSynonymGroupId)
            .ToListAsync(ct);

        var dto = new UomSynonymScopeDto
        {
            SubDistributorId = subDistributorId,
            Groups = rows.Where(g => g.SubDistributorId == subDistributorId).Select(ToDto).ToList()
        };

        if (subDistributorId is int subdId)
        {
            dto.GlobalGroups = rows.Where(g => g.SubDistributorId is null).Select(ToDto).ToList();

            dto.Exclusions = await ctx.UomSynonymExclusions
                .AsNoTracking()
                .Where(e => e.SubDistributorId == subdId)
                .Select(e => new UomSynonymExclusionDto(
                    e.UomSynonymGroupId,
                    e.UomSynonymId == null
                        ? null
                        : ctx.UomSynonyms.Where(s => s.UomSynonymId == e.UomSynonymId).Select(s => s.SynonymText).FirstOrDefault()))
                .ToListAsync(ct);
        }

        return dto;
    }

    public async Task<List<UomSynonymSubdSummaryDto>> GetSubdSummariesAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var own = await ctx.UomSynonymGroups.AsNoTracking()
            .Where(g => g.SubDistributorId != null)
            .GroupBy(g => g.SubDistributorId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var optOuts = await ctx.UomSynonymExclusions.AsNoTracking()
            .GroupBy(e => e.SubDistributorId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        return own.Keys.Union(optOuts.Keys)
            .Select(id => new UomSynonymSubdSummaryDto(id, own.GetValueOrDefault(id), optOuts.GetValueOrDefault(id)))
            .ToList();
    }

    public async Task<UomSynonymSaveResult> SaveScopeAsync(UomSynonymScopeDto dto, int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return UomSynonymSaveResult.Fail("Unable to identify the current user. Please sign in again.");

        NormalizeDto(dto);

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        if (dto.SubDistributorId is int subdCheck &&
            !await ctx.SubDistributors.AnyAsync(s => s.SubDistributorId == subdCheck && s.IsActive, ct))
            return UomSynonymSaveResult.Fail("The selected subdistributor was not found or is inactive.");

        var existing = await ctx.UomSynonymGroups
            .Include(g => g.UomSynonyms)
            .Where(g => g.SubDistributorId == dto.SubDistributorId)
            .ToListAsync(ct);
        var byId = existing.ToDictionary(g => g.UomSynonymGroupId);

        var errors = new List<string>();
        foreach (var g in dto.Groups.Where(g => g.UomSynonymGroupId != 0))
        {
            if (!byId.TryGetValue(g.UomSynonymGroupId, out var row))
                errors.Add($"Group \"{g.GroupName}\" was deleted by someone else. Reload and try again.");
            else if (row.Version != g.Version)
                errors.Add($"Group \"{g.GroupName}\" was changed by someone else since you opened it. Reload and try again.");
        }
        if (errors.Count > 0)
            return UomSynonymSaveResult.Fail(errors);

        // Validate the scope as it will be after this save, including any group someone else added meanwhile.
        var sentIds = dto.Groups.Where(g => g.UomSynonymGroupId != 0).Select(g => g.UomSynonymGroupId).ToHashSet();
        var deletedIds = dto.DeletedGroupIds.ToHashSet();
        var untouched = existing
            .Where(e => !sentIds.Contains(e.UomSynonymGroupId) && !deletedIds.Contains(e.UomSynonymGroupId))
            .Select(ToDto);

        errors = UomSynonymValidator.Validate(dto.Groups.Concat(untouched).ToList());
        if (errors.Count > 0)
            return UomSynonymSaveResult.Fail(errors);

        // Decide what changed before anything is touched, so only edited groups get a new version.
        var changedIds = dto.Groups
            .Select((g, i) => (g, Order: i + 1))
            .Where(x => x.g.UomSynonymGroupId != 0 && HasChanged(byId[x.g.UomSynonymGroupId], x.g, x.Order))
            .Select(x => x.g.UomSynonymGroupId)
            .ToHashSet();

        // Spellings about to disappear: from deleted groups, and those taken out of edited ones.
        var removedSpellings = existing.Where(e => deletedIds.Contains(e.UomSynonymGroupId)).SelectMany(e => e.UomSynonyms)
            .Concat(dto.Groups.Where(g => changedIds.Contains(g.UomSynonymGroupId))
                .SelectMany(g => byId[g.UomSynonymGroupId].UomSynonyms.Where(s => !g.Spellings.Contains(s.SynonymText))))
            .ToList();

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;

            // Opt-outs that point at a spelling or group going away go first: the spelling FK has no cascade.
            var removedSpellingIds = removedSpellings.Select(s => s.UomSynonymId).ToList();
            var deletedGroupIds = deletedIds.ToList();
            if (removedSpellingIds.Count > 0 || deletedGroupIds.Count > 0)
                await ctx.UomSynonymExclusions
                    .Where(e => (e.UomSynonymId != null && removedSpellingIds.Contains(e.UomSynonymId.Value))
                                || deletedGroupIds.Contains(e.UomSynonymGroupId))
                    .ExecuteDeleteAsync(ct);

            // Removals are saved on their own first, so a spelling moved from one group to another
            // is gone from the old group before the unique index sees it in the new one.
            foreach (var row in existing.Where(e => deletedIds.Contains(e.UomSynonymGroupId)))
            {
                ctx.UomSynonyms.RemoveRange(row.UomSynonyms.ToList());
                ctx.UomSynonymGroups.Remove(row);
            }
            ctx.UomSynonyms.RemoveRange(removedSpellings.Where(s => !deletedIds.Contains(s.UomSynonymGroupId)));

            await ctx.SaveChangesAsync(ct);

            for (var gi = 0; gi < dto.Groups.Count; gi++)
            {
                var g = dto.Groups[gi];
                UomSynonymGroup row;

                if (g.UomSynonymGroupId == 0)
                {
                    row = new UomSynonymGroup
                    {
                        SubDistributorId = dto.SubDistributorId,
                        Version = 1,
                        CreatedDate = now,
                        CreatedBy = userId
                    };
                    ctx.UomSynonymGroups.Add(row);
                }
                else
                {
                    if (!changedIds.Contains(g.UomSynonymGroupId)) continue;

                    row = byId[g.UomSynonymGroupId];
                    row.Version++;
                    row.UpdatedDate = now;
                    row.UpdatedBy = userId;
                }

                row.GroupName = g.GroupName;
                row.IsActive = g.IsActive;
                row.SortOrder = gi + 1;

                var kept = row.UomSynonyms
                    .Where(s => ctx.Entry(s).State is not (EntityState.Detached or EntityState.Deleted))
                    .ToDictionary(s => s.SynonymText, StringComparer.Ordinal);

                for (var si = 0; si < g.Spellings.Count; si++)
                {
                    if (!kept.TryGetValue(g.Spellings[si], out var syn))
                    {
                        syn = new UomSynonym { SynonymText = g.Spellings[si], SubDistributorId = dto.SubDistributorId };
                        row.UomSynonyms.Add(syn);
                    }
                    syn.SortOrder = si + 1;
                }
            }

            await ctx.SaveChangesAsync(ct);

            if (dto.SubDistributorId is int subdId)
                await ReplaceExclusionsAsync(ctx, subdId, dto.Exclusions, userId, now, ct);

            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return UomSynonymSaveResult.Fail("A spelling or group name here already exists in this scope, probably saved by someone else just now. Reload and try again.");
        }
        catch (DbUpdateException ex)
        {
            return UomSynonymSaveResult.Fail($"Could not save the UOM synonyms: {ex.GetBaseException().Message}");
        }

        _cache.Remove(CacheKey);
        return UomSynonymSaveResult.Ok();
    }

    // Replaces the subdistributor's opt-outs with the ones on screen. A spelling is matched by its text within its
    // global group; anything that no longer exists (deleted meanwhile) is dropped quietly.
    private static async Task ReplaceExclusionsAsync(
        EntrielContext ctx, int subdId, List<UomSynonymExclusionDto> wanted, int userId, DateTime now, CancellationToken ct)
    {
        await ctx.UomSynonymExclusions.Where(e => e.SubDistributorId == subdId).ExecuteDeleteAsync(ct);
        if (wanted.Count == 0) return;

        var groupIds = wanted.Select(w => w.UomSynonymGroupId).Distinct().ToList();
        var globals = await ctx.UomSynonymGroups
            .AsNoTracking()
            .Where(g => g.SubDistributorId == null && groupIds.Contains(g.UomSynonymGroupId))
            .Select(g => new { g.UomSynonymGroupId, Spellings = g.UomSynonyms.Select(s => new { s.UomSynonymId, s.SynonymText }).ToList() })
            .ToDictionaryAsync(g => g.UomSynonymGroupId, ct);

        var rows = new List<UomSynonymExclusion>();
        foreach (var w in wanted)
        {
            if (!globals.TryGetValue(w.UomSynonymGroupId, out var group)) continue;

            int? synonymId = null;
            if (w.Spelling is not null)
            {
                var match = group.Spellings.FirstOrDefault(s => s.SynonymText == w.Spelling);
                if (match is null) continue;
                synonymId = match.UomSynonymId;
            }

            rows.Add(new UomSynonymExclusion
            {
                SubDistributorId = subdId,
                UomSynonymGroupId = w.UomSynonymGroupId,
                UomSynonymId = synonymId,
                CreatedDate = now,
                CreatedBy = userId
            });
        }

        ctx.UomSynonymExclusions.AddRange(rows);
        await ctx.SaveChangesAsync(ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static void NormalizeDto(UomSynonymScopeDto dto)
    {
        dto.DeletedGroupIds ??= new();
        foreach (var g in dto.Groups)
        {
            g.GroupName = g.GroupName?.Trim() ?? string.Empty;
            // Lower-cased and blanks dropped; repeats are kept so the validator can report them.
            g.Spellings = (g.Spellings ?? new()).Select(UomSynonymMap.Norm).Where(s => s.Length > 0).ToList();
        }

        // A whole-group opt-out already covers its spellings, and repeats are dropped.
        var wholeGroups = (dto.Exclusions ?? new()).Where(e => e.Spelling is null).Select(e => e.UomSynonymGroupId).ToHashSet();
        dto.Exclusions = (dto.Exclusions ?? new())
            .Select(e => e with { Spelling = e.Spelling is null ? null : UomSynonymMap.Norm(e.Spelling) })
            .Where(e => e.Spelling is null || (e.Spelling.Length > 0 && !wholeGroups.Contains(e.UomSynonymGroupId)))
            .Distinct()
            .ToList();
    }

    private static bool HasChanged(UomSynonymGroup row, UomSynonymGroupEditDto g, int sortOrder) =>
        row.GroupName != g.GroupName
        || row.IsActive != g.IsActive
        || row.SortOrder != sortOrder
        || !row.UomSynonyms.OrderBy(s => s.SortOrder).Select(s => s.SynonymText).SequenceEqual(g.Spellings);

    private static UomSynonymGroupEditDto ToDto(UomSynonymGroup g) => new()
    {
        UomSynonymGroupId = g.UomSynonymGroupId,
        GroupName = g.GroupName,
        IsActive = g.IsActive,
        Version = g.Version,
        Spellings = g.UomSynonyms.OrderBy(s => s.SortOrder).Select(s => s.SynonymText).ToList()
    };
}