using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportTemplateColumnAlias
{
    public int ImportTemplateColumnAliasId { get; set; }

    public int ImportTemplateColumnId { get; set; }

    public string AliasText { get; set; } = null!;

    public int SortOrder { get; set; }

    public virtual ImportTemplateColumn ImportTemplateColumn { get; set; } = null!;
}
