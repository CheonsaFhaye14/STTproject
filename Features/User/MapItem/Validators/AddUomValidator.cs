using STTproject.Models;

namespace STTproject.Features.User.MapItem.Validators;

public static class AddUomValidator
{
    public static Dictionary<string, string> ValidateUomEntry(
        string uomName,
        string conversionInput,
        decimal? pcConversion,
        string priceInput,
        Dictionary<string, UomEntry> existingEntries)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(uomName))
        {
            errors["uom"] = "Unit of measure is required.";
        }

        if (!string.IsNullOrWhiteSpace(conversionInput))
        {
            if (!decimal.TryParse(conversionInput, out var rawConversion) || rawConversion <= 0)
            {
                errors["conversion"] = "Conversion must be a positive number.";
            }
            else if (pcConversion.HasValue &&
                    existingEntries.Values.Any(entry => entry.IsActive && entry.Conversion == pcConversion))
            {
                errors["conversion"] = "Conversion value must be unique.";
            }
            else if (IsPieceUom(uomName) && pcConversion != 1)
            {
                errors["conversion"] = "Unit 'PC/PCS/PIECE' must have conversion 1.";
            }
        }

        if (!string.IsNullOrWhiteSpace(priceInput))
        {
            if (!decimal.TryParse(priceInput, out var price) || price <= 0)
            {
                errors["price"] = "Price must be greater than zero.";
            }
        }

        if (existingEntries.TryGetValue(uomName, out var existingEntry) && existingEntry.IsActive)
        {
            errors["uom"] = $"'{uomName}' already exists.";
        }

        return errors;
    }

    public static Dictionary<string, string> ValidateFinalUomEntries(Dictionary<string, UomEntry> entries)
    {
        var errors = new Dictionary<string, string>();

        if (!entries.Any(x => x.Value.Price.HasValue))
        {
            errors["prices"] = "At least one unit of measure must have a price set for calculation.";
        }

        foreach (var kv in entries)
        {
            if (!kv.Value.IsActive) continue;

            if (!kv.Value.Price.HasValue)
            {
                errors[$"price_{kv.Key}"] = $"'{kv.Key}' needs a price — either enter one directly or set a conversion so it can be calculated.";
            }
            else if (kv.Value.Price <= 0)
            {
                errors[$"price_{kv.Key}"] = "Price must be greater than zero.";
            }
        }

        if (entries.TryGetValue("PC", out var baseUnitEntry) &&
            (!baseUnitEntry.Price.HasValue || baseUnitEntry.Price <= 0))
        {
            errors["price_PC"] = "Base unit price must be provided or derivable from another priced unit.";
        }

        foreach (var kv in entries)
        {
            if (IsPieceUom(kv.Key) && kv.Value.Conversion.HasValue && kv.Value.Conversion.Value != 1)
            {
                errors["conversion_piece"] = $"Unit '{kv.Key}' must have conversion 1.";
                break;
            }
        }

        return errors;
    }

    private static bool IsPieceUom(string? uom)
    {
        var normalized = (uom ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "piece" or "pcs" or "pc";
    }
}