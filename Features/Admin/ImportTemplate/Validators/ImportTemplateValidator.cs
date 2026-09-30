using STTproject.Features.Admin.ImportTemplate.DTOs;

namespace STTproject.Features.Admin.ImportTemplate.Validators;

public static class ImportTemplateValidator
{
    public static List<string> Validate(ImportTemplateEditDto dto)
    {
        var errors = new List<string>();

        // TODO: keep your existing checks here (ImportType exists in ImportFieldRegistry,
        // scope rules for Subd vs Principal, required registry fields are mapped).

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

            if (s.SheetMatchMode == SheetMatchModes.Ignore)
            {
                if (s.Columns.Count > 0)
                    errors.Add($"{who} is set to Ignore, so it can't have columns.");
                continue;
            }

            ValidateColumns(s, who, errors);
        }

        return errors;
    }

    private static void ValidateColumns(ImportTemplateSheetEditDto sheet, string who, List<string> errors)
    {
        if (sheet.Columns.Count == 0)
        {
            errors.Add($"{who}: add at least one column.");
            return;
        }

        var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in sheet.Columns)
        {
            var col = string.IsNullOrWhiteSpace(c.HeaderText) ? "A column" : $"Column \"{c.HeaderText}\"";

            if (string.IsNullOrWhiteSpace(c.HeaderText))
            {
                errors.Add($"{who}: every column needs a header text.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(c.FieldKey))
                errors.Add($"{who}, {col}: choose a system field or mark it ignored.");

            if (!ColumnRuleTypes.All.Contains(c.RuleType))
                errors.Add($"{who}, {col}: unknown rule.");

            if (c.RuleType == ColumnRuleTypes.Regex && string.IsNullOrWhiteSpace(c.OptionsJson))
                errors.Add($"{who}, {col}: the Regex rule needs a pattern.");

        }

        var sharedFields = sheet.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey))
            .GroupBy(c => c.FieldKey!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

    }
}