using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class SubDistributor
{
    public int SubDistributorId { get; set; }

    public string SubdCode { get; set; } = null!;

    public string CompanySubdCode { get; set; } = null!;

    public string SubdName { get; set; } = null!;

    public string CityMunicipality { get; set; } = null!;

    public string Province { get; set; } = null!;

    public int EncoderId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

    public virtual User Encoder { get; set; } = null!;

    public virtual ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();

    public virtual ICollection<SubdItem> SubdItems { get; set; } = new List<SubdItem>();

    public virtual User? UpdatedByNavigation { get; set; }
}
