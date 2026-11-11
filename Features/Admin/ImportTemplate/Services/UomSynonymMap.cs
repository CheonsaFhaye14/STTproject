using System.Text.RegularExpressions;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>
/// Which spellings mean the same unit, for one import. Built once per import from the admin's
/// UOM synonym groups and passed down. There is no built-in list: a unit no group knows is tried as itself.
/// </summary>
public sealed class UomSynonymMap
{
    private static readonly Regex TrailingParen = new(@"\s*\(.*?\)\s*$", RegexOptions.Compiled);

    private readonly Dictionary<string, IReadOnlyList<string>> _groupBySpelling;

    private UomSynonymMap(Dictionary<string, IReadOnlyList<string>> groupBySpelling) => _groupBySpelling = groupBySpelling;

    /// <summary>No synonyms at all: every unit is tried exactly as written.</summary>
    public static readonly UomSynonymMap Empty = new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>
    /// The subdistributor's own groups go first and claim their spellings. A global group then keeps only the
    /// spellings nobody claimed, so for this subdistributor "bx" can mean its own Box instead of the global Inner box.
    /// Pass only active groups, each in the order its spellings should be tried.
    /// </summary>
    public static UomSynonymMap Build(IEnumerable<IEnumerable<string>> ownGroups, IEnumerable<IEnumerable<string>> globalGroups)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        void Add(IEnumerable<string> group)
        {
            var spellings = group
                .Select(Norm)
                .Where(s => s.Length > 0 && !map.ContainsKey(s))
                .Distinct()
                .ToList();
            foreach (var s in spellings) map[s] = spellings;
        }

        foreach (var g in ownGroups) Add(g);
        foreach (var g in globalGroups) Add(g);

        return new UomSynonymMap(map);
    }

    /// <summary>
    /// Every spelling to try for this unit, the group's first spelling first. A trailing "(…)" is ignored.
    /// A unit no group knows comes back as itself.
    /// </summary>
    public IEnumerable<string> Expand(string? value)
    {
        var stripped = TrailingParen.Replace(value ?? string.Empty, string.Empty).Trim();
        return _groupBySpelling.TryGetValue(Norm(stripped), out var group) ? group : new[] { stripped };
    }

    public static string Norm(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}