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
                "Find the corresponding company item and create or update its item mapping. Or if the item is a typo, correct it in the source Excel file and upload the file again. Or if it can be fixed in the import template, use Fix Template."),
            ["CustomerNotFound"] = new("This customer in the uploaded file could not be matched to a customer in Entriel.",
                "The customer may not be mapped for this subdistributor, or the customer code has a typo.",
                "Find the matching customer on the Customers page and create or update its customer mapping. Or if the customer is a typo, correct it in the source Excel file and upload the file again. Or if it can be fixed in the import template, use Fix Template."),
            ["UomNotFound"] = new("This unit of measure in the file is not linked to a unit Entriel recognizes for the item.",
                "The subdistributor uses a unit name that hasn't been mapped yet.",
                "Open Map Item and add or correct this unit of measure for the item. Or if the unit is a typo, correct it in the source Excel file and upload the file again. Or if it can be fixed in the import template, use Fix Template."),
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
            ["InvoiceHeaderInvalid"] = new("The invoice header information in the file is missing or inconsistent. Entriel checks an invoice as a whole, using all of its rows together.",
                "A required header value (for example the salesman, customer or date) may be missing or empty on one or more rows of this invoice. Rows whose header values differ are treated as separate invoices, so fixing only one row can leave the rest of the invoice invalid.",
                "Correct the value on every row that belongs to this invoice (the rows are listed below). Then upload the file again."),
            ["SalesmanMissing"] = new("Rows in the uploaded file have no salesman name.",
                "The salesman cell is empty on these rows. Every row of an invoice needs a salesman, and rows with different salesmen are treated as separate invoices.",
                "Fill in the salesman name on each row listed below that is marked as missing, then upload the file again. Or if the salesman exist on the file but is not recognized, use Fix Template to correct the template's salesman column."),
            ["InvoiceInconsistent"] = new("Rows of the same invoice disagree with each other.",
                "For example the same invoice number appears with different dates or customers.", SourceTodo),
            ["RequiredSheetMissing"] = new("The file does not contain a sheet the import needs.",
                "The sheet may have been renamed or deleted before uploading.", "Restore the sheet in the source Excel file, then upload the file again."),
            ["MissingRequiredHeaders"] = new("The sheet is missing column headers the import template requires.",
                "A header may have been renamed, moved or deleted.", "Restore the headers in the source Excel file, then upload the file again."),
            ["FileUnreadable"] = new("The uploaded file could not be opened as an Excel workbook.",
                "The file may be damaged, password protected or not a real .xlsx file.", "Open and re-save the file in Excel, then upload it again."),
        };

        /// <summary>True when the type has written guidance. Its Why and Todo already explain the problem, so the page leaves out What.</summary>
        public static bool IsKnown(string? errorType) => errorType != null && Map.ContainsKey(errorType);

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
            ["SkuNotFound"] = ("Fix Item Mapping", "/admin/mapitem"),
            ["CustomerNotFound"] = ("Fix Customer Mapping", "/admin/customer"),
            ["UomNotFound"] = ("Fix UOM Mapping", "/admin/mapitem"),
        };

        // ---- Fix choices: the ways an administrator can fix an error. One choice goes straight there; several are offered in a window. ----

        public sealed record FixChoice(string Key, string Label, string Description);

        public const string ChoiceReimport = "reimport";
        public const string ChoiceTemplate = "template";
        public const string ChoiceMapping = "mapping";

        // The Sales Invoice page opens its import window by itself with ?import=1 and picks the subdistributor with &subd=.
        public static string ReimportRoute(int? subDistributorId) =>
            "/admin/salesinvoice?import=1" + (subDistributorId is > 0 ? "&subd=" + subDistributorId : string.Empty);

        // Error types whose cause can be the source Excel file.
        private static readonly HashSet<string> SourceFixable = new(StringComparer.OrdinalIgnoreCase)
        {
            "SalesmanMissing", "CustomerNotFound", "InvoiceDateInvalid", "InvoiceDateMissing", "QuantityInvalid", "QuantityMissing", "SkuMissing", "CustomerMissing",
            "UomMissing", "InvoiceHeaderInvalid", "InvoiceInconsistent", "SkuNotFound", "UomNotFound", "RequiredSheetMissing", "MissingRequiredHeaders",
            "FileUnreadable"
        };

        private static string MappingHelp(string errorType) =>
            string.Equals(errorType, "CustomerNotFound", StringComparison.OrdinalIgnoreCase)
                ? "Open Customers to add the customer or correct its subdistributor mapping."
                : string.Equals(errorType, "UomNotFound", StringComparison.OrdinalIgnoreCase)
                    ? "Open Map Item, pick the subdistributor and item, and add or correct its unit of measure."
                    : "Open Map Item and correct the item mapping.";

        public static IReadOnlyList<FixChoice> FixChoices(string? errorType)
        {
            var list = new List<FixChoice>();
            if (errorType == null) return list;

            if (Fix.TryGetValue(errorType, out var f))
                list.Add(new FixChoice(ChoiceMapping, f.Label, MappingHelp(errorType)));
            if (SourceFixable.Contains(errorType))
                list.Add(new FixChoice(ChoiceReimport, "Fix the Excel file and re-import", "Correct the source file, then upload it again on the Sales Invoice import page."));
            if (ImportErrorPresentation.UsesTemplateFix(errorType))
                list.Add(new FixChoice(ChoiceTemplate, "Fix the import template", "Open the template that read this file and correct its column or format."));
            return list;
        }

        public static string FixLabel(string? errorType) =>
            ImportErrorPresentation.UsesTemplateFix(errorType) ? "Fix Template"
            : errorType != null && Fix.TryGetValue(errorType, out var f) ? f.Label : "Fix";

        public static string FixRoute(string? errorType, int importErrorId) =>
            errorType != null && Fix.TryGetValue(errorType, out var f) ? $"{f.Route}?importErrorId={importErrorId}" : "/admin/import-errors";
    }
}