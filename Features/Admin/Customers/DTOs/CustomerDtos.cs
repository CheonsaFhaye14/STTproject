using STTproject.Shared.Components.Filter;
namespace STTproject.Features.Admin.Customers.DTOs
{
    public class CustomerListDto
    {
        public int CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? SubdCustCode { get; set; }
        public string? SubdCustName { get; set; }
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? SubDistributorName { get; set; }
    }

    public class CustomerDetailDto
    {
        public int CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? SubdCustCode { get; set; }
        public string? SubdCustName { get; set; }
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
    }

    public class CustomerCreateDto
    {
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? SubdCustCode { get; set; }
        public string? SubdCustName { get; set; }
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
        public int? CreatedBy { get; set; }
    }

    public class CustomerGroupUpdateDto
    {
        public int CustomerId { get; set; }
        public List<SubdMappingDto> Mappings { get; set; } = new();
    }

    public class SubdMappingDto
    {
        public int CustomerId { get; set; }
        public int SubDistributorId { get; set; }
        public string? SubdCustCode { get; set; }
        public string? SubdCustName { get; set; }
    }

    public class CustomerUpdateDto : CustomerCreateDto
    {
        public int CustomerId { get; set; }
        public int? UpdatedBy { get; set; }
    }

    public class SubDistributorDto
    {
        public int SubDistributorId { get; set; }
        public string? SubDistributorName { get; set; }
    }

    public class GeographicDataDto
    {
        public string? Province { get; set; }
        public string? CityMunicipality { get; set; }
        public string? Island { get; set; }
        public int? ZipCode { get; set; }
    }
    public class CustomerImportResultDto
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int TotalCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public bool HasErrors => Errors.Count > 0;
    }
    public class CustomerImportRowDto : IImportRow
    {
        public int RowNumber { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
        public List<string> Issues { get; set; } = new();

        IReadOnlyList<string> IImportRow.Issues => Issues;
    }
    public class CustomerImportGroupDto : IImportGroup<CustomerImportRowDto>
    {
        public string GroupKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<CustomerImportRowDto> Rows { get; set; } = new();
        public List<string> Issues { get; set; } = new();
        public bool Selected { get; set; }
        public bool IsSaved { get; set; }

        IReadOnlyList<string> IImportGroup<CustomerImportRowDto>.Issues => Issues;
    }


    public sealed class CustomerImportResult
    {
        public int SubDistributorId { get; set; }
        public string? SubDistributorName { get; set; }
        public List<string> OriginalHeaders { get; set; } = new();
        public List<CustomerImportRowResult> Rows { get; } = new();
        public List<PreparedCustomerGroup> PreparedGroups { get; } = new();
        public List<CustomerImportIssue> Issues { get; } = new();

        public int SuccessCount => Rows.Count(r => r.IsSuccess);
        public int ErrorCount => Rows.Count(r => !r.IsSuccess);
        public bool HasRows => Rows.Count > 0;
        public bool HasIssues => Issues.Count > 0;

        public void AddError(int rowNumber, string customerCode, string message)
            => Issues.Add(new CustomerImportIssue(rowNumber, customerCode, message));
    }

    public sealed record CustomerDetailsUpdate(
        string? AddressLine, string? City, string? Province, int? ZipCode, string? CustomerType,
        bool OnlyFillBlanks = false)
    {
        private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "0";

        // Returns a list like "City: Manila → MUNTINLUPA CITY".
        // It only changes the customer when commit is true, so the same method works for the preview and the save.
        public List<string> Apply(STTproject.Data.Customer c, bool commit)
        {
            var changes = new List<string>();

            void Set(string label, string? current, string? incoming, Action<string> assign)
            {
                if (Blank(incoming)) return;                                          // blank or 0 in the file: keep what's there
                if (OnlyFillBlanks && !string.IsNullOrWhiteSpace(current)) return;
                var v = incoming!.Trim();
                if (string.Equals(current?.Trim(), v, StringComparison.OrdinalIgnoreCase)) return;   // nothing to change

                changes.Add($"{label}: {(string.IsNullOrWhiteSpace(current) ? "(blank)" : current)} → {v}");
                if (commit) assign(v);
            }

            Set("Address", c.AddressLine, AddressLine, v => c.AddressLine = v);
            Set("City", c.City, City, v => c.City = v);
            Set("Province", c.Province, Province, v => c.Province = v);
            Set("Customer type", c.CustomerType, CustomerType, v => c.CustomerType = v);

            if (ZipCode is > 0 && ZipCode != c.ZipCode && !(OnlyFillBlanks && c.ZipCode is > 0))
            {
                changes.Add($"Zip: {c.ZipCode?.ToString() ?? "(blank)"} → {ZipCode}");
                if (commit) c.ZipCode = ZipCode;
            }

            return changes;
        }
    }

    public sealed class CustomerImportRowResult
    {
        public int RowNumber { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? SubdCustCode { get; set; }
        public string? SubdCustName { get; set; }
        public string? CustomerType { get; set; } = string.Empty;
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
        public bool IsSuccess { get; set; }
        public int? CustomerId { get; set; }
        public List<string> Issues { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Notes { get; } = new();
        public Dictionary<string, string?> RawValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int? ExistingCustomerIdToUpdate { get; set; }
        public bool IsAlreadyImported { get; set; }
        public bool IsDuplicateInFile { get; set; }
        public CustomerDetailsUpdate? DetailsUpdate { get; set; }
    }

    public sealed class PreparedCustomerGroup
    {
        public List<CustomerImportRowResult> Rows { get; } = new();
        public List<CustomerImportIssue> Issues { get; } = new();
        public bool Selected { get; set; }
        public bool IsSaved { get; set; }

        public PreparedCustomerGroup() { }
        public PreparedCustomerGroup(List<CustomerImportRowResult> rows) => Rows = rows ?? new();
    }

    public enum ImportMatchType { None, ExactDuplicate, FillableBlank }
    public sealed record ImportMatchResult(ImportMatchType MatchType, int? ExistingCustomerId);
    public sealed record CustomerImportIssue(int RowNumber, string CustomerCode, string Message);
}