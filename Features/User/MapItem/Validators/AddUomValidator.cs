using System.Globalization;
using STTproject.Models;

namespace STTproject.Features.User.MapItem.Validators;

/// <summary>
/// Checks for the Add UOM window. Conversions are decimals relative to the company stock UOM (stock UOM = 1).
/// Prices are no longer entered, so there are no price rules.
/// </summary>
public static class AddUomValidator
{
    /// <param name="conversion">The conversion worked out from the input, in stock units (null when blank or invalid).</param>
    /// <param name="conversionRequired">True when the company item has a stock UOM (priced item).</param>
    public static Dictionary<string, string> ValidateUomEntry(
        string uomName,
        string conversionInput,
        decimal? conversion,
        Dictionary<string, UomEntry> existingEntries,
        bool conversionRequired)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(uomName))
            errors["uom"] = "Unit of measure is required.";
        else if (existingEntries.TryGetValue(uomName, out var existing) && existing.IsActive)
            errors["uom"] = $"'{uomName}' already exists.";

        if (string.IsNullOrWhiteSpace(conversionInput))
        {
            if (conversionRequired)
                errors["conversion"] = "Conversion is required: prices are worked out from it.";
        }
        else if (!decimal.TryParse(conversionInput, NumberStyles.Number, CultureInfo.InvariantCulture, out var raw) || raw <= 0)
        {
            errors["conversion"] = "Conversion must be a number greater than zero.";
        }
        else if (conversion is null)
        {
            errors["conversion"] = "Pick the unit the conversion is counted in.";
        }
        else if (existingEntries.Any(kv => kv.Value.IsActive
                                           && !string.Equals(kv.Key, uomName, StringComparison.OrdinalIgnoreCase)
                                           && kv.Value.Conversion == conversion))
        {
            errors["conversion"] = "Another unit already has this conversion.";
        }

        return errors;
    }

    public static Dictionary<string, string> ValidateFinalUomEntries(
        Dictionary<string, UomEntry> entries,
        bool conversionRequired)
    {
        var errors = new Dictionary<string, string>();

        var duplicateNameGroups = entries
            .Where(kv => kv.Value.IsActive)
            .GroupBy(kv => kv.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateNameGroups)
            foreach (var kv in group)
                errors[$"uomname_{kv.Key}"] = $"'{kv.Key}' is already in use.";

        var duplicateConversionGroups = entries
            .Where(kv => kv.Value.IsActive && kv.Value.Conversion.HasValue)
            .GroupBy(kv => kv.Value.Conversion!.Value)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateConversionGroups)
            foreach (var kv in group)
                errors[$"conversion_{kv.Key}"] = "Another unit has the same conversion.";

        if (conversionRequired)
        {
            foreach (var kv in entries.Where(kv => kv.Value.IsActive && kv.Value.Conversion is null))
                errors.TryAdd($"conversion_{kv.Key}", "Conversion is required: prices are worked out from it.");
        }

        return errors;
    }

    public static Dictionary<string, string> ValidateUomRename(string oldName, string newName, Dictionary<string, UomEntry> existingEntries)
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
        decimal? conversion,
        Dictionary<string, UomEntry> existingEntries,
        bool isStock)
    {
        var errors = new Dictionary<string, string>();

        if (!conversion.HasValue) return errors;

        if (conversion.Value <= 0)
        {
            errors[$"conversion_{uomName}"] = "Conversion must be greater than zero.";
            return errors;
        }

        if (isStock && conversion.Value != 1m)
        {
            errors[$"conversion_{uomName}"] = "The stock UOM must have conversion 1.";
            return errors;
        }

        var isDuplicate = existingEntries.Any(kv =>
            kv.Value.IsActive &&
            !string.Equals(kv.Key, uomName, StringComparison.OrdinalIgnoreCase) &&
            kv.Value.Conversion == conversion);

        if (isDuplicate)
            errors[$"conversion_{uomName}"] = "Another unit has the same conversion.";

        return errors;
    }
}