using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportFileSheet
{
    public int ImportFileSheetId { get; set; }

    public int ImportFileId { get; set; }

    public int? ImportTemplateSheetId { get; set; }

    public string SheetName { get; set; } = null!;

    public int? HeaderRowNumber { get; set; }

    public int? TotalRows { get; set; }

    public virtual ImportFile ImportFile { get; set; } = null!;

    public virtual ICollection<ImportFileColumnMapping> ImportFileColumnMappings { get; set; } = new List<ImportFileColumnMapping>();

    public virtual ICollection<ImportRow> ImportRows { get; set; } = new List<ImportRow>();

    public virtual ImportTemplateSheet? ImportTemplateSheet { get; set; }
}
