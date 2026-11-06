using System.Text.Json;
using System.Text.RegularExpressions;

namespace STTproject.Features.Admin.ImportErrors.DTOs
{
    /// <summary>
    /// Turns database values (ErrorType, FieldName, Status, ResolutionType) into the plain-language
    /// text shown on the Import Errors pages, so admins never have to read "SkuNotFound".
    /// </summary>
    public static class ImportErrorPresentation
    {
        public const string Pending = "pending";
        public const string Ignored = "ignored";
        public const string Resolved = "resolved";

        // Error types that can be fixed inside Entriel by correcting a mapping.
        private static readonly HashSet<string> Fixable = new(StringComparer.OrdinalIgnoreCase)
        {
            "SkuNotFound", "CustomerNotFound", "UomNotFound"
        };

        // Error types whose cause can be the import template (wrong date format, wrong column). Fixed in Import Templates,
        // not in a mapping page, so they are deliberately not in Fixable above (the list pages route Fixable types by FixRoute).
        private static readonly HashSet<string> TemplateFixable = new(StringComparer.OrdinalIgnoreCase)
        {
            "InvoiceDateInvalid"
        };

        private static readonly Dictionary<string, string> Titles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SkuNotFound"] = "Item not found",
            ["CustomerNotFound"] = "Customer not found",
            ["UomNotFound"] = "Unit of measure not mapped",
            ["InvoiceDateInvalid"] = "Invalid invoice date",
            ["InvoiceHeaderInvalid"] = "Invalid invoice header",
            ["QuantityInvalid"] = "Invalid quantity",
            ["RequiredSheetMissing"] = "Required sheet missing",
        };

        private static readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ItemName"] = "Item Name",
            ["SkuCode"] = "SKU Code",
            ["CustomerCode"] = "Customer Code",
            ["CustomerName"] = "Customer Name",
            ["UOM"] = "Unit of Measure",
            ["Quantity"] = "Quantity",
            ["InvoiceDate"] = "Invoice Date",
            ["InvoiceNumber"] = "Invoice Number",
        };

        public static string Title(string? errorType)
        {
            if (string.IsNullOrWhiteSpace(errorType)) return "Unknown problem";
            return Titles.TryGetValue(errorType, out var t) ? t : SplitPascal(errorType);
        }

        public static string FieldLabel(string? fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return string.Empty;
            return Fields.TryGetValue(fieldName, out var f) ? f : SplitPascal(fieldName);
        }

        /// <summary>True when the Fix button should be offered (the error has a maintenance page to fix it in).</summary>
        public static bool CanFix(string? errorType) => !string.IsNullOrWhiteSpace(errorType) && Fixable.Contains(errorType);

        /// <summary>True when the details page should offer "Fix Template" (open the import template that read the file).</summary>
        public static bool UsesTemplateFix(string? errorType) => !string.IsNullOrWhiteSpace(errorType) && TemplateFixable.Contains(errorType);

        public static string StatusKey(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant();

        public static bool IsPending(string? status) => StatusKey(status) == Pending;

        public static string StatusText(string? status) => StatusKey(status) switch
        {
            Pending => "Pending",
            Ignored => "Ignored",
            Resolved => "Resolved",
            _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status!
        };

        /// <summary>
        /// Second line under a Resolved status. Align the "auto" check with whatever your
        /// ResolutionType column stores for automatic (verified) resolutions.
        /// </summary>
        public static string ResolutionNote(string? status, string? resolutionType, DateTime? resolvedAt)
        {
            if (StatusKey(status) != Resolved) return string.Empty;
            var auto = resolutionType != null && resolutionType.Contains("auto", StringComparison.OrdinalIgnoreCase);
            var verified = resolutionType != null && resolutionType.StartsWith("Verified", StringComparison.OrdinalIgnoreCase);
            var text = verified ? "Verified by re-import" : auto ? "Verified automatically" : "Resolved manually";
            return resolvedAt.HasValue ? $"{text} · {resolvedAt.Value:MMM d, yyyy}" : text;
        }

        /// <summary>
        /// Reads the problematic value out of ImportError.DataJson. Falls back to the error message when the
        /// JSON has no recognizable value property. Adjust the property names to match what your importer writes.
        /// </summary>
        public static string ExtractProblemValue(string? dataJson, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(dataJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(dataJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var wanted in new[] { "ProblemValue", "Value", "OriginalValue", "RawValue" })
                        {
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                if (!string.Equals(prop.Name, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                                if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                                var s = prop.Value.ToString();
                                if (!string.IsNullOrWhiteSpace(s)) return s;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Not JSON we understand; use the fallback below.
                }
            }
            return fallback;
        }

        private static string SplitPascal(string value) =>
            Regex.Replace(value.Trim(), "(?<=[a-z0-9])(?=[A-Z])", " ");
    }
}