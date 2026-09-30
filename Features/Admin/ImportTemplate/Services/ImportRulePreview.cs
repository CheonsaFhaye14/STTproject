using System.Globalization;
using System.Text.RegularExpressions;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public static class ImportRulePreview
{
    public static string Apply(string sample, string ruleType, string? pattern) =>
        ImportRules.TryApply(sample, ruleType, pattern, out var result, out var error)
            ? result
            : $"({error})";

    public static string ApplyDate(string sample, string? format)
    {
        var text = sample.Trim();
        DateTime date;

        if (string.IsNullOrWhiteSpace(format))
        {
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return "(can't read this as a date)";

            var m = Regex.Match(text, @"^(\d{1,2})[/.\-](\d{1,2})[/.\-]\d{2,4}$");
            if (m.Success
                && int.Parse(m.Groups[1].Value) <= 12
                && int.Parse(m.Groups[2].Value) <= 12
                && m.Groups[1].Value != m.Groups[2].Value)
                return $"{Show(date)} - day and month could be swapped, pick a format";
        }
        else if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return $"(doesn't match {format})";
        }

        return Show(date);
    }

    private static string Show(DateTime d) =>
        $"{d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)})";
}
