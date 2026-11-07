using System;
using System.Collections.Generic;

namespace STTproject.Data;

public partial class ImportFile
{
    public int ImportFileId { get; set; }

    public string FileHash { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public string StoragePath { get; set; } = null!;

    public long FileSize { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ImportBatch> ImportBatches { get; set; } = new List<ImportBatch>();
}
