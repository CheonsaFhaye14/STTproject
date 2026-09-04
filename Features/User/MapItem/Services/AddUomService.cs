using STTproject.Models;

namespace STTproject.Features.User.MapItem.Services;

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

        if (!sourceConversion.HasValue || sourceConversion.Value == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(sourceKey) && entry.Key.Equals(sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // No conversion set on this UOM — nothing to derive its price from.
            // If it was previously auto-calculated (conversion has since been cleared),
            // drop the stale calculated price rather than leaving it looking valid.
            if (!entry.Value.Conversion.HasValue)
            {
                if (entry.Value.IsAutoCalculated)
                {
                    entry.Value.Price = null;
                }
                continue;
            }

            if (entry.Value.Price.HasValue && !entry.Value.IsAutoCalculated)
            {
                continue;
            }

            entry.Value.Price = (sourcePrice / sourceConversion.Value) * entry.Value.Conversion.Value;
            entry.Value.IsAutoCalculated = true;
        }
    }
}