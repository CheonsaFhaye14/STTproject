using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportTemplateSheet
{
    public int ImportTemplateSheetId { get; set; }

    public int ImportTemplateId { get; set; }

    public string SheetLabel { get; set; } = null!;

    public string SheetMatchMode { get; set; } = null!;

    public string? SheetMatchValue { get; set; }

    public bool IsRequired { get; set; }

    public string HeaderRowMode { get; set; } = null!;

    public int? HeaderRowNumber { get; set; }

    public int SortOrder { get; set; }

    public virtual ICollection<ImportFileSheet> ImportFileSheets { get; set; } = new List<ImportFileSheet>();

    public virtual ImportTemplate ImportTemplate { get; set; } = null!;

    public virtual ICollection<ImportTemplateColumn> ImportTemplateColumns { get; set; } = new List<ImportTemplateColumn>();
}
