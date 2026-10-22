namespace STTproject.Features.Admin.CompanyItem.DTOs;

public sealed record HistoricalPriceRow(string? PriceListType, DateTime EffectivityDate, decimal NewPrice);

public sealed record ExistingPriceHistoryRow(
    int CompanyItemPriceHistoryId,
    string? PriceListType,
    DateTime EffectivityDate,
    decimal? OldPrice,
    decimal NewPrice,
    DateTime? AppliedDate);

public sealed record HistoricalPriceApplyResult(bool Success, string? Error, int Inserted, int Skipped);