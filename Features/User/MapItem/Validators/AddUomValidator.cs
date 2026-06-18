using STTproject.Models;

namespace STTproject.Features.User.MapItem.Validators;

public static class AddUomValidator
{
    public static Dictionary<string, string> ValidateUomEntry(
        string uomName,
        string conversionInput,
        string priceInput,
        Dictionary<string, UomEntry> existingEntries)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(uomName))
        {
            errors["uom"] = "Unit of measure is required.";
        }

        if (!int.TryParse(conversionInput, out var conversion) || conversion <= 0)
        {
            errors["conversion"] = "Conversion must be a positive integer.";
        }
        else if (existingEntries.Values.Any(entry => entry.IsActive && entry.Conversion == conversion))
        {
            errors["conversion"] = "Conversion value must be unique.";
        }

        if (int.TryParse(conversionInput, out var conv) && IsPieceUom(uomName) && conv != 1)
        {
            errors["conversion"] = "Unit 'PC/PCS/PIECE' must have conversion 1.";
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
        var baseUnitEntry = entries.TryGetValue("PC", out var pcEntry)
            ? pcEntry
            : entries.TryGetValue("Piece", out var legacyPieceEntry)
                ? legacyPieceEntry
                : null;

        if (!entries.Any(x => x.Value.Price.HasValue))
        {
            errors["prices"] = "At least one unit of measure must have a price set for calculation.";
        }

        if (entries.Any(x => x.Value.Price.HasValue && x.Value.Price <= 0))
        {
            errors["prices"] = "All prices must be greater than zero.";
        }

        if (baseUnitEntry is not null &&
            (!baseUnitEntry.Price.HasValue || baseUnitEntry.Price <= 0))
        {
            errors["prices"] = "Base unit price must be provided or derivable from another priced unit.";
        }

        // Ensure any piece-like UOM has conversion == 1
        foreach (var kv in entries)
        {
            if (IsPieceUom(kv.Key) && kv.Value.Conversion != 1)
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