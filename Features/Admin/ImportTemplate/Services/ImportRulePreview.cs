using System.Globalization;
using System.Text.RegularExpressions;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>
/// Shows the admin what a "how to read it" choice does to a sample value while they build a template.
///
/// PREVIEW ONLY. The real import must apply the rules the same way. When you write the importer,
/// have it call this class (or move the rules somewhere both can share) so the preview can never
/// disagree with what actually gets imported.
/// </summary>
public static class ImportRulePreview
{
    public static string Apply(string sample, string ruleType, string? pattern)
    {
        try
        {
            switch (ruleType)
            {
                case "Trim":
                    return sample.Trim();

                case "StripLeadingCode":
                    // "00123 - ABC STORE" -> "ABC STORE"
                    return Regex.Replace(sample, @"^\s*\w+\s*[-–:|]\s*", "",
                        RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();

                case "StripParenthetical":
                    // "Coca Cola (1L)" -> "Coca Cola"
                    return Regex.Replace(sample, @"\s*\([^)]*\)", "",
                        RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();

                case "ExtractBracketed":
                    // "Coca Cola [ITEM001]" -> "ITEM001"
                    var b = Regex.Match(sample, @"\[([^\]]*)\]", RegexOptions.None, TimeSpan.FromSeconds(1));
                    return b.Success ? b.Groups[1].Value.Trim() : "(no [brackets] found)";

                case "Regex":
                    if (string.IsNullOrWhiteSpace(pattern)) return "(enter a pattern)";
                    var m = Regex.Match(sample, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
                    if (!m.Success) return "(no match)";
                    return (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value).Trim();

                default:
                    return sample;
            }
        }
        catch (ArgumentException)
        {
            return "(pattern isn't valid)";
        }
        catch (RegexMatchTimeoutException)
        {
            return "(pattern is too slow)";
        }
    }

    /// <summary>
    /// Previews a date. <paramref name="format"/> is a .NET format such as "dd/MM/yyyy";
    /// empty means "work it out automatically".
    /// </summary>
    public static string ApplyDate(string sample, string? format)
    {
        var text = sample.Trim();
        DateTime date;

        if (string.IsNullOrWhiteSpace(format))
        {
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return "(can't read this as a date)";

            // 01/05/2026 could be 5 January or 1 May. Say so instead of quietly guessing.
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
