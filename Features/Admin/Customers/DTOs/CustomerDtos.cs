using System;
using STTproject.Shared.Components.Filter;
namespace STTproject.Features.Admin.Customers.DTOs
{
    public class CustomerListDto
    {
        public int CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
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
        public string? CustomerType { get; set; }
        public int SubDistributorId { get; set; }
        public bool IsActive { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
        public int? CreatedBy { get; set; } // ← add
    }

    public class CustomerUpdateDto : CustomerCreateDto
    {
        public int CustomerId { get; set; }
        public int? UpdatedBy { get; set; } // ← add
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
        public int ZipCode { get; set; }
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
        public List<CustomerImportRowResult> Rows { get; } = new();
        public List<PreparedCustomerGroup> PreparedGroups { get; } = new();
        public List<CustomerImportIssue> Issues { get; } = new(); // header/global-level problems

        public int SuccessCount => Rows.Count(r => r.IsSuccess);
        public int ErrorCount => Rows.Count(r => !r.IsSuccess);
        public bool HasRows => Rows.Count > 0;
        public bool HasIssues => Issues.Count > 0;

        public void AddError(int rowNumber, string customerCode, string message)
            => Issues.Add(new CustomerImportIssue(rowNumber, customerCode, message));
    }

    public sealed class CustomerImportRowResult
    {
        public int RowNumber { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerType { get; set; } = string.Empty;
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public int? ZipCode { get; set; }
        public bool IsSuccess { get; set; }
        public int? CustomerId { get; set; }
        public List<string> Issues { get; } = new();
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

    public sealed record CustomerImportIssue(int RowNumber, string CustomerCode, string Message);

}

