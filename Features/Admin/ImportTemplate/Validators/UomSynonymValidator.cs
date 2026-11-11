using STTproject.Features.Admin.ImportTemplate.DTOs;

namespace STTproject.Features.Admin.ImportTemplate.Validators;

public static class UomSynonymValidator
{
    /// <summary>Checks one already-normalized spelling. Null when it's fine.</summary>
    public static string? CheckSpelling(string spelling)
    {
        if (spelling.Length == 0)
            return "Enter a spelling.";
        if (spelling.Length > UomSynonymLimits.SpellingMax)
            return $"\"{spelling}\" is longer than {UomSynonymLimits.SpellingMax} characters.";
        if (spelling.IndexOfAny(new[] { '(', ')' }) >= 0)
            return $"\"{spelling}\" can't contain brackets. Anything in brackets at the end of a file's UOM is ignored before matching.";
        return null;
    }

    /// <summary>
    /// Validates every group of one scope together, inactive ones included: an inactive group still owns its spellings,
    /// because the database allows a spelling only once per scope.
    /// </summary>
    public static List<string> Validate(IReadOnlyList<UomSynonymGroupEditDto> groups)
    {
        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var name = g.GroupName?.Trim() ?? string.Empty;
            var who = name.Length == 0 ? "A group" : $"Group \"{name}\"";

            if (name.Length == 0)
                errors.Add("Every group needs a name.");
            else if (name.Length > UomSynonymLimits.GroupNameMax)
                errors.Add($"{who}: the name is longer than {UomSynonymLimits.GroupNameMax} characters.");
            else if (!names.Add(name))
                errors.Add($"Group name \"{name}\" is used more than once.");

            if (g.Spellings.Count == 0)
                errors.Add($"{who}: add at least one spelling.");

            var inGroup = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in g.Spellings)
            {
                var problem = CheckSpelling(s);
                if (problem is not null)
                {
                    errors.Add($"{who}: {problem}");
                    continue;
                }

                if (!inGroup.Add(s))
                {
                    errors.Add($"{who}: \"{s}\" is listed more than once.");
                    continue;
                }

                if (owner.TryGetValue(s, out var other))
                    errors.Add($"\"{s}\" is in both {other} and {(name.Length == 0 ? "an unnamed group" : name)}. A spelling can belong to only one group.");
                else
                    owner[s] = name.Length == 0 ? "an unnamed group" : name;
            }
        }

        return errors;
    }
}