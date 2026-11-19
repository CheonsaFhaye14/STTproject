using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.User.MapItem.Services;

/// <summary>
/// Tells whether two UOM names mean the same unit for a sub-distributor: the same name, the built-in spellings
/// (CS = CASE, PC = PIECE = EA, PK = PACK …, see <see cref="StockUomSuggester"/>), or the admin's UOM synonym groups
/// (global or the subd's own, minus its exclusions).
///
/// Used to find which of an item's UOMs is the company stock UOM (CompanyItem.StockUomName). Nothing is stored for it:
/// the stock UOM is always conversion 1, and an item doesn't need a row for it.
/// </summary>
public sealed class UomNameMatcher
{
    // Per subd: normalized name -> the synonym groups it belongs to (global groups included, exclusions removed).
    private readonly Dictionary<int, Dictionary<string, HashSet<int>>> _groupsBySubd;

    private UomNameMatcher(Dictionary<int, Dictionary<string, HashSet<int>>> groupsBySubd) => _groupsBySubd = groupsBySubd;

    /// <summary>A matcher that only knows exact names and the built-in spellings.</summary>
    public static UomNameMatcher BuiltInOnly { get; } = new(new());

    /// <summary>Letters only, upper case: "Case (12)" and "case" both become "CASE".</summary>
    public static string Key(string? name) =>
        new string((name ?? string.Empty).Where(char.IsLetter).ToArray()).ToUpperInvariant();

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are the same unit for this subd.</summary>
    public bool Same(int subDistributorId, string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        var keyA = Key(a);
        var keyB = Key(b);
        if (keyA.Length == 0 || keyB.Length == 0) return false;
        if (keyA == keyB) return true;
        if (StockUomSuggester.Canon(a) == StockUomSuggester.Canon(b)) return true;

        return _groupsBySubd.TryGetValue(subDistributorId, out var groups)
               && groups.TryGetValue(keyA, out var ga)
               && groups.TryGetValue(keyB, out var gb)
               && ga.Overlaps(gb);
    }

    /// <summary>True when <paramref name="uomName"/> is the company stock UOM for this subd.</summary>
    public bool IsStockUom(int subDistributorId, string? uomName, string? companyStockUomName) =>
        !string.IsNullOrWhiteSpace(companyStockUomName) && Same(subDistributorId, uomName, companyStockUomName);

    /// <summary>Loads the active synonym groups that apply to the given sub-distributors.</summary>
    public static async Task<UomNameMatcher> LoadAsync(
        EntrielContext context, IEnumerable<int> subDistributorIds, CancellationToken ct = default)
    {
        var subdIds = subDistributorIds.Where(id => id > 0).Distinct().ToList();
        if (subdIds.Count == 0) return BuiltInOnly;

        var synonyms = await (
                from s in context.UomSynonyms.AsNoTracking()
                join g in context.UomSynonymGroups.AsNoTracking() on s.UomSynonymGroupId equals g.UomSynonymGroupId
                where g.IsActive
                      && (g.SubDistributorId == null || subdIds.Contains(g.SubDistributorId.Value))
                      && (s.SubDistributorId == null || subdIds.Contains(s.SubDistributorId.Value))
                select new
                {
                    s.UomSynonymId,
                    s.UomSynonymGroupId,
                    s.SynonymText,
                    GroupSubdId = g.SubDistributorId,
                    SynonymSubdId = s.SubDistributorId
                })
            .ToListAsync(ct);

        var exclusions = await context.UomSynonymExclusions.AsNoTracking()
            .Where(x => subdIds.Contains(x.SubDistributorId))
            .Select(x => new { x.SubDistributorId, x.UomSynonymGroupId, x.UomSynonymId })
            .ToListAsync(ct);

        var result = new Dictionary<int, Dictionary<string, HashSet<int>>>();

        foreach (var subdId in subdIds)
        {
            var map = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

            foreach (var s in synonyms)
            {
                if (s.GroupSubdId is int gs && gs != subdId) continue;          // another subd's group
                if (s.SynonymSubdId is int ss && ss != subdId) continue;        // another subd's spelling

                var excluded = exclusions.Any(x => x.SubDistributorId == subdId
                                                   && x.UomSynonymGroupId == s.UomSynonymGroupId
                                                   && (x.UomSynonymId == null || x.UomSynonymId == s.UomSynonymId));
                if (excluded) continue;

                var key = Key(s.SynonymText);
                if (key.Length == 0) continue;

                if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<int>();
                set.Add(s.UomSynonymGroupId);
            }

            result[subdId] = map;
        }

        return new UomNameMatcher(result);
    }
}