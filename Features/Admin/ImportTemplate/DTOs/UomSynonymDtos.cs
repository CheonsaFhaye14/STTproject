namespace STTproject.Features.Admin.ImportTemplate.DTOs;

public static class UomSynonymLimits
{
    public const int GroupNameMax = 50;
    public const int SpellingMax = 30;
}

/// <summary>One group on the UOM synonyms page.</summary>
public sealed class UomSynonymGroupEditDto
{
    /// <summary>0 for a new group.</summary>
    public int UomSynonymGroupId { get; set; }

    /// <summary>Client-side identity so Blazor keeps the right card when groups are added or removed.</summary>
    public Guid RowKey { get; set; } = Guid.NewGuid();

    public string GroupName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Loaded from the database; a mismatch on save means someone else changed the group.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Trimmed, lower-case, in the order they're tried.</summary>
    public List<string> Spellings { get; set; } = new();
}

/// <summary>Everything the page edits at once: the groups of one scope (global, or one subdistributor).</summary>
public sealed class UomSynonymScopeDto
{
    /// <summary>Null = global.</summary>
    public int? SubDistributorId { get; set; }

    public List<UomSynonymGroupEditDto> Groups { get; set; } = new();

    /// <summary>Groups removed on the page. Groups that are simply missing from <see cref="Groups"/> are left alone,
    /// so a group someone else added meanwhile is never deleted by accident.</summary>
    public List<int> DeletedGroupIds { get; set; } = new();

    /// <summary>Read only, filled for a subdistributor scope: the global groups it can inherit.</summary>
    public List<UomSynonymGroupEditDto> GlobalGroups { get; set; } = new();

    /// <summary>
    /// Subdistributor scope only: the global groups, or single spellings of them, it does not inherit.
    /// Saving replaces this subdistributor's whole list with this one.
    /// </summary>
    public List<UomSynonymExclusionDto> Exclusions { get; set; } = new();
}

/// <summary>One opt-out. Spelling null = the whole global group is not inherited.</summary>
public sealed record UomSynonymExclusionDto(int UomSynonymGroupId, string? Spelling = null);

public sealed class UomSynonymSaveResult
{
    public bool Success => Errors.Count == 0;
    public List<string> Errors { get; } = new();

    public static UomSynonymSaveResult Ok() => new();

    public static UomSynonymSaveResult Fail(IEnumerable<string> errors)
    {
        var result = new UomSynonymSaveResult();
        result.Errors.AddRange(errors);
        return result;
    }

    public static UomSynonymSaveResult Fail(params string[] errors) => Fail((IEnumerable<string>)errors);
}

/// <summary>A subdistributor that differs from global: its own groups and its opt-outs. For the modal's scope rail.</summary>
public sealed record UomSynonymSubdSummaryDto(int SubDistributorId, int OwnGroups, int OptOuts)
{
    public int Changes => OwnGroups + OptOuts;
}