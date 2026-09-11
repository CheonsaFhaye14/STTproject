using System.Text.Json;
using System.Text.Json.Serialization;

namespace STTproject.Features.User.MapItem.DTOs;

public sealed class LenientNullableIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            // Legacy value like 12.0000 — TryGetInt32 fails on any decimal point,
            // so fall back to reading as decimal and rounding.
            var decimalValue = reader.GetDecimal();
            return (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (int.TryParse(text, out var parsedInt))
            {
                return parsedInt;
            }

            if (decimal.TryParse(text, out var parsedDecimal))
            {
                return (int)Math.Round(parsedDecimal, MidpointRounding.AwayFromZero);
            }
        }

        // Anything else unreadable — treat as missing rather than crashing the circuit.
        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}