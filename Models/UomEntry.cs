namespace STTproject.Models;

using System.Text.Json.Serialization;
using STTproject.Features.User.MapItem.DTOs;

public sealed class UomEntry
{
    public int? ItemsUomId { get; set; }
    public string? ConversionBasedOn { get; set; }

    [JsonConverter(typeof(LenientNullableIntConverter))]
    public int? Conversion { get; set; }

    public decimal? Price { get; set; }
    public bool IsAutoCalculated { get; set; }
    public bool IsActive { get; set; } = true;
}