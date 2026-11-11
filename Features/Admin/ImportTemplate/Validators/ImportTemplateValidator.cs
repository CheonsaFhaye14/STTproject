using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Services;
namespace STTproject.Features.Admin.ImportTemplate.Validators;

public static class ImportTemplateValidator
{

    public static List<string> ValidateScope(string importType, int? subDistributorId, string? principal)
    {
        var def = ImportFieldRegistry.Get(importType);
        if (def is null) return new() { $"Unknown import type '{importType}'." };

        var hasPrincipal = !string.IsNullOrWhiteSpace(principal);

        if (subDistributorId is not null && hasPrincipal)
            return new() { "A template applies to a subdistributor or a principal, not both." };
        if (def.Scope == ImportScope.Principal && subDistributorId is not null)
            return new() { $"{def.Display} templates are set by principal, not by subdistributor." };
        if (def.Scope == ImportScope.Subd && hasPrincipal)
            return new() { $"{def.Display} templates are set by subdistributor, not by principal." };

        return new();
    }

    public static List<string> MissingRequiredGroups(ImportTemplateSheetEditDto sheet, string importType)
    {
        if (sheet.SheetMatchMode == SheetMatchModes.Ignore) return new();

        var mapped = sheet.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey))
            .Select(c => c.FieldKey!);

        return ImportFieldRegistry.MissingRequired(importType, mapped);
    }

    public static List<string> Validate(ImportTemplateEditDto dto)
    {
        var errors = ValidateStructure(dto);
        if (dto.Sheets.Count > 0) errors.AddRange(RequiredGroupErrors(dto));
        return errors;
    }

    public static List<string> RequiredGroupErrors(ImportTemplateEditDto dto)
    {
        var read = dto.Sheets.Where(s => s.SheetMatchMode != SheetMatchModes.Ignore).ToList();
        var several = read.Count > 1;
        var errors = new List<string>();

        foreach (var s in read)
        {
            var who = string.IsNullOrWhiteSpace(s.SheetLabel) ? "A sheet" : $"Sheet \"{s.SheetLabel}\"";
            foreach (var m in MissingRequiredGroups(s, dto.ImportType))
                errors.Add(several ? $"{who}: {m}" : m);
        }

        if (several && errors.Count > 0)
            errors.Add("Each sheet is read on its own, so every sheet that isn't ignored must map all required fields. " +
                       "Combining fields across sheets isn't supported yet. Set a sheet to Ignore if it shouldn't be read.");
        return errors;
    }

    public static List<string> ValidateStructure(ImportTemplateEditDto dto)
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

            if (s.SheetMatchMode == SheetMatchModes.Headers)
            {
                var distinctFields = s.Columns
                    .Where(c => !string.IsNullOrWhiteSpace(c.FieldKey) && TemplateSheetResolver.HeaderTexts(c).Any())
                    .Select(c => c.FieldKey!.Trim().ToLowerInvariant())
                    .Distinct()
                    .Count();

                if (distinctFields < TemplateSheetResolver.MinHeaderFields)
                    errors.Add($"{who}: finding a sheet by its headers needs columns for at least " +
                               $"{TemplateSheetResolver.MinHeaderFields} different fields, so it can tell sheets apart.");
            }
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

        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Normalized header (primary or alias, any column) -> the column and field that own it.
        var headerOwner = new Dictionary<string, (ImportTemplateColumnEditDto Column, string? Field)>(StringComparer.OrdinalIgnoreCase);

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
            else if (!seenFields.Add(c.FieldKey))
            {
                var fieldName = ImportFieldRegistry.GetField(importType, c.FieldKey)?.Display ?? c.FieldKey;
                errors.Add($"{who}, {col}: {fieldName} is already mapped by another column on this sheet. " +
                           "Add this header as an alias of that column instead.");
            }

            // Many headers (primary + aliases) may feed one field, but one header feeds only one field, and
            // each header text appears once on the sheet. Checked together, normalized the way the file is matched.
            foreach (var text in TemplateSheetResolver.HeaderTexts(c))
            {
                var norm = TemplateSheetResolver.Norm(text);
                if (norm.Length == 0) continue;

                if (!headerOwner.TryGetValue(norm, out var owner))
                {
                    headerOwner[norm] = (c, c.FieldKey);
                    continue;
                }

                var otherField = !string.IsNullOrWhiteSpace(owner.Field)
                    && !string.IsNullOrWhiteSpace(c.FieldKey)
                    && !string.Equals(owner.Field, c.FieldKey, StringComparison.OrdinalIgnoreCase);

                if (otherField)
                {
                    var ownerName = ImportFieldRegistry.GetField(importType, owner.Field)?.Display ?? owner.Field;
                    errors.Add($"{who}, {col}: \"{text}\" is already used for {ownerName}. A header can feed only one field.");
                }
                else
                    errors.Add($"{who}, {col}: \"{text}\" is listed more than once for the same field, so its rule would be ambiguous.");
            }

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

            if (ImportFieldRegistry.GetField(importType, c.FieldKey)?.Type == ImportFieldType.Date
                && !string.IsNullOrWhiteSpace(c.OptionsJson))
            {
                var dateProblem = DateOptions.Parse(c.OptionsJson).Validate();
                if (dateProblem is not null)
                    errors.Add($"{who}, {col}: {dateProblem}");
            }
        }
    }
}