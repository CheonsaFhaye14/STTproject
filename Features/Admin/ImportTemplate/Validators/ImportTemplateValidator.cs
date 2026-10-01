using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services; 
namespace STTproject.Features.Admin.ImportTemplate.Validators;

public static class ImportTemplateValidator
{
    public static List<string> Validate(ImportTemplateEditDto dto)
    {
        var errors = new List<string>();

        if (dto.Sheets.Count == 0)
        {
            errors.Add("Add at least one sheet.");
            return errors;
        }

        var active = dto.Sheets.Where(s => s.SheetMatchMode != SheetMatchModes.Ignore).ToList();
        if (active.Count == 0)
            errors.Add("At least one sheet must be read (not Ignore).");

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in dto.Sheets)
        {
            var who = string.IsNullOrWhiteSpace(s.SheetLabel) ? "A sheet" : $"Sheet \"{s.SheetLabel}\"";

            if (string.IsNullOrWhiteSpace(s.SheetLabel))
                errors.Add("Every sheet needs a label.");
            else if (!labels.Add(s.SheetLabel))
                errors.Add($"Sheet label \"{s.SheetLabel}\" is used more than once.");

            if (!SheetMatchModes.All.Contains(s.SheetMatchMode))
            {
                errors.Add($"{who}: unknown sheet matching mode.");
                continue;
            }

            switch (s.SheetMatchMode)
            {
                case SheetMatchModes.Exact or SheetMatchModes.Contains:
                    if (string.IsNullOrWhiteSpace(s.SheetMatchValue))
                        errors.Add($"{who}: enter the text to match.");
                    break;
                case SheetMatchModes.Position:
                    if (!int.TryParse(s.SheetMatchValue, out var pos) || pos < 1)
                        errors.Add($"{who}: sheet position must be a number of 1 or more.");
                    break;
                case SheetMatchModes.Any when dto.Sheets.Count > 1:
                    errors.Add($"{who}: \"Any sheet\" can only be used when the template has a single sheet.");
                    break;
            }

            if (!HeaderRowModes.All.Contains(s.HeaderRowMode))
                errors.Add($"{who}: unknown header row mode.");
            else if (s.HeaderRowMode == HeaderRowModes.Fixed && (s.HeaderRowNumber is null or < 1))
                errors.Add($"{who}: enter the header row number.");

            if (s.HeaderRowCount is < 1 or > 5)
                errors.Add($"{who}: header rows must be between 1 and 5.");

            if (s.SheetMatchMode == SheetMatchModes.Ignore)
            {
                if (s.Columns.Count > 0)
                    errors.Add($"{who} is set to Ignore, so it can't have columns.");
                continue;
            }

            ValidateColumns(s, who, dto.ImportType, errors);
        }

        return errors;
    }

    private static void ValidateColumns(ImportTemplateSheetEditDto sheet, string who, string importType, List<string> errors)
    {
        if (sheet.Columns.Count == 0)
        {
            errors.Add($"{who}: add at least one column.");
            return;
        }

        foreach (var c in sheet.Columns)
        {
            var col = string.IsNullOrWhiteSpace(c.HeaderText) ? "A column" : $"Column \"{c.HeaderText}\"";

            if (string.IsNullOrWhiteSpace(c.HeaderText))
            {
                errors.Add($"{who}: every column needs a header text.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(c.FieldKey))
                errors.Add($"{who}, {col}: choose a system field.");

            if (!ColumnRuleTypes.All.Contains(c.RuleType))
                errors.Add($"{who}, {col}: unknown rule.");

            if (c.RuleType == ColumnRuleTypes.Regex && string.IsNullOrWhiteSpace(c.OptionsJson))
                errors.Add($"{who}, {col}: the Regex rule needs a pattern.");

            if (c.RuleType == ColumnRuleTypes.ValueMap)
            {
                var field = ImportFieldRegistry.GetField(importType, c.FieldKey);
                var entries = ValueMapOptions.Parse(c.OptionsJson)
                    .Where(e => e.From == "*" || !string.IsNullOrWhiteSpace(e.From) || !string.IsNullOrWhiteSpace(e.To))
                    .ToList();

                if (field?.Choices is null)
                    errors.Add($"{who}, {col}: this field doesn't use a value table.");
                else
                {
                    if (entries.Count == 0)
                        errors.Add($"{who}, {col}: add at least one value to the table.");

                    foreach (var e in entries)
                    {
                        var name = e.From == "*" ? "anything else" : e.From;
                        if (e.From != "*" && string.IsNullOrWhiteSpace(e.From))
                            errors.Add($"{who}, {col}: a row in the table has no text from the file.");
                        else if (!field.Choices.Contains(e.To, StringComparer.OrdinalIgnoreCase))
                            errors.Add($"{who}, {col}: choose what \"{name}\" means.");
                    }
                }
            }
        }
    }
}