using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportRow
{
    public long ImportRowId { get; set; }

    public int ImportFileSheetId { get; set; }

    public int RowNumber { get; set; }

    public string? RawJson { get; set; }

    public string? MappedJson { get; set; }

    public string Status { get; set; } = null!;

    public string? MessagesJson { get; set; }

    public bool IsSelected { get; set; }

    public DateTime? CommittedDate { get; set; }

    public int? TargetId { get; set; }

    public virtual ImportFileSheet ImportFileSheet { get; set; } = null!;
}
