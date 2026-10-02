namespace STTproject.Features.Admin.ImportTemplate.DTOs;

/// <summary>Allowed values, shared by dropdowns, the validator and the service.</summary>
public static class SheetMatchModes
{
    public const string Any = "Any";
    public const string Exact = "Exact";
    public const string Contains = "Contains";
    public const string Position = "Position";
    public const string Ignore = "Ignore";
    public static readonly string[] All = { Any, Exact, Contains, Position, Ignore };
}

public static class HeaderRowModes
{
    public const string Automatic = "Automatic";
    public const string Fixed = "Fixed";
    public static readonly string[] All = { Automatic, Fixed };
}

public static class ColumnRuleTypes
{
    public const string Direct = "Direct";
    public const string Regex = "Regex";
    public const string ValueMap = "ValueMap";
    public static readonly string[] All =
    {
        Direct, "Trim", "StripLeadingCode", "StripParenthetical", "ExtractBracketed", "StripMarkers", ValueMap, Regex,
    };
}

public sealed class ValueMapEntry
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public static class ValueMapOptions
{
    public static List<ValueMapEntry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<ValueMapEntry>>(json) ?? new(); }
        catch (System.Text.Json.JsonException) { return new(); }
    }
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
    public int SheetCount { get; set; }
    public int ColumnCount { get; set; }
    public DateTime? LastChangedDate { get; set; }

    /// <summary>A blank subd means this is the global default for the import type.</summary>
    public bool IsGlobalDefault => SubDistributorId is null && string.IsNullOrWhiteSpace(Principal);

    public string ScopeLabel =>
        IsGlobalDefault ? "Global default"
        : SubDistributorId is not null ? $"{SubdCode} - {SubdName}"
        : Principal!;
}

/// <summary>The template being created or edited, with its sheets.</summary>
public sealed class ImportTemplateEditDto
{
    /// <summary>0 for a new template.</summary>
    public int ImportTemplateId { get; set; }

    public string ImportType { get; set; } = "SalesInvoice";
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Null = global default for the import type.</summary>
    public int? SubDistributorId { get; set; }
    public string? Principal { get; set; }

    public bool AllowGlobalFallback { get; set; } = false;
    public bool IsActive { get; set; } = true;

    /// <summary>Loaded from the database; the service uses it to bump the version on save.</summary>
    public int Version { get; set; } = 1;

    public List<ImportTemplateSheetEditDto> Sheets { get; set; } = new();
}

/// <summary>How to find one sheet in the workbook, where its header is, and which columns it holds.</summary>
public sealed class ImportTemplateSheetEditDto
{
    /// <summary>0 for a new sheet.</summary>
    public int ImportTemplateSheetId { get; set; }

    /// <summary>Client-side identity so Blazor keeps the right sheet tab when sheets are added or removed.</summary>
    public Guid RowKey { get; set; } = Guid.NewGuid();

    /// <summary>Admin's own name for this sheet, e.g. "Sales". Not the name inside the Excel file.</summary>
    public string SheetLabel { get; set; } = string.Empty;

    /// <summary>Any / Exact / Contains / Position / Ignore.</summary>
    public string SheetMatchMode { get; set; } = SheetMatchModes.Any;

    /// <summary>The name, text, or position number, depending on the match mode. Empty for Any and Ignore.</summary>
    public string? SheetMatchValue { get; set; }
    public int HeaderRowCount { get; set; } = 1;

    public bool IsRequired { get; set; } = true;

    /// <summary>Automatic / Fixed.</summary>
    public string HeaderRowMode { get; set; } = HeaderRowModes.Automatic;

    /// <summary>Only used when HeaderRowMode is Fixed.</summary>
    public int? HeaderRowNumber { get; set; }

    public int SortOrder { get; set; }

    public List<ImportTemplateColumnEditDto> Columns { get; set; } = new();
}

/// <summary>One row of the column grid: "this header in the file means this field, read this way".</summary>
public sealed class ImportTemplateColumnEditDto
{
    /// <summary>0 for a new row. Used to line rows up when saving.</summary>
    public int ImportTemplateColumnId { get; set; }

    /// <summary>Client-side identity so Blazor keeps the right row when rows are added or removed.</summary>
    public Guid RowKey { get; set; } = Guid.NewGuid();

    /// <summary>The header as it usually appears in the subd's file.</summary>
    public string HeaderText { get; set; } = string.Empty;

    /// <summary>Must be one of the registry keys for the import type. Null only when IsIgnored.</summary>
    public string? FieldKey { get; set; }

    /// <summary>How the value is cleaned or extracted: Direct, Trim, Regex...</summary>
    public string RuleType { get; set; } = ColumnRuleTypes.Direct;

    /// <summary>Extra settings for a rule, e.g. the pattern for Regex.</summary>
    public string? OptionsJson { get; set; }

    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Input for the "clone template" modal.</summary>
public sealed class CloneImportTemplateDto
{
    public int SourceTemplateId { get; set; }

    /// <summary>Null = clone as a global default.</summary>
    public int? TargetSubDistributorId { get; set; }
    public string? TargetPrincipal { get; set; }
    public string? TemplateName { get; set; }
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

public static class ImportTemplateSortModes
{
    public const string Default = "Default";          // import type, then who it applies to
    public const string NameAsc = "NameAsc";
    public const string AppliesTo = "AppliesTo";
    public const string NewestChanged = "NewestChanged";
    public const string OldestChanged = "OldestChanged";
    public const string MostColumns = "MostColumns";
}

/// <summary>Filters for the template list page. Null means "don't filter on this".</summary>
public sealed class ImportTemplateFilterDto
{
    public string? ImportType { get; set; }
    public int? SubDistributorId { get; set; }
    public string? Principal { get; set; }
    public bool? IsActive { get; set; }                       // null = show all (was true)
    public string? SearchText { get; set; }
    public string SortBy { get; set; } = ImportTemplateSortModes.Default;
}