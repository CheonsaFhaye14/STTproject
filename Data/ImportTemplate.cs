using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportTemplate
{
    public int ImportTemplateId { get; set; }

    public string ImportType { get; set; } = null!;

    public int? SubDistributorId { get; set; }

    public string? Principal { get; set; }

    public string TemplateName { get; set; } = null!;

    public string? SheetName { get; set; }

    public int? HeaderRowNumber { get; set; }

    public bool AllowGlobalFallback { get; set; }

    public int Version { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual ICollection<ImportFile> ImportFiles { get; set; } = new List<ImportFile>();

    public virtual ICollection<ImportTemplateColumn> ImportTemplateColumns { get; set; } = new List<ImportTemplateColumn>();

    public virtual SubDistributor? SubDistributor { get; set; }
}
