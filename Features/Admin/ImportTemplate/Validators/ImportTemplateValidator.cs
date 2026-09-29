using System.Text.Json;
using System.Text.RegularExpressions;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;

namespace STTproject.Features.Admin.ImportTemplate.Validators;

public static class ImportTemplateValidator
{
    private const int MaxHeaderRow = 100;

    public static List<string> Validate(ImportTemplateEditDto t)
    {
        var errors = new List<string>();
        var def = ImportFieldRegistry.Get(t.ImportType);

        if (def is null)
            errors.Add("Please choose a valid import type.");
        else
        {
            if (def.Scope == ImportScope.Principal && t.SubDistributorId is not null)
                errors.Add("Company Item templates are set by principal, not by subdistributor.");

            if (def.Scope == ImportScope.Subd && !string.IsNullOrWhiteSpace(t.Principal))
                errors.Add($"{def.Display} templates are set by subdistributor, not by principal.");
        }

        if (!string.IsNullOrWhiteSpace(t.Principal) && t.Principal.Trim().Length > 100)
            errors.Add("Principal cannot be longer than 100 characters.");

        if (t.HeaderRowNumber is < 1 or > MaxHeaderRow)
            errors.Add($"Header row number must be between 1 and {MaxHeaderRow}, or left blank to auto-detect.");

        if (t.Columns.Count == 0)
            errors.Add("Add at least one column.");

        var seenHeaders = new Dictionary<string, int>();

        for (int i = 0; i < t.Columns.Count; i++)
        {
            var c = t.Columns[i];
            var label = $"Row {i + 1}";

            if (string.IsNullOrWhiteSpace(c.HeaderText))
            {
                errors.Add($"{label}: header text is required.");
            }
            else
            {
                if (c.HeaderText.Trim().Length > 200)
                    errors.Add($"{label}: header text cannot be longer than 200 characters.");

                var norm = NormalizeHeader(c.HeaderText);
                if (seenHeaders.TryGetValue(norm, out var firstRow))
                    errors.Add($"{label}: header \"{c.HeaderText.Trim()}\" duplicates row {firstRow}.");
                else
                    seenHeaders[norm] = i + 1;
            }

            if (!c.IsIgnored)
            {
                if (string.IsNullOrWhiteSpace(c.FieldKey))
                {
                    errors.Add($"{label}: choose a field, or tick Ignored.");
                }
                else if (def is not null)
                {
                    if (!ImportFieldRegistry.IsValidField(t.ImportType, c.FieldKey))
                        errors.Add($"{label}: field \"{c.FieldKey}\" is not valid for {def.Display}.");
                    else if (!ImportFieldRegistry.IsValidReadMode(t.ImportType, c.FieldKey, c.ReadMode))
                        errors.Add($"{label}: read mode \"{c.ReadMode}\" is not allowed for that field.");
                }
            }

            ValidateOptions(c, label, errors);
        }

        // A subd template that falls back to the global default can be partial, because the
        // default fills the gaps. Only complete templates must cover every required field.
        var isGlobal = t.SubDistributorId is null && string.IsNullOrWhiteSpace(t.Principal);
        var mustBeComplete = isGlobal || !t.AllowGlobalFallback;
        if (mustBeComplete && def is not null)
        {
            var mapped = t.Columns
                .Where(c => !c.IsIgnored && !string.IsNullOrWhiteSpace(c.FieldKey))
                .Select(c => c.FieldKey!)
                .ToList();
            errors.AddRange(ImportFieldRegistry.MissingRequired(t.ImportType, mapped));

            if (t.ImportType == "SalesInvoice" && mapped.Contains("Quantity") &&
                !mapped.Any(k => k is "CaseQuantity" or "DozenQuantity" or "PieceQuantity" or "InBoxQuantity") &&
                !mapped.Contains("UnitOfMeasure"))
                errors.Add("Unit of Measure is required when using Quantity.");
        }

        return errors;
    }

    private static void ValidateOptions(ImportTemplateColumnEditDto c, string label, List<string> errors)
    {
        var hasOptions = !string.IsNullOrWhiteSpace(c.OptionsJson);
        var isRegex = c.ReadMode == "Regex" && !c.IsIgnored;

        if (isRegex && !hasOptions)
        {
            errors.Add($"{label}: Regex read mode needs options like {{\"pattern\":\"...\"}}.");
            return;
        }
        if (!hasOptions) return;

        try
        {
            using var doc = JsonDocument.Parse(c.OptionsJson!);
            if (isRegex)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("pattern", out var p) ||
                    p.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(p.GetString()))
                {
                    errors.Add($"{label}: Regex options must include a \"pattern\".");
                    return;
                }

                _ = new Regex(p.GetString()!, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
        }
        catch (JsonException)
        {
            errors.Add($"{label}: options are not valid JSON.");
        }
        catch (ArgumentException)
        {
            errors.Add($"{label}: the regex pattern is not valid.");
        }
    }

    // Same normalization the importers use, so duplicate detection matches real header matching.
    public static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s\.\#\/\-\,\:\(\)]+", " ").Trim();
    }
}