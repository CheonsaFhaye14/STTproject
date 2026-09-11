using STTproject.Models;

namespace STTproject.Features.User.MapItem.Validators;

public static class AddUomValidator
{
    public static Dictionary<string, string> ValidateUomEntry(
        string uomName,
        string conversionInput,
        int? pcConversion,
        string priceInput,
        Dictionary<string, UomEntry> existingEntries,
        string baseUomName)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(uomName))
        {
            errors["uom"] = "Unit of measure is required.";
        }

        if (!string.IsNullOrWhiteSpace(conversionInput))
        {
            if (!int.TryParse(conversionInput, out var rawConversion) || rawConversion <= 0)
            {
                errors["conversion"] = "Conversion must be a positive integer.";
            }
            else if (pcConversion.HasValue &&
                    existingEntries.Values.Any(entry => entry.IsActive && entry.Conversion == pcConversion))
            {
                errors["conversion"] = "Conversion value must be unique.";
            }
            else if (IsBaseUnit(uomName, baseUomName) && pcConversion != 1)
            {
                errors["conversion"] = $"Unit '{baseUomName}' must have conversion 1.";
            }
        }

        if (!string.IsNullOrWhiteSpace(priceInput))
        {
            if (!decimal.TryParse(priceInput, out var price) || price < 0)
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

    public static Dictionary<string, string> ValidateFinalUomEntries(
        Dictionary<string, UomEntry> entries,
        string baseUomName)
    {
        var errors = new Dictionary<string, string>();

        if (!entries.Any(x => x.Value.Price.HasValue))
        {
            errors["prices"] = "At least one unit of measure must have a price set for calculation.";
        }

        // Guard against duplicate active names (defense-in-depth: rename should already prevent this).
        var duplicateNameGroups = entries
            .Where(kv => kv.Value.IsActive)
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateNameGroups)
        {
            foreach (var kv in group)
            {
                errors[$"uomname_{kv.Key}"] = $"'{kv.Key}' is already in use.";
            }
        }

        // Guard against duplicate active conversions.
        var duplicateConversionGroups = entries
            .Where(kv => kv.Value.IsActive && kv.Value.Conversion.HasValue)
            .GroupBy(kv => kv.Value.Conversion!.Value)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateConversionGroups)
        {
            foreach (var kv in group)
            {
                errors[$"conversion_{kv.Key}"] = "Conversion value must be unique.";
            }
        }

        foreach (var kv in entries)
        {
            if (!kv.Value.IsActive) continue;

        }

        if (entries.TryGetValue(baseUomName, out var baseUnitEntry) &&
            (!baseUnitEntry.Price.HasValue || baseUnitEntry.Price <= 0))
        {
            errors[$"price_{baseUomName}"] = "Base unit price must be provided or derivable from another priced unit.";
        }

        foreach (var kv in entries)
        {
            if (IsBaseUnit(kv.Key, baseUomName) && kv.Value.Conversion.HasValue && kv.Value.Conversion.Value != 1)
            {
                errors["conversion_base"] = $"Unit '{kv.Key}' must have conversion 1.";
                break;
            }
        }

        return errors;
    }
    
    private static bool IsBaseUnit(string? uom, string baseUomName)
    {
        return string.Equals((uom ?? string.Empty).Trim(), baseUomName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string> ValidateUomRename( string oldName, string newName, Dictionary<string, UomEntry> existingEntries, string baseUomName)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(newName))
        {
            errors[$"uomname_{oldName}"] = "Unit of measure name is required.";
            return errors;
        }

        if (!string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase) &&
            existingEntries.TryGetValue(newName, out var conflictingEntry) &&
            conflictingEntry.IsActive)
        {
            errors[$"uomname_{oldName}"] = $"'{newName}' is already in use.";
        }

        return errors;
    }

    public static Dictionary<string, string> ValidateUomConversion(
        string uomName,
        int? conversion,
        Dictionary<string, UomEntry> existingEntries,
        string baseUomName)
    {
        var errors = new Dictionary<string, string>();

        if (!conversion.HasValue)
        {
            return errors;
        }

        if (conversion.Value <= 0)
        {
            errors[$"conversion_{uomName}"] = "Conversion must be a positive integer.";
            return errors;
        }

        var isDuplicate = existingEntries.Any(kv =>
            kv.Value.IsActive &&
            !string.Equals(kv.Key, uomName, StringComparison.OrdinalIgnoreCase) &&
            kv.Value.Conversion == conversion);

        if (isDuplicate)
        {
            errors[$"conversion_{uomName}"] = "Conversion value must be unique.";
            return errors;
        }

        if (IsBaseUnit(uomName, baseUomName) && conversion.Value != 1)
        {
            errors[$"conversion_{uomName}"] = $"Unit '{baseUomName}' must have conversion 1.";
        }

        return errors;
    }
}