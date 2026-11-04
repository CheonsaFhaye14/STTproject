using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>Settings of a Date column: an optional regex that extracts the date text, and the format(s) it is read with.</summary>
public sealed class DateOptions
{
    public string? Pattern { get; set; }
    public string? Format { get; set; }

    public static DateOptions Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        var s = raw.Trim();
        if (!s.StartsWith('{')) return new() { Format = s };          // legacy: plain format string
        try { return JsonSerializer.Deserialize<DateOptions>(s, JsonOpts) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Plain format string when there is no pattern (keeps old data identical), JSON otherwise. Null when both are blank.</summary>
    public static string? ToOptionsJson(string? format, string? pattern)
    {
        format = string.IsNullOrWhiteSpace(format) ? null : format.Trim();
        pattern = string.IsNullOrWhiteSpace(pattern) ? null : pattern.Trim();
        if (pattern is null) return format;
        return JsonSerializer.Serialize(new DateOptions { Pattern = pattern, Format = format }, JsonOpts);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public string[] Formats =>
        (Format ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Validates the pattern. Returns null when fine. Used by the template validator.</summary>
    public string? Validate()
    {
        if (!string.IsNullOrWhiteSpace(Pattern))
        {
            try { _ = Regex.IsMatch("", Pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException) { return "The date pattern isn't a valid regular expression."; }
        }
        return null;
    }

    /// <summary>The one date-reading routine. The importer should call this too, so test and import never disagree.</summary>
    public bool TryRead(string text, out DateTime date, out string extracted, out string? error)
    {
        date = default;
        error = null;
        extracted = text.Trim();

        if (!string.IsNullOrWhiteSpace(Pattern))
        {
            try
            {
                var m = Regex.Match(extracted, Pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                if (!m.Success) { error = "pattern found no date"; return false; }
                extracted = (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value).Trim();
            }
            catch (ArgumentException) { error = "pattern isn't valid"; return false; }
            catch (RegexMatchTimeoutException) { error = "pattern is too slow"; return false; }
        }

        var formats = Formats;
        if (formats.Length > 0)
        {
            if (DateTime.TryParseExact(extracted, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
            error = $"\"{extracted}\" doesn't match {string.Join(" or ", formats)}";
            return false;
        }

        if (DateTime.TryParse(extracted, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        error = "can't read this as a date";
        return false;
    }
}