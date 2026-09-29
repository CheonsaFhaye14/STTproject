using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Validators;
using TemplateEntity = STTproject.Data.ImportTemplate;
using ColumnEntity = STTproject.Data.ImportTemplateColumn;

namespace STTproject.Features.Admin.ImportTemplate.Services;

public sealed class ImportTemplateService : IImportTemplateService
{
    private readonly IDbContextFactory<SttprojectContext> _factory;

    public ImportTemplateService(IDbContextFactory<SttprojectContext> factory)
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

        return await q
            .OrderBy(t => t.ImportType)
            .ThenBy(t => t.SubDistributorId)          // global defaults (null) first
            .ThenBy(t => t.Principal)
            .ThenByDescending(t => t.IsActive)
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
            .Include(x => x.ImportTemplateColumns)
            .FirstOrDefaultAsync(x => x.ImportTemplateId == importTemplateId, ct);

        if (t is null) return null;

        return new ImportTemplateEditDto
        {
            ImportTemplateId = t.ImportTemplateId,
            ImportType = t.ImportType,
            TemplateName = t.TemplateName,
            SubDistributorId = t.SubDistributorId,
            Principal = t.Principal,
            SheetName = t.SheetName,
            HeaderRowNumber = t.HeaderRowNumber,
            AllowGlobalFallback = t.AllowGlobalFallback,
            IsActive = t.IsActive,
            Version = t.Version,
            Columns = t.ImportTemplateColumns
                .OrderBy(c => c.SortOrder).ThenBy(c => c.ImportTemplateColumnId)
                .Select(c => new ImportTemplateColumnEditDto
                {
                    ImportTemplateColumnId = c.ImportTemplateColumnId,
                    HeaderText = c.HeaderText,
                    FieldKey = c.FieldKey,
                    ReadMode = c.ReadMode,
                    OptionsJson = c.OptionsJson,
                    IsRequired = c.IsRequired,
                    IsIgnored = c.IsIgnored,
                    SortOrder = c.SortOrder
                })
                .ToList()
        };
    }

    public async Task<ImportTemplateSaveResult> SaveAsync(ImportTemplateEditDto dto, int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            return ImportTemplateSaveResult.Fail("Unable to identify the current user. Please sign in again.");

        NormalizeScope(dto);

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
                    .Include(t => t.ImportTemplateColumns)
                    .FirstOrDefaultAsync(t => t.ImportTemplateId == dto.ImportTemplateId, ct);

                if (existing is null)
                    return ImportTemplateSaveResult.Fail("This template no longer exists.");

                if (existing.Version != dto.Version)
                    return ImportTemplateSaveResult.Fail("This template was changed by someone else since you opened it. Reload it and try again.");

                entity = existing;
                entity.Version++;
                entity.UpdatedDate = DateTime.UtcNow;
                entity.UpdatedBy = userId;

                // Replace the column set. Deleting first (and saving) avoids unique-index clashes
                // on HeaderText if headers were swapped between rows.
                ctx.ImportTemplateColumns.RemoveRange(entity.ImportTemplateColumns);
                await ctx.SaveChangesAsync(ct);
                entity.ImportTemplateColumns.Clear();
            }

            entity.ImportType = dto.ImportType;
            entity.TemplateName = await BuildTemplateNameAsync(ctx, dto.ImportType, dto.SubDistributorId, dto.Principal, ct);
            entity.SubDistributorId = dto.SubDistributorId;
            entity.Principal = dto.Principal;
            entity.AllowGlobalFallback = dto.AllowGlobalFallback;
            entity.IsActive = dto.IsActive;
            // SheetName and HeaderRowNumber are not edited on the page, so any stored value is kept.

            AddColumns(entity, dto.Columns);

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
            .Include(t => t.ImportTemplateColumns)
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
                TemplateName = await BuildTemplateNameAsync(ctx, source.ImportType, dto.TargetSubDistributorId, targetPrincipal, ct),
                SubDistributorId = dto.TargetSubDistributorId,
                Principal = targetPrincipal,
                SheetName = source.SheetName,
                HeaderRowNumber = source.HeaderRowNumber,
                AllowGlobalFallback = source.AllowGlobalFallback,
                Version = 1,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = userId
            };

            foreach (var c in source.ImportTemplateColumns.OrderBy(c => c.SortOrder))
            {
                copy.ImportTemplateColumns.Add(new ColumnEntity
                {
                    FieldKey = c.FieldKey,
                    HeaderText = c.HeaderText,
                    ReadMode = c.ReadMode,
                    OptionsJson = c.OptionsJson,
                    IsRequired = c.IsRequired,
                    IsIgnored = c.IsIgnored,
                    SortOrder = c.SortOrder
                });
            }

            ctx.ImportTemplates.Add(copy);
            await ctx.SaveChangesAsync(ct);
            return ImportTemplateSaveResult.Ok(copy.ImportTemplateId);
        }
        catch(DbUpdateConcurrencyException)
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

    private static void NormalizeScope(ImportTemplateEditDto dto)
    {
        dto.Principal = string.IsNullOrWhiteSpace(dto.Principal) ? null : dto.Principal.Trim();

        foreach (var c in dto.Columns)
        {
            c.HeaderText = c.HeaderText?.Trim() ?? string.Empty;
            c.OptionsJson = string.IsNullOrWhiteSpace(c.OptionsJson) ? null : c.OptionsJson.Trim();
            if (c.IsIgnored) c.FieldKey = null;
            else c.FieldKey = string.IsNullOrWhiteSpace(c.FieldKey) ? null : c.FieldKey.Trim();
            if (string.IsNullOrWhiteSpace(c.ReadMode)) c.ReadMode = "Text";
        }
    }

    private static void AddColumns(TemplateEntity entity, List<ImportTemplateColumnEditDto> columns)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            entity.ImportTemplateColumns.Add(new ColumnEntity
            {
                HeaderText = c.HeaderText,
                FieldKey = c.IsIgnored ? null : c.FieldKey,
                ReadMode = c.IsIgnored ? "Text" : c.ReadMode,
                OptionsJson = c.OptionsJson,
                IsRequired = c.IsRequired && !c.IsIgnored,
                IsIgnored = c.IsIgnored,
                SortOrder = i + 1
            });
        }
    }

    private static async Task<string> BuildTemplateNameAsync(
        SttprojectContext ctx, string importType, int? subDistributorId, string? principal, CancellationToken ct)
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

    private static async Task<string?> CheckSubdExistsAsync(SttprojectContext ctx, int? subDistributorId, CancellationToken ct)
    {
        if (subDistributorId is null) return null;

        var exists = await ctx.SubDistributors
            .AnyAsync(s => s.SubDistributorId == subDistributorId.Value && s.IsActive, ct);

        return exists ? null : "The selected subdistributor was not found or is inactive.";
    }

    // Mirrors the filtered unique index UX_ImportTemplates_ActiveScope so the user gets a
    // readable message instead of a database error. NULL scope values compare as equal.
    private static Task<string?> FindActiveConflictAsync(
        SttprojectContext ctx, string importType, int? subDistributorId, string? principal, int excludeId, CancellationToken ct)
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

    private static string ConflictMessage(string existingName) =>
        $"An active template \"{existingName}\" already exists for this scope. Deactivate it first.";
}