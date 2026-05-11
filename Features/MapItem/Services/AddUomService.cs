using STTproject.Models;

namespace STTproject.Features.MapItem.Services;

public class AddUomService
{
    public void RecalculatePrices(Dictionary<string, UomEntry> entries, string? sourceUom = null)
    {
        var sourceKey = sourceUom;
        UomEntry? sourceEntry = null;

        if (!string.IsNullOrWhiteSpace(sourceUom) && entries.TryGetValue(sourceUom, out var specifiedEntry) &&
            specifiedEntry.Price.HasValue)
        {
            sourceEntry = new UomEntry
            {
                Conversion = specifiedEntry.Conversion,
                Price = specifiedEntry.Price.Value
            };
        }

        if (sourceEntry == null)
        {
            var firstEntry = entries.FirstOrDefault(x => x.Value.Price.HasValue);
            if (firstEntry.Value == null)
            {
                return;
            }

            sourceEntry = new UomEntry
            {
                Conversion = firstEntry.Value.Conversion,
                Price = firstEntry.Value.Price!.Value
            };
            sourceKey = firstEntry.Key;
        }

        var sourcePrice = sourceEntry.Price;
        var sourceConversion = sourceEntry.Conversion;

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(sourceKey) && entry.Key.Equals(sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Value.Price.HasValue && !entry.Value.IsAutoCalculated)
            {
                continue;
            }

            entry.Value.Price = (sourcePrice / sourceConversion) * entry.Value.Conversion;
            entry.Value.IsAutoCalculated = true;
        }
    }
}