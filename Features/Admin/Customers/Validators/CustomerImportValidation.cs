using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    // ExactDuplicate   nothing new: the customer already has this mapping (or the row has none)
    // AddMapping       the customer exists; the mapping is new and will be added to it
    // MappingConflict  the mapping belongs to a different customer in this subdistributor (row can't be saved)
    public enum ImportDuplicateOutcome { None, ExactDuplicate, AddMapping, MappingConflict }

    public sealed record ImportDuplicateResult(ImportDuplicateOutcome Outcome, int? ExistingCustomerId, string? IssueMessage);

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

        // A customer is one row per (Subdistributor, Customer Code); its subd mappings live in SubdCustomer.
        public static async Task<ImportDuplicateResult> ValidateDuplicateAsync(
            IAdminCustomerService customerService,
            string customerCode,
            int subDistributorId,
            string? subdCustCode,
            string? subdCustName)
        {
            var match = await customerService.CheckImportDuplicateAsync(
                customerCode, subDistributorId, subdCustCode, subdCustName);

            return match.MatchType switch
            {
                ImportMatchType.ExactDuplicate => new ImportDuplicateResult(
                    ImportDuplicateOutcome.ExactDuplicate,
                    match.ExistingCustomerId,
                    "This Customer Code / Subd Customer Code / Subd Store Name combination already exists for this Subdistributor."),

                ImportMatchType.AddMapping => new ImportDuplicateResult(
                    ImportDuplicateOutcome.AddMapping,
                    match.ExistingCustomerId,
                    null),

                ImportMatchType.MappingConflict => new ImportDuplicateResult(
                    ImportDuplicateOutcome.MappingConflict,
                    match.ExistingCustomerId,
                    $"Subd Customer Code '{SubdCustomerKey.Code(subdCustCode)}' / Subd Store Name '{SubdCustomerKey.Name(subdCustName)}' " +
                    $"is already mapped to another customer ({match.OwnerCustomerCode}) in this Subdistributor."),

                _ => new ImportDuplicateResult(ImportDuplicateOutcome.None, null, null)
            };
        }
    }
}