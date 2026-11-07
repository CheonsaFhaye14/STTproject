using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace STTproject.Services;

/// <summary>
/// Deterministic SHA-256 of an error's evidence JSON (ImportErrorBatch.DataJson shape). Pure: no database, no clock.
/// The stored evidence text is never changed; only the hash is computed from a canonical form of it:
/// object keys sorted ordinally, "AffectedRows" sorted by (SheetIndex, RowNumber), no whitespace, versioned prefix.
/// Because the sheet name, row number, invoice, header names and cell values are all inside AffectedRows, any change
/// to any of them gives a different hash. Unparseable legacy JSON is hashed as-is with a distinct prefix.
/// </summary>
public static class ImportEvidenceHasher
{
    public const string Version = "v1";

    private const string CanonicalPrefix = "entriel-evidence:" + Version + "\n";
    private const string RawPrefix = "entriel-evidence-raw:" + Version + "\n";
    private const string AffectedRowsProperty = "AffectedRows";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = true
    };

    /// <summary>Uppercase hex SHA-256 (same style as the error fingerprint). Null or blank evidence hashes as an empty payload.</summary>
    public static string Hash(string? dataJson)
    {
        var payload = Canonicalize(dataJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>The exact text that is hashed. Exposed so the dry-run and a manual check can show it.</summary>
    public static string Canonicalize(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return RawPrefix;

        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            return CanonicalPrefix + Write(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or DuplicatePropertyException)
        {
            // Not valid JSON, or JSON with a repeated property name in some object: its meaning is ambiguous, so it is not
            // treated as reliable canonical evidence. It is hashed exactly as stored, under the raw prefix.
            return RawPrefix + dataJson.Trim();
        }
    }

    private sealed class DuplicatePropertyException : Exception
    {
        public DuplicatePropertyException(string name) : base($"Duplicate JSON property '{name}'.") { }
    }

    private static string Write(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            WriteValue(writer, element, sortRowsArray: false);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement element, bool sortRowsArray)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    // Name is the unescaped name, so "a" and "a" count as the same property.
                    if (!seen.Add(prop.Name)) throw new DuplicatePropertyException(prop.Name);
                    writer.WritePropertyName(prop.Name);
                    var isRows = prop.Value.ValueKind == JsonValueKind.Array
                        && string.Equals(prop.Name, AffectedRowsProperty, StringComparison.Ordinal);
                    WriteValue(writer, prop.Value, isRows);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                if (sortRowsArray) WriteSortedRows(writer, element);
                else
                {
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray()) WriteValue(writer, item, false);
                    writer.WriteEndArray();
                }
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }

    // Rows are ordered by (SheetIndex, RowNumber), then by their own canonical text, so the same set of rows always
    // hashes the same however the importer happened to list them.
    private static void WriteSortedRows(Utf8JsonWriter writer, JsonElement rows)
    {
        var items = rows.EnumerateArray()
            .Select(r => (Sheet: IntProperty(r, "SheetIndex"), Row: IntProperty(r, "RowNumber"), Text: Write(r)))
            .OrderBy(x => x.Sheet)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Text, StringComparer.Ordinal)
            .ToList();

        writer.WriteStartArray();
        foreach (var item in items) writer.WriteRawValue(item.Text);
        writer.WriteEndArray();
    }

    private static long IntProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return long.MinValue;
        foreach (var prop in element.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.Ordinal)
                && prop.Value.ValueKind == JsonValueKind.Number
                && prop.Value.TryGetInt64(out var n))
                return n;
        return long.MinValue;
    }
}