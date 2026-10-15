namespace STTproject.Features.Admin.CompanyItem.DTOs;

// One price a file row wants to record: which price list, from which date, at what price.
// PriceListType: null = the single Stock Price, "Regional" or "National" (KCPI).
public sealed record HistoricalPriceRow(string? PriceListType, DateTime EffectivityDate, decimal NewPrice);

// A row that is already in CompanyItemPriceHistories, used to detect duplicates and conflicts.
public sealed record ExistingPriceHistoryRow(
    int CompanyItemPriceHistoryId,
    string? PriceListType,
    DateTime EffectivityDate,
    decimal? OldPrice,
    decimal NewPrice,
    DateTime? AppliedDate);

public sealed record HistoricalPriceApplyResult(bool Success, string? Error, int Inserted, int Skipped);