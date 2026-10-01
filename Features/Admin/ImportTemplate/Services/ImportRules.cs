using System.Text.RegularExpressions;
using STTproject.Features.Admin.ImportTemplate.DTOs;
namespace STTproject.Features.Admin.ImportTemplate.Services;

public static class ImportRules
{
    public static bool TryApply(string value, string ruleType, string? pattern, out string result, out string? error)
    {
        result = value;
        error = null;
        try
        {
            switch (ruleType)
            {
                case "Trim":
                    result = value.Trim();
                    return true;
                case "StripLeadingCode":
                    result = Regex.Replace(value, @"^\s*\w+\s*[-–:|]\s*", "", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
                    return true;
                case "StripParenthetical":
                    result = Regex.Replace(value, @"\s*\([^)]*\)", "", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
                    return true;
                case "ExtractBracketed":
                    var b = Regex.Match(value, @"\[([^\]]*)\]", RegexOptions.None, TimeSpan.FromSeconds(1));
                    if (!b.Success) { error = "no [brackets] found"; return false; }
                    result = b.Groups[1].Value.Trim();
                    return true;
                case "Regex":
                    if (string.IsNullOrWhiteSpace(pattern)) { error = "enter a pattern"; return false; }
                    var m = Regex.Match(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    if (!m.Success) { error = "no match"; return false; }
                    result = (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value).Trim();
                    return true;
                case "ValueMap":
                    var entries = ValueMapOptions.Parse(pattern);
                    var text = value.Trim();
                    var hit = entries.FirstOrDefault(e => e.From != "*" && e.From.Trim().Length > 0 &&
                                string.Equals(e.From.Trim(), text, StringComparison.OrdinalIgnoreCase))
                            ?? entries.FirstOrDefault(e => e.From == "*");   // "anything else"
                    if (hit is null || string.IsNullOrWhiteSpace(hit.To))
                    {
                        error = $"\"{text}\" isn't mapped";
                        return false;
                    }
                    result = hit.To;
                    return true;
                case "StripMarkers":
                    result = Regex.Replace(value, @"^[\s\*]+|[\s\-\*]+$", "", RegexOptions.None, TimeSpan.FromSeconds(1));
                    return true;
                default:
                    return true;   
            }
        }
        catch (ArgumentException) { error = "pattern isn't valid"; return false; }
        catch (RegexMatchTimeoutException) { error = "pattern is too slow"; return false; }
    }
}