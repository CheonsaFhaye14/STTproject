using System.Globalization;
using System.Text.RegularExpressions;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public static class ImportRulePreview
{
    public static string Apply(string sample, string ruleType, string? pattern) =>
        ImportRules.TryApply(sample, ruleType, pattern, out var result, out var error)
            ? result
            : $"({error})";

    /// <summary>
    /// <paramref name="options"/> is the column's OptionsJson: a plain format string (legacy) or
    /// JSON with an optional regex "pattern" and "format" (several formats separated by |).
    /// </summary>
    public static string ApplyDate(string sample, string? options)
    {
        var opts = DateOptions.Parse(options);

        if (!opts.TryRead(sample, out var date, out var extracted, out var error))
            return $"({error})";

        // Ambiguity warning only when guessing (no format given)
        if (opts.Formats.Length == 0)
        {
            var m = Regex.Match(extracted, @"^(\d{1,2})[/.\-](\d{1,2})[/.\-]\d{2,4}$");
            if (m.Success
                && int.Parse(m.Groups[1].Value) <= 12
                && int.Parse(m.Groups[2].Value) <= 12
                && m.Groups[1].Value != m.Groups[2].Value)
                return $"{Show(date)} - day and month could be swapped, pick a format";
        }

        return Show(date);
    }

    private static string Show(DateTime d) =>
        $"{d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)})";
}