namespace STTproject.Features.Admin.ImportTemplate.DTOs;

/// <summary>Filters for the template list page. Null means "don't filter on this".</summary>
public sealed class ImportTemplateFilterDto
{
    public string? ImportType { get; set; }
    public int? SubDistributorId { get; set; }
    public string? Principal { get; set; }
    public bool? IsActive { get; set; } = true;

}

/// <summary>One row in the list page table.</summary>
public sealed class ImportTemplateListItemDto
{
    public int ImportTemplateId { get; set; }
    public string ImportType { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;

    public int? SubDistributorId { get; set; }
    public string? SubdCode { get; set; }
    public string? SubdName { get; set; }
    public string? Principal { get; set; }

    public int Version { get; set; }
    public bool IsActive { get; set; }
    public bool AllowGlobalFallback { get; set; }
    public int ColumnCount { get; set; }
    public DateTime? LastChangedDate { get; set; }

    /// <summary>A blank subd means this is the global default for the import type.</summary>
    public bool IsGlobalDefault => SubDistributorId is null && string.IsNullOrWhiteSpace(Principal);

    public string ScopeLabel =>
        IsGlobalDefault ? "Global default"
        : SubDistributorId is not null ? $"{SubdCode} - {SubdName}"
        : Principal!;
}

/// <summary>The template being created or edited, with its column rows.</summary>
public sealed class ImportTemplateEditDto
{
    /// <summary>0 for a new template.</summary>
    public int ImportTemplateId { get; set; }

    public string ImportType { get; set; } = "SalesInvoice";
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Null = global default for the import type.</summary>
    public int? SubDistributorId { get; set; }
    public string? Principal { get; set; }

    public string? SheetName { get; set; }
    public int? HeaderRowNumber { get; set; }

    public bool AllowGlobalFallback { get; set; } = true;
    public bool IsActive { get; set; } = true;

    /// <summary>Loaded from the database; the service uses it to bump the version on save.</summary>
    public int Version { get; set; } = 1;

    public List<ImportTemplateColumnEditDto> Columns { get; set; } = new();
}

/// <summary>One row of the column grid: "this header in the file means this field".</summary>
public sealed class ImportTemplateColumnEditDto
{
    /// <summary>0 for a new row. Only used to line rows up when saving.</summary>
    public int ImportTemplateColumnId { get; set; }

    /// <summary>Client-side identity so Blazor keeps the right row when rows are added or removed.</summary>
    public Guid RowKey { get; set; } = Guid.NewGuid();

    /// <summary>The header as it appears in the subd's file.</summary>
    public string HeaderText { get; set; } = string.Empty;

    /// <summary>Must be one of the registry keys for the import type. Null only when IsIgnored.</summary>
    public string? FieldKey { get; set; }

    public string ReadMode { get; set; } = "Text";

    /// <summary>Extra settings for a read mode, e.g. the pattern for Regex.</summary>
    public string? OptionsJson { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>The column exists in the file but should be skipped.</summary>
    public bool IsIgnored { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Input for the "clone template" modal.</summary>
public sealed class CloneImportTemplateDto
{
    public int SourceTemplateId { get; set; }

    /// <summary>Null = clone as a global default.</summary>
    public int? TargetSubDistributorId { get; set; }
    public string? TargetPrincipal { get; set; }
}

/// <summary>Returned by save, clone and activate calls so the page can show errors.</summary>
public sealed class ImportTemplateSaveResult
{
    public bool Success => Errors.Count == 0;
    public int? ImportTemplateId { get; set; }
    public List<string> Errors { get; } = new();

    public static ImportTemplateSaveResult Ok(int id) => new() { ImportTemplateId = id };

    public static ImportTemplateSaveResult Fail(params string[] errors)
    {
        var result = new ImportTemplateSaveResult();
        result.Errors.AddRange(errors);
        return result;
    }
}

/// <summary>Subd dropdown option on the list and edit pages.</summary>
public sealed class SubDistributorOptionDto
{
    public int SubDistributorId { get; set; }
    public string SubdCode { get; set; } = string.Empty;
    public string SubdName { get; set; } = string.Empty;

    public string Display => $"{SubdCode} - {SubdName}";
}