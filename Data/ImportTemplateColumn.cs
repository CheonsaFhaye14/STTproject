using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportTemplateColumn
{
    public int ImportTemplateColumnId { get; set; }

    public int ImportTemplateId { get; set; }

    public string? FieldKey { get; set; }

    public string HeaderText { get; set; } = null!;

    public string ReadMode { get; set; } = null!;

    public string? OptionsJson { get; set; }

    public bool IsRequired { get; set; }

    public bool IsIgnored { get; set; }

    public int SortOrder { get; set; }

    public virtual ImportTemplate ImportTemplate { get; set; } = null!;
}
