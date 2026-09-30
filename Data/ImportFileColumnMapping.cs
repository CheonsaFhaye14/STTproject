using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportFileColumnMapping
{
    public int ImportFileSheetId { get; set; }

    public int ImportTemplateColumnId { get; set; }

    public string? ActualHeader { get; set; }

    public int? ColumnIndex { get; set; }

    public virtual ImportFileSheet ImportFileSheet { get; set; } = null!;

    public virtual ImportTemplateColumn ImportTemplateColumn { get; set; } = null!;
}
