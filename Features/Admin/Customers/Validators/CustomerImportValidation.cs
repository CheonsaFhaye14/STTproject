namespace STTproject.Features.Admin.Customers.Services
{
    public sealed class CustomerImportValidation
    {
        public static bool ValidateHeader(string[] headers, IReadOnlyDictionary<string, string[]> headerMappings)
        {
            var providedHeaders = new HashSet<string>(
                headers.Select(h => h.Trim()),
                StringComparer.OrdinalIgnoreCase);

            // For every required field, at least one of its known aliases must be present.
            return headerMappings.All(kvp => kvp.Value.Any(alias => providedHeaders.Contains(alias)));
        }
    }
}