using STTproject.Models;

namespace STTproject.Features.User.MapItem.Services;

public sealed record StockUomSuggestion(string UomName, string Reason);

public static class StockUomSuggester
{
    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    private static Dictionary<string, string> BuildAliases()
    {
        var groups = new[]
        {
            new[] { "CASE", "CASES", "CS", "CSE" },
            new[] { "BOX", "BOXES", "BX" },
            new[] { "INBOX", "INBOXES", "IBX" },
            new[] { "PIECE", "PIECES", "PC", "PCS", "EA", "EACH" },
            new[] { "PACK", "PACKS", "PK", "PCK" },
            new[] { "BUNDLE", "BUNDLES", "BDL" },
            new[] { "CARTON", "CARTONS", "CTN" },
            new[] { "BOTTLE", "BOTTLES", "BTL" },
            new[] { "DOZEN", "DZ", "DOZ" },
            new[] { "PACKAGE", "PACKAGES", "PKG" },
        };

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var alias in g)
                map[alias] = g[0];
        return map;
    }

    public static string Canon(string? name)
    {
        var key = new string((name ?? string.Empty).Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return Aliases.TryGetValue(key, out var canon) ? canon : key;
    }

    public static StockUomSuggestion? Suggest(string? companyStockUom, IReadOnlyDictionary<string, UomEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(companyStockUom)) return null;
        var target = Canon(companyStockUom);
        if (target.Length == 0) return null;

        var matches = entries
            .Where(kv => kv.Value.IsActive
                         && kv.Value.Conversion is > 0
                         && Canon(kv.Key) == target)
            .ToList();

        return matches.Count == 1
            ? new StockUomSuggestion(matches[0].Key, $"Name matches the company stock UOM ({companyStockUom}).")
            : null;
    }

    public static bool PriceMatches(decimal? applicablePrice, UomEntry entry) =>
        applicablePrice.HasValue && entry.Price.HasValue && entry.Price.Value == applicablePrice.Value;
}