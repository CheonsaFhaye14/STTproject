using System;
using System.Collections.Generic;

namespace STTproject.Models;

public partial class User
{
    public int UserId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Role { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual ICollection<CustomerBranch> CustomerBranchCreatedByNavigations { get; set; } = new List<CustomerBranch>();

    public virtual ICollection<CustomerBranch> CustomerBranchUpdatedByNavigations { get; set; } = new List<CustomerBranch>();

    public virtual ICollection<Customer> CustomerCreatedByNavigations { get; set; } = new List<Customer>();

    public virtual ICollection<Customer> CustomerUpdatedByNavigations { get; set; } = new List<Customer>();

    public virtual ICollection<SalesInvoice> SalesInvoiceCreatedByNavigations { get; set; } = new List<SalesInvoice>();

    public virtual ICollection<SalesInvoice> SalesInvoiceUpdatedByNavigations { get; set; } = new List<SalesInvoice>();

    public virtual ICollection<SubDistributor> SubDistributorCreatedByNavigations { get; set; } = new List<SubDistributor>();

    public virtual ICollection<SubDistributor> SubDistributorEncoders { get; set; } = new List<SubDistributor>();

    public virtual ICollection<SubDistributor> SubDistributorUpdatedByNavigations { get; set; } = new List<SubDistributor>();

    public virtual ICollection<SubdItem> SubdItemCreatedByNavigations { get; set; } = new List<SubdItem>();

    public virtual ICollection<SubdItemUom> SubdItemUomCreatedByNavigations { get; set; } = new List<SubdItemUom>();

    public virtual ICollection<SubdItemUom> SubdItemUomUpdatedByNavigations { get; set; } = new List<SubdItemUom>();

    public virtual ICollection<SubdItem> SubdItemUpdatedByNavigations { get; set; } = new List<SubdItem>();
}
