using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace STTproject.Services;

/// <summary>
/// What makes an error "the same problem" apart from its type, field and where it came from: the offending value and any
/// context that keeps equal values apart (e.g. the SKU a unit of measure was used with).
/// </summary>
public sealed record ErrorIdentity(string? Value, string? Context = null);

/// <summary>
/// Stable identity of an import error across batches. Pure: no database, no clock.
///
/// SHA-256 (hex, 64 chars) of: version, import type, sub-distributor, error type, field, normalized value, normalized context.
/// Normalization is the one the row aggregator already uses to group rows inside a batch (trim, collapse whitespace,
/// upper-case), so "same error inside a batch" and "same error across batches" can never disagree.
///
/// Changing anything that feeds the hash changes every fingerprint. Bump <see cref="Version"/> when that is intended, so old
/// and new fingerprints can't be mistaken for each other.
/// </summary>
public static class ImportErrorFingerprint
{
    public const string Version = "v1";
    public const int MaxPartLength = 200;

    // Unit Separator: not something a spreadsheet cell realistically holds, and Normalize turns it into a space anyway,
    // so a value can never smuggle a separator into the hashed text and shift a boundary between two parts.
    private const char Separator = '\u001F';

    private static readonly Regex WhitespaceRun = new(@"[\s\u001F]+", RegexOptions.Compiled);

    public static string Compute(
        string importType,
        int? subDistributorId,
        string errorType,
        string? fieldName,
        ErrorIdentity identity)
    {
        var text = string.Join(
            Separator,
            Version,
            Normalize(importType),
            subDistributorId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Normalize(errorType),
            Normalize(fieldName),
            Normalize(identity.Value),
            Normalize(identity.Context));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Trim, collapse inner whitespace, upper-case, cap at <see cref="MaxPartLength"/>. The original text is kept elsewhere.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = WhitespaceRun.Replace(text.Trim(), " ").ToUpperInvariant();
        return normalized.Length <= MaxPartLength ? normalized : normalized[..MaxPartLength];
    }
}