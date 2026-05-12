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
        else if (existingEntries.Values.Any(entry => entry.Conversion == conversion))
        {
            errors["conversion"] = "Conversion value must be unique.";
        }

        if (!string.IsNullOrWhiteSpace(priceInput))
        {
            if (!decimal.TryParse(priceInput, out var price) || price <= 0)
            {
                errors["price"] = "Price must be greater than zero.";
            }
        }

        if (existingEntries.ContainsKey(uomName))
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

        if (entries.Any(x => x.Value.Price.HasValue && x.Value.Price <= 0))
        {
            errors["prices"] = "All prices must be greater than zero.";
        }

        if (entries.TryGetValue("Piece", out var pieceEntry) &&
            (!pieceEntry.Price.HasValue || pieceEntry.Price <= 0))
        {
            errors["prices"] = "Base unit price must be provided or derivable from another priced unit.";
        }

        return errors;
    }
}