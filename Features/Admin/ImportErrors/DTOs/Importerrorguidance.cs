namespace STTproject.Features.Admin.ImportErrors.DTOs
{
    /// <summary>
    /// Plain-language "what happened / why / what to do" text for the details page, plus the Fix button's label and route.
    /// Keeps wording out of the Razor file. Unknown types fall back to the stored error message.
    /// </summary>
    public static class ImportErrorGuidance
    {
        public sealed record Text(string What, string Why, string Todo);

        private const string SourceTodo = "Correct it in the source Excel file, then upload the file again.";

        private static readonly Dictionary<string, Text> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SkuNotFound"] = new("This item from the uploaded Excel file could not be matched to an item in Entriel's company item records.",
                "The source item might not have a company item mapping yet, or its name or code doesn't match the existing mapping.",
                "Find the corresponding company item and create or update its item mapping."),
            ["CustomerNotFound"] = new("This customer in the uploaded file could not be matched to a customer in Entriel.",
                "The customer may not be mapped for this subdistributor, or the customer code has a typo.",
                "Find the matching customer and create or update its customer mapping."),
            ["UomNotFound"] = new("This unit of measure in the file is not linked to a unit Entriel recognizes for the item.",
                "The subdistributor uses a unit name that hasn't been mapped yet.",
                "Map this unit to an existing unit of measure for the item."),
            ["InvoiceDateInvalid"] = new("The invoice date in the file is not a real calendar date.",
                "The date may have been typed incorrectly, or the file uses a different date format than the import template expects.",
                "Look at the problem value. If it is a real date but the template reads it the wrong way (for example day and month swapped, or the wrong column), use Fix Template. If it is not a real date (for example 32/01/2026), correct it in the source Excel file. Then upload the file again."),
            ["InvoiceDateMissing"] = new("A row in the file has no invoice date.", "The date cell is empty.", SourceTodo),
            ["QuantityInvalid"] = new("The quantity in the file is not a valid number.",
                "A letter may have been typed instead of a digit, for example the letter O instead of zero.", SourceTodo),
            ["QuantityMissing"] = new("A row in the file has no quantity.", "The quantity cell is empty.", SourceTodo),
            ["SkuMissing"] = new("A row in the file has no item code or name.", "The item cell is empty.", SourceTodo),
            ["CustomerMissing"] = new("A row in the file has no customer.", "The customer cell is empty.", SourceTodo),
            ["UomMissing"] = new("A row in the file has no unit of measure.", "The unit cell is empty.", SourceTodo),
            ["InvoiceHeaderInvalid"] = new("The invoice header information in the file is missing or inconsistent.",
                "A required header value may be missing, renamed or empty.", SourceTodo),
            ["InvoiceInconsistent"] = new("Rows of the same invoice disagree with each other.",
                "For example the same invoice number appears with different dates or customers.", SourceTodo),
            ["RequiredSheetMissing"] = new("The file does not contain a sheet the import needs.",
                "The sheet may have been renamed or deleted before uploading.", "Restore the sheet in the source Excel file, then upload the file again."),
            ["MissingRequiredHeaders"] = new("The sheet is missing column headers the import template requires.",
                "A header may have been renamed, moved or deleted.", "Restore the headers in the source Excel file, then upload the file again."),
            ["FileUnreadable"] = new("The uploaded file could not be opened as an Excel workbook.",
                "The file may be damaged, password protected or not a real .xlsx file.", "Open and re-save the file in Excel, then upload it again."),
        };

        /// <summary>Falls back to the stored message when the type has no written guidance.</summary>
        public static Text For(string? errorType, string message) =>
            errorType != null && Map.TryGetValue(errorType, out var t)
                ? t
                : new Text(string.IsNullOrWhiteSpace(message) ? "This problem was found while importing the file." : message,
                    "Entriel has no extra explanation for this type of problem yet.",
                    "Review the affected rows below and correct the source Excel file, then upload it again.");

        // TODO: point these at your real maintenance pages. The error id lets the page open pre-filled.
        private static readonly Dictionary<string, (string Label, string Route)> Fix = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SkuNotFound"] = ("Fix Item Mapping", "/admin/map-items"),
            ["CustomerNotFound"] = ("Fix Customer Mapping", "/admin/customers"),
            ["UomNotFound"] = ("Fix UOM Mapping", "/admin/map-items"),
        };

        public static string FixLabel(string? errorType) =>
            ImportErrorPresentation.UsesTemplateFix(errorType) ? "Fix Template"
            : errorType != null && Fix.TryGetValue(errorType, out var f) ? f.Label : "Fix";

        public static string FixRoute(string? errorType, int importErrorId) =>
            errorType != null && Fix.TryGetValue(errorType, out var f) ? $"{f.Route}?importErrorId={importErrorId}" : "/admin/import-errors";
    }
}