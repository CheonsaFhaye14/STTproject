namespace STTproject.Features.Admin.ImportTemplate.Services;

public enum PlanErrorKind
{
    RequiredSheetMissing,
    AmbiguousSheet,
    TruncatedSheetMatch,
    EmptySheet,
    HeaderRowNotFound,
    AmbiguousColumns,
    MissingRequiredHeaders,
    NoReadableSheet
}

public sealed record PlanError(
    PlanErrorKind Kind,
    string Message,
    int? SheetIndex = null,
    string? SheetName = null,
    object? Detail = null);