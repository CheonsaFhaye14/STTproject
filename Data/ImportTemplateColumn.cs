using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportTemplateColumn
{
    public int ImportTemplateColumnId { get; set; }

    public int ImportTemplateId { get; set; }

    public string? FieldKey { get; set; }

    public string HeaderText { get; set; } = null!;

    public string? OptionsJson { get; set; }

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    public int ImportTemplateSheetId { get; set; }

    public string RuleType { get; set; } = null!;

    public virtual ImportTemplate ImportTemplate { get; set; } = null!;

    public virtual ICollection<ImportTemplateColumnAlias> ImportTemplateColumnAliases { get; set; } = new List<ImportTemplateColumnAlias>();

    public virtual ImportTemplateSheet ImportTemplateSheet { get; set; } = null!;
}
