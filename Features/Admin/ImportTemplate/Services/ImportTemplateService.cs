using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Validators;
using TemplateEntity = STTproject.Data.ImportTemplate;
using SheetEntity = STTproject.Data.ImportTemplateSheet;
using ColumnEntity = STTproject.Data.ImportTemplateColumn;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public sealed class ImportTemplateService : IImportTemplateService
{
    private readonly IDbContextFactory<EntrielContext> _factory;

    public ImportTemplateService(IDbContextFactory<EntrielContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<ImportTemplateListItemDto>> GetListAsync(ImportTemplateFilterDto f, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var q = ctx.ImportTemplates.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.ImportType))
            q = q.Where(t => t.ImportType == f.ImportType);
        if (f.SubDistributorId.HasValue)
            q = q.Where(t => t.SubDistributorId == f.SubDistributorId);
        if (!string.IsNullOrWhiteSpace(f.Principal))
        {
            var p = f.Principal.Trim();
            q = q.Where(t => t.Principal == p);
        }
        if (f.IsActive.HasValue)
            q = q.Where(t => t.IsActive == f.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(f.SearchText))
        {
            var term = f.SearchText.Trim();
            q = q.Where(t =>
                t.TemplateName.Contains(term) ||
                (t.Principal != null && t.Principal.Contains(term)) ||
                (t.SubDistributor != null &&
                    (t.SubDistributor.SubdCode.Contains(term) || t.SubDistributor.SubdName.Contains(term))));
        }

        IOrderedQueryable<TemplateEntity> ordered = f.SortBy switch
        {
            ImportTemplateSortModes.NameAsc =>
                q.OrderBy(t => t.TemplateName),
            ImportTemplateSortModes.AppliesTo =>
                q.OrderBy(t => t.SubDistributor != null ? t.SubDistributor.SubdCode : (t.Principal ?? "")),
            ImportTemplateSortModes.NewestChanged =>
                q.OrderByDescending(t => t.UpdatedDate ?? t.CreatedDate),
            ImportTemplateSortModes.OldestChanged =>
                q.OrderBy(t => t.UpdatedDate ?? t.CreatedDate),
            ImportTemplateSortModes.MostColumns =>
                q.OrderByDescending(t => t.ImportTemplateColumns.Count),
            _ =>
                q.OrderBy(t => t.ImportType).ThenBy(t => t.SubDistributorId).ThenBy(t => t.Principal)
        };

        return await ordered
            .ThenByDescending(t => t.IsActive)         
            .ThenBy(t => t.TemplateName)
            .Select(t => new ImportTemplateListItemDto
            {
                ImportTemplateId = t.ImportTemplateId,
                ImportType = t.ImportType,
                TemplateName = t.TemplateName,
                SubDistributorId = t.SubDistributorId,
                SubdCode = t.SubDistributor != null ? t.SubDistributor.SubdCode : null,
                SubdName = t.SubDistributor != null ? t.SubDistributor.SubdName : null,
                Principal = t.Principal,
                Version = t.Version,
                IsActive = t.IsActive,
                AllowGlobalFallback = t.AllowGlobalFallback,
                SheetCount = t.ImportTemplateSheets.Count,
                ColumnCount = t.ImportTemplateColumns.Count,
                LastChangedDate = t.UpdatedDate ?? t.CreatedDate
            })
            .ToListAsync(ct);
    }

    public async Task<ImportTemplateEditDto?> GetAsync(int importTemplateId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var t = await ctx.ImportTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.ImportTemplateSheets)
                .ThenInclude(s => s.ImportTemplateColumns)
            .FirstOrDefaultAsync(x => x.ImportTemplateId == importTemplateId, ct);

        if (t is null) return null;

        return new ImportTemplateEditDto
        {
            ImportTemplateId = t.ImportTemplateId,
            ImportType = t.ImportType,
            TemplateName = t.TemplateName,
            SubDistributorId = t.SubDistributorId,
            Principal = t.Principal,
            AllowGlobalFallback = t.AllowGlobalFallback,
            IsActive = t.IsActive,
            Version = t.Version,
            Sheets = t.ImportTemplateSheets
                .OrderBy(s => s.SortOrder).ThenBy(s => s.ImportTemplateSheetId)
                .Select(s => new ImportTemplateSheetEditDto
                {
                    ImportTemplateSheetId = s.ImportTemplateSheetId,
                    SheetLabel = s.SheetLabel,
                    SheetMatchMode = s.SheetMatchMode,
                    SheetMatchValue = s.SheetMatchValue,
                    IsRequired = s.IsRequired,
                    HeaderRowMode = s.HeaderRowMode,
                    HeaderRowNumber = s.HeaderRowNumber,
                    HeaderRowCount = s.HeaderRowCount,
                    SortOrder = s.SortOrder,
                    Columns = s.ImportTemplateColumns
                        .OrderBy(c => c.SortOrder).ThenBy(c => c.ImportTemplateColumnId)
                        .Select(c => new ImportTemplateColumnEditDto
                        {
                            ImportTemplateColumnId = c.ImportTemplateColumnId,
                            HeaderText = c.HeaderText,
                            FieldKey = c.FieldKey,
                            RuleType = c.RuleType,
                            OptionsJson = c.OptionsJson,
                            IsRequired = c.IsRequired,
                            SortOrder = c.SortOrder
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    public async Task<ImportTemplateSaveResult> SaveAsync(ImportTemplateEditDto dto, int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return ImportTemplateSaveResult.Fail("Unable to identify the current user. Please sign in again.");

        NormalizeDto(dto);

        var errors = ImportTemplateValidator.Validate(dto);
        if (errors.Count > 0)
            return ImportTemplateSaveResult.Fail(errors.ToArray());

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var scopeError = await CheckSubdExistsAsync(ctx, dto.SubDistributorId, ct);
        if (scopeError is not null)
            return ImportTemplateSaveResult.Fail(scopeError);

        if (dto.IsActive)
        {
            var conflict = await FindActiveConflictAsync(ctx, dto.ImportType, dto.SubDistributorId, dto.Principal, dto.ImportTemplateId, ct);
            if (conflict is not null)
                return ImportTemplateSaveResult.Fail(ConflictMessage(conflict));
        }

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        try
        {
            TemplateEntity entity;

            if (dto.ImportTemplateId == 0)
            {
                entity = new TemplateEntity
                {
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = userId,
                    Version = 1
                };
                ctx.ImportTemplates.Add(entity);
            }
            else
            {
                var existing = await ctx.ImportTemplates
                    .AsSplitQuery()
                    .Include(t => t.ImportTemplateSheets)
                        .ThenInclude(s => s.ImportTemplateColumns)
                    .FirstOrDefaultAsync(t => t.ImportTemplateId == dto.ImportTemplateId, ct);

                if (existing is null)
                    return ImportTemplateSaveResult.Fail("This template no longer exists.");

                if (existing.Version != dto.Version)
                    return ImportTemplateSaveResult.Fail("This template was changed by someone else since you opened it. Reload it and try again.");

                entity = existing;
                entity.Version++;
                entity.UpdatedDate = DateTime.UtcNow;
                entity.UpdatedBy = userId;

                RemoveDeleted(ctx, entity, dto);
                await ctx.SaveChangesAsync(ct);
            }

            entity.ImportType = dto.ImportType;
            entity.TemplateName = string.IsNullOrWhiteSpace(dto.TemplateName)
                ? await BuildTemplateNameAsync(ctx, dto.ImportType, dto.SubDistributorId, dto.Principal, ct)
                : dto.TemplateName.Trim();            
            entity.SubDistributorId = dto.SubDistributorId;
            entity.Principal = dto.Principal;
            entity.AllowGlobalFallback = dto.AllowGlobalFallback;
            entity.IsActive = dto.IsActive;

            UpsertSheets(ctx, entity, dto);

            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return ImportTemplateSaveResult.Ok(entity.ImportTemplateId);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ImportTemplateSaveResult.Fail(
                "This template was changed by someone else since you opened it. Reload it and try again.");
        }
        catch (DbUpdateException ex)
        {
            return ImportTemplateSaveResult.Fail($"Could not save the template: {ex.GetBaseException().Message}");
        }
    }

    public async Task<ImportTemplateSaveResult> CloneAsync(CloneImportTemplateDto dto, int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return ImportTemplateSaveResult.Fail("Unable to identify the current user. Please sign in again.");

        var targetPrincipal = string.IsNullOrWhiteSpace(dto.TargetPrincipal) ? null : dto.TargetPrincipal.Trim();

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var source = await ctx.ImportTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.ImportTemplateSheets)
                .ThenInclude(s => s.ImportTemplateColumns)
            .FirstOrDefaultAsync(t => t.ImportTemplateId == dto.SourceTemplateId, ct);

        if (source is null)
            return ImportTemplateSaveResult.Fail("The template to clone no longer exists.");

        var def = ImportFieldRegistry.Get(source.ImportType);
        if (def is not null)
        {
            if (def.Scope == ImportScope.Principal && dto.TargetSubDistributorId is not null)
                return ImportTemplateSaveResult.Fail("Company Item templates are set by principal, not by subdistributor.");
            if (def.Scope == ImportScope.Subd && targetPrincipal is not null)
                return ImportTemplateSaveResult.Fail($"{def.Display} templates are set by subdistributor, not by principal.");
        }

        var scopeError = await CheckSubdExistsAsync(ctx, dto.TargetSubDistributorId, ct);
        if (scopeError is not null)
            return ImportTemplateSaveResult.Fail(scopeError);

        var conflict = await FindActiveConflictAsync(ctx, source.ImportType, dto.TargetSubDistributorId, targetPrincipal, 0, ct);
        if (conflict is not null)
            return ImportTemplateSaveResult.Fail(ConflictMessage(conflict));

        try
        {
            var copy = new TemplateEntity
            {
                ImportType = source.ImportType,
                TemplateName = dto.TemplateName ?? await BuildTemplateNameAsync(ctx, source.ImportType, dto.TargetSubDistributorId, targetPrincipal, ct),
                SubDistributorId = dto.TargetSubDistributorId,
                Principal = targetPrincipal,
                AllowGlobalFallback = source.AllowGlobalFallback,
                Version = 1,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = userId
            };

            foreach (var s in source.ImportTemplateSheets.OrderBy(s => s.SortOrder))
            {
                var sheetCopy = new SheetEntity
                {
                    SheetLabel = s.SheetLabel,
                    SheetMatchMode = s.SheetMatchMode,
                    SheetMatchValue = s.SheetMatchValue,
                    IsRequired = s.IsRequired,
                    HeaderRowMode = s.HeaderRowMode,
                    HeaderRowNumber = s.HeaderRowNumber,
                    HeaderRowCount = s.HeaderRowCount,
                    SortOrder = s.SortOrder
                };
                copy.ImportTemplateSheets.Add(sheetCopy);

                foreach (var c in s.ImportTemplateColumns.OrderBy(c => c.SortOrder))
                {
                    var colCopy = new ColumnEntity
                    {
                        FieldKey = c.FieldKey,
                        HeaderText = c.HeaderText,
                        RuleType = c.RuleType,
                        OptionsJson = c.OptionsJson,
                        IsRequired = c.IsRequired,
                        SortOrder = c.SortOrder
                    };

                    // A column belongs to both its template and its sheet.
                    copy.ImportTemplateColumns.Add(colCopy);
                    sheetCopy.ImportTemplateColumns.Add(colCopy);
                }
            }

            ctx.ImportTemplates.Add(copy);
            await ctx.SaveChangesAsync(ct);
            return ImportTemplateSaveResult.Ok(copy.ImportTemplateId);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ImportTemplateSaveResult.Fail(
                "This template was changed by someone else since you opened it. Reload it and try again.");
        }
        catch (DbUpdateException ex)
        {
            return ImportTemplateSaveResult.Fail($"Could not clone the template: {ex.GetBaseException().Message}");
        }
    }

    public async Task<ImportTemplateSaveResult> SetActiveAsync(int importTemplateId, bool isActive, int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return ImportTemplateSaveResult.Fail("Unable to identify the current user. Please sign in again.");

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var t = await ctx.ImportTemplates.FirstOrDefaultAsync(x => x.ImportTemplateId == importTemplateId, ct);
        if (t is null)
            return ImportTemplateSaveResult.Fail("This template no longer exists.");

        if (t.IsActive == isActive)
            return ImportTemplateSaveResult.Ok(t.ImportTemplateId);

        if (isActive)
        {
            var conflict = await FindActiveConflictAsync(ctx, t.ImportType, t.SubDistributorId, t.Principal, t.ImportTemplateId, ct);
            if (conflict is not null)
                return ImportTemplateSaveResult.Fail(ConflictMessage(conflict));
        }

        try
        {
            t.IsActive = isActive;
            t.UpdatedDate = DateTime.UtcNow;
            t.UpdatedBy = userId;
            await ctx.SaveChangesAsync(ct);
            return ImportTemplateSaveResult.Ok(t.ImportTemplateId);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ImportTemplateSaveResult.Fail(
                "This template was changed by someone else since you opened it. Reload it and try again.");
        }
        catch (DbUpdateException ex)
        {
            return ImportTemplateSaveResult.Fail($"Could not update the template: {ex.GetBaseException().Message}");
        }
    }

    public async Task<List<SubDistributorOptionDto>> GetSubDistributorsAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        return await ctx.SubDistributors
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SubdCode)
            .Select(s => new SubDistributorOptionDto
            {
                SubDistributorId = s.SubDistributorId,
                SubdCode = s.SubdCode,
                SubdName = s.SubdName
            })
            .ToListAsync(ct);
    }

    public async Task<List<string>> GetPrincipalsAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        return await ctx.CompanyItems
            .AsNoTracking()
            .Where(c => c.Principal != null && c.Principal != "")
            .Select(c => c.Principal!)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static void NormalizeDto(ImportTemplateEditDto dto)
    {
        dto.Principal = string.IsNullOrWhiteSpace(dto.Principal) ? null : dto.Principal.Trim();

        foreach (var s in dto.Sheets)
        {
            s.SheetLabel = s.SheetLabel?.Trim() ?? string.Empty;
            s.SheetMatchValue = string.IsNullOrWhiteSpace(s.SheetMatchValue) ? null : s.SheetMatchValue.Trim();
            s.HeaderRowCount = Math.Clamp(s.HeaderRowCount, 1, 5);
            // Clear values the chosen mode doesn't use, so stale text can't linger.
            if (s.SheetMatchMode is SheetMatchModes.Any or SheetMatchModes.Ignore) s.SheetMatchValue = null;
            if (s.HeaderRowMode != HeaderRowModes.Fixed) s.HeaderRowNumber = null;
            if (s.SheetMatchMode == SheetMatchModes.Ignore) s.Columns.Clear();

            foreach (var c in s.Columns)
            {
                c.HeaderText = c.HeaderText?.Trim() ?? string.Empty;
                c.FieldKey = string.IsNullOrWhiteSpace(c.FieldKey) ? null : c.FieldKey.Trim();
                c.OptionsJson = string.IsNullOrWhiteSpace(c.OptionsJson) ? null : c.OptionsJson.Trim();
                if (string.IsNullOrWhiteSpace(c.RuleType)) c.RuleType = ColumnRuleTypes.Direct;
            }
        }
    }

    private static void RemoveDeleted(EntrielContext ctx, TemplateEntity entity, ImportTemplateEditDto dto)
    {
        var keepSheetIds = dto.Sheets.Where(s => s.ImportTemplateSheetId != 0)
            .Select(s => s.ImportTemplateSheetId).ToHashSet();

        foreach (var sheet in entity.ImportTemplateSheets.ToList())
        {
            if (!keepSheetIds.Contains(sheet.ImportTemplateSheetId))
            {
                ctx.ImportTemplateColumns.RemoveRange(sheet.ImportTemplateColumns.ToList());
                ctx.ImportTemplateSheets.Remove(sheet);
                continue;
            }

            var keepColumnIds = dto.Sheets
                .First(s => s.ImportTemplateSheetId == sheet.ImportTemplateSheetId)
                .Columns.Where(c => c.ImportTemplateColumnId != 0)
                .Select(c => c.ImportTemplateColumnId).ToHashSet();

            ctx.ImportTemplateColumns.RemoveRange(sheet.ImportTemplateColumns
                .Where(c => !keepColumnIds.Contains(c.ImportTemplateColumnId)).ToList());
        }
    }

    /// <summary>Updates rows that already exist (keeping their ids, so past imports stay linked) and adds new ones.</summary>
    private static void UpsertSheets(EntrielContext ctx, TemplateEntity entity, ImportTemplateEditDto dto)
    {
        for (int si = 0; si < dto.Sheets.Count; si++)
        {
            var sd = dto.Sheets[si];

            var sheet = sd.ImportTemplateSheetId == 0
                ? null
                : entity.ImportTemplateSheets.FirstOrDefault(s => s.ImportTemplateSheetId == sd.ImportTemplateSheetId);

            if (sheet is null)
            {
                sheet = new SheetEntity();
                entity.ImportTemplateSheets.Add(sheet);
            }

            sheet.SheetLabel = sd.SheetLabel;
            sheet.SheetMatchMode = sd.SheetMatchMode;
            sheet.SheetMatchValue = sd.SheetMatchValue;
            sheet.IsRequired = sd.IsRequired;
            sheet.HeaderRowMode = sd.HeaderRowMode;
            sheet.HeaderRowNumber = sd.HeaderRowNumber;
            sheet.HeaderRowCount = sd.HeaderRowCount;
            sheet.SortOrder = si + 1;

            for (int ci = 0; ci < sd.Columns.Count; ci++)
            {
                var cd = sd.Columns[ci];

                var col = cd.ImportTemplateColumnId == 0
                    ? null
                    : sheet.ImportTemplateColumns.FirstOrDefault(c => c.ImportTemplateColumnId == cd.ImportTemplateColumnId);

                if (col is null)
                {
                    col = new ColumnEntity();
                    entity.ImportTemplateColumns.Add(col);   // column belongs to the template...
                    sheet.ImportTemplateColumns.Add(col);    // ...and to its sheet
                }

                col.HeaderText = cd.HeaderText;
                col.FieldKey = cd.FieldKey;
                col.RuleType = cd.RuleType;
                col.OptionsJson = cd.OptionsJson;
                col.IsRequired = cd.IsRequired;
                col.SortOrder = ci + 1;
            }
        }
    }

    private static async Task<string> BuildTemplateNameAsync(
        EntrielContext ctx, string importType, int? subDistributorId, string? principal, CancellationToken ct)
    {
        var display = ImportFieldRegistry.Get(importType)?.Display ?? importType;

        string scope;
        if (subDistributorId is not null)
        {
            scope = await ctx.SubDistributors
                .AsNoTracking()
                .Where(s => s.SubDistributorId == subDistributorId.Value)
                .Select(s => s.SubdCode)
                .FirstOrDefaultAsync(ct) ?? "Unknown subd";
        }
        else if (!string.IsNullOrWhiteSpace(principal))
        {
            scope = principal.Trim();
        }
        else
        {
            scope = "Global";
        }

        var name = $"{display} - {scope}";
        return name.Length <= 150 ? name : name[..150];
    }

    private static async Task<string?> CheckSubdExistsAsync(EntrielContext ctx, int? subDistributorId, CancellationToken ct)
    {
        if (subDistributorId is null) return null;

        var exists = await ctx.SubDistributors
            .AnyAsync(s => s.SubDistributorId == subDistributorId.Value && s.IsActive, ct);

        return exists ? null : "The selected subdistributor was not found or is inactive.";
    }

    private static Task<string?> FindActiveConflictAsync(
        EntrielContext ctx, string importType, int? subDistributorId, string? principal, int excludeId, CancellationToken ct)
    {
        return ctx.ImportTemplates
            .AsNoTracking()
            .Where(t => t.IsActive
                        && t.ImportType == importType
                        && t.SubDistributorId == subDistributorId
                        && t.Principal == principal
                        && t.ImportTemplateId != excludeId)
            .Select(t => t.TemplateName)
            .FirstOrDefaultAsync(ct);
    }
    public async Task<ImportTemplateEditDto?> ResolveForImportAsync(string importType, int? subDistributorId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var candidates = await ctx.ImportTemplates.AsNoTracking()
            .Where(t => t.IsActive && t.ImportType == importType && t.Principal == null
                        && (t.SubDistributorId == subDistributorId || t.SubDistributorId == null))
            .Select(t => new { t.ImportTemplateId, t.SubDistributorId })
            .ToListAsync(ct);

        var specificId = candidates.FirstOrDefault(c => c.SubDistributorId != null)?.ImportTemplateId;
        var globalId = candidates.FirstOrDefault(c => c.SubDistributorId == null)?.ImportTemplateId;

        var specific = specificId is int s ? await GetAsync(s, ct) : null;
        var global = globalId is int g ? await GetAsync(g, ct) : null;

        if (specific is null) return global;
        if (global is null || !specific.AllowGlobalFallback) return specific;

        // "Also use the global default": add global columns for fields the specific template doesn't map.
        // Assumes one readable sheet on each side.
        var target = specific.Sheets.FirstOrDefault(sh => sh.SheetMatchMode != SheetMatchModes.Ignore);
        var source = global.Sheets.FirstOrDefault(sh => sh.SheetMatchMode != SheetMatchModes.Ignore);
        if (target is null || source is null) return specific;

        var mapped = specific.Sheets.SelectMany(sh => sh.Columns).Select(c => c.FieldKey)
            .Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var c in source.Columns.Where(c => !string.IsNullOrWhiteSpace(c.FieldKey) && !mapped.Contains(c.FieldKey!)))
            target.Columns.Add(c);

        return specific;
    }
    private static string ConflictMessage(string existingName) =>
        $"An active template \"{existingName}\" already exists for this scope. Deactivate it first.";
}