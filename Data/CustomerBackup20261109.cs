using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class CustomerBackup20261109
{
    public int CustomerId { get; set; }

    public string CustomerCode { get; set; } = null!;

    public string CustomerName { get; set; } = null!;

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

    public string? SubdCustCode { get; set; }

    public string? SubdCustName { get; set; }
}
