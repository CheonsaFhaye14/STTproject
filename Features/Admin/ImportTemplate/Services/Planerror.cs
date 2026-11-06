namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>
/// Why a workbook can't be read as configured, as a code. Lives in the template layer so template and sheet
/// resolution never depend on import logging; the importer maps these to its own error-type strings.
/// </summary>
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

/// <summary>
/// One blocking reason, as data. <see cref="Message"/> is the exact sentence the results page shows, so the log and
/// the screen never disagree. <see cref="Detail"/> is a small, serializable object (no stack traces).
/// </summary>
public sealed record PlanError(
    PlanErrorKind Kind,
    string Message,
    int? SheetIndex = null,
    string? SheetName = null,
    object? Detail = null);