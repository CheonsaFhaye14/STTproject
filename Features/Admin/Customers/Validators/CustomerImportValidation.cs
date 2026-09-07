using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public enum ImportDuplicateOutcome { None, ExactDuplicate, FillableBlank }

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

        // Checks the real unique key (CustomerCode + CustomerName + SubDistributorId + SubdCustCode + SubdCustName)
        // instead of just CustomerCode + SubDistributorId. If a sibling row exists with the same
        // Code/Name/Subdistributor but blank Subd fields, that row should be filled in rather than
        // rejected or duplicated.
        public static async Task<ImportDuplicateResult> ValidateDuplicateAsync(
            IAdminCustomerService customerService,
            string customerCode,
            string customerName,
            int subDistributorId,
            string? subdCustCode,
            string? subdCustName)
        {
            var match = await customerService.CheckImportDuplicateAsync(
                customerCode, customerName, subDistributorId, subdCustCode, subdCustName);

            return match.MatchType switch
            {
                ImportMatchType.ExactDuplicate => new ImportDuplicateResult(
                    ImportDuplicateOutcome.ExactDuplicate,
                    match.ExistingCustomerId,
                    "This exact Customer Code / Subd Customer Code / Subd Store Name combination already exists for this Subdistributor."),

                ImportMatchType.FillableBlank => new ImportDuplicateResult(
                    ImportDuplicateOutcome.FillableBlank,
                    match.ExistingCustomerId,
                    null),

                _ => new ImportDuplicateResult(ImportDuplicateOutcome.None, null, null)
            };
        }
    }
}