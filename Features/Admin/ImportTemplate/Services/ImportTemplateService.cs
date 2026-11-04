using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.ImportTemplate.DTOs;
using STTproject.Features.Admin.ImportTemplate.Validators;
using TemplateEntity = STTproject.Data.ImportTemplate;
using SheetEntity = STTproject.Data.ImportTemplateSheet;
using ColumnEntity = STTproject.Data.ImportTemplateColumn;
using AliasEntity = STTproject.Data.ImportTemplateColumnAlias;

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

        var asc = f.SortAscending;

        IOrderedQueryable<TemplateEntity> ordered = f.SortColumn switch
        {
            "AppliesTo" => asc
                ? q.OrderBy(t => t.SubDistributor != null ? t.SubDistributor.SubdCode : (t.Principal ?? ""))
                : q.OrderByDescending(t => t.SubDistributor != null ? t.SubDistributor.SubdCode : (t.Principal ?? "")),
            "Columns" => asc
                ? q.OrderBy(t => t.ImportTemplateColumns.Count)
                : q.OrderByDescending(t => t.ImportTemplateColumns.Count),
            "Version" => asc
                ? q.OrderBy(t => t.Version)
                : q.OrderByDescending(t => t.Version),
            "Status" => asc
                ? q.OrderBy(t => t.IsActive)
                : q.OrderByDescending(t => t.IsActive),
            "LastChanged" => asc
                ? q.OrderByDescending(t => t.UpdatedDate ?? t.CreatedDate)
                : q.OrderBy(t => t.UpdatedDate ?? t.CreatedDate),
            _ => asc
                ? q.OrderBy(t => t.ImportType).ThenBy(t => t.SubDistributorId).ThenBy(t => t.Principal)
                : q.OrderByDescending(t => t.ImportType).ThenByDescending(t => t.SubDistributorId).ThenByDescending(t => t.Principal)
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
                    .ThenInclude(c => c.ImportTemplateColumnAliases)
            .FirstOrDefaultAsync(x => x.ImportTemplateId == importTemplateId, ct);

        if (t is null) return null;

        return new ImportTemplateEditDto
        {
            ImportTemplateId = t.ImportTemplateId,
            ImportType = t.ImportType,
            TemplateName = t.TemplateName,
            SubDistributorId = t.SubDistributorId,
            Principal = t.Principal,
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
                            Aliases = c.ImportTemplateColumnAliases
                                .OrderBy(a => a.SortOrder).ThenBy(a => a.ImportTemplateColumnAliasId)
                                .Select(a => new ImportTemplateAliasEditDto
                                {
                                    ImportTemplateColumnAliasId = a.ImportTemplateColumnAliasId,
                                    AliasText = a.AliasText,
                                    SortOrder = a.SortOrder
                                }).ToList(),
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

        var scopeErrors = ImportTemplateValidator.ValidateScope(dto.ImportType, dto.SubDistributorId, dto.Principal);
        if (scopeErrors.Count > 0)
            return ImportTemplateSaveResult.Fail(scopeErrors.ToArray());

        var scopeError = await CheckSubdExistsAsync(ctx, dto.SubDistributorId, ct);
        if (scopeError is not null)
            return ImportTemplateSaveResult.Fail(scopeError);

        // Several active templates may share a scope; they only need different names so they can be told apart.
        var templateName = await ResolveTemplateNameAsync(
            ctx, dto.ImportType, dto.SubDistributorId, dto.Principal, dto.TemplateName, dto.ImportTemplateId, ct);

        if (dto.IsActive && await HasActiveNameConflictAsync(
                ctx, dto.ImportType, dto.SubDistributorId, dto.Principal, templateName, dto.ImportTemplateId, ct))
            return ImportTemplateSaveResult.Fail(NameConflictMessage(templateName));

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
                            .ThenInclude(c => c.ImportTemplateColumnAliases)
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
            entity.TemplateName = templateName;
            entity.SubDistributorId = dto.SubDistributorId;
            entity.Principal = dto.Principal;
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
                    .ThenInclude(c => c.ImportTemplateColumnAliases)
            .FirstOrDefaultAsync(t => t.ImportTemplateId == dto.SourceTemplateId, ct);

        if (source is null)
            return ImportTemplateSaveResult.Fail("The template to clone no longer exists.");

        var scopeErrors = ImportTemplateValidator.ValidateScope(source.ImportType, dto.TargetSubDistributorId, targetPrincipal);
        if (scopeErrors.Count > 0)
            return ImportTemplateSaveResult.Fail(scopeErrors.ToArray());

        var scopeError = await CheckSubdExistsAsync(ctx, dto.TargetSubDistributorId, ct);
        if (scopeError is not null)
            return ImportTemplateSaveResult.Fail(scopeError);

        // Cloning into a scope that already has templates is allowed; the copy just needs its own name.
        var copyName = await ResolveTemplateNameAsync(
            ctx, source.ImportType, dto.TargetSubDistributorId, targetPrincipal, dto.TemplateName, 0, ct);

        if (await HasActiveNameConflictAsync(ctx, source.ImportType, dto.TargetSubDistributorId, targetPrincipal, copyName, 0, ct))
            return ImportTemplateSaveResult.Fail(NameConflictMessage(copyName));

        try
        {
            var copy = new TemplateEntity
            {
                ImportType = source.ImportType,
                TemplateName = copyName,
                SubDistributorId = dto.TargetSubDistributorId,
                Principal = targetPrincipal,
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

                    foreach (var a in c.ImportTemplateColumnAliases.OrderBy(a => a.SortOrder))
                        colCopy.ImportTemplateColumnAliases.Add(new AliasEntity { AliasText = a.AliasText, SortOrder = a.SortOrder });

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

        if (isActive && await HasActiveNameConflictAsync(
                ctx, t.ImportType, t.SubDistributorId, t.Principal, t.TemplateName, t.ImportTemplateId, ct))
            return ImportTemplateSaveResult.Fail(NameConflictMessage(t.TemplateName));

        try
        {
            t.IsActive = isActive;
            t.Version++;                     // an edit session opened before this change must fail its version check
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
            if (s.SheetMatchMode is SheetMatchModes.Any or SheetMatchModes.Headers or SheetMatchModes.Ignore) s.SheetMatchValue = null;
            if (s.HeaderRowMode != HeaderRowModes.Fixed) s.HeaderRowNumber = null;
            if (s.SheetMatchMode == SheetMatchModes.Ignore) s.Columns.Clear();

            foreach (var c in s.Columns)
            {
                c.HeaderText = c.HeaderText?.Trim() ?? string.Empty;
                c.FieldKey = string.IsNullOrWhiteSpace(c.FieldKey) ? null : c.FieldKey.Trim();
                c.OptionsJson = string.IsNullOrWhiteSpace(c.OptionsJson) ? null : c.OptionsJson.Trim();
                if (string.IsNullOrWhiteSpace(c.RuleType)) c.RuleType = ColumnRuleTypes.Direct;

                // Aliases: trimmed, blanks dropped, kept in the order given. Repeats are left for the validator to report.
                c.Aliases ??= new();
                foreach (var a in c.Aliases) a.AliasText = a.AliasText?.Trim() ?? string.Empty;
                c.Aliases.RemoveAll(a => a.AliasText.Length == 0);
                for (var i = 0; i < c.Aliases.Count; i++) c.Aliases[i].SortOrder = i + 1;
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
                foreach (var c in sheet.ImportTemplateColumns)
                    ctx.ImportTemplateColumnAliases.RemoveRange(c.ImportTemplateColumnAliases.ToList());
                ctx.ImportTemplateColumns.RemoveRange(sheet.ImportTemplateColumns.ToList());
                ctx.ImportTemplateSheets.Remove(sheet);
                continue;
            }

            var keepColumnIds = dto.Sheets
                .First(s => s.ImportTemplateSheetId == sheet.ImportTemplateSheetId)
                .Columns.Where(c => c.ImportTemplateColumnId != 0)
                .Select(c => c.ImportTemplateColumnId).ToHashSet();

            // Aliases go with their parent column, whether or not the database cascades.
            var removedColumns = sheet.ImportTemplateColumns
                .Where(c => !keepColumnIds.Contains(c.ImportTemplateColumnId)).ToList();
            foreach (var c in removedColumns)
                ctx.ImportTemplateColumnAliases.RemoveRange(c.ImportTemplateColumnAliases.ToList());
            ctx.ImportTemplateColumns.RemoveRange(removedColumns);
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

                SyncAliases(ctx, col, cd.Aliases);
            }
        }
    }

    /// <summary>
    /// Makes the column's alias rows match the DTO: existing rows keep their ids, new ones are added,
    /// rows no longer listed are deleted. The cleaning rule is not stored per alias; it belongs to the parent.
    /// </summary>
    private static void SyncAliases(EntrielContext ctx, ColumnEntity col, List<ImportTemplateAliasEditDto> wanted)
    {
        var keepIds = wanted.Where(a => a.ImportTemplateColumnAliasId != 0)
            .Select(a => a.ImportTemplateColumnAliasId).ToHashSet();

        var gone = col.ImportTemplateColumnAliases
            .Where(a => a.ImportTemplateColumnAliasId != 0 && !keepIds.Contains(a.ImportTemplateColumnAliasId))
            .ToList();
        if (gone.Count > 0) ctx.ImportTemplateColumnAliases.RemoveRange(gone);

        for (var i = 0; i < wanted.Count; i++)
        {
            var ad = wanted[i];
            var alias = ad.ImportTemplateColumnAliasId == 0
                ? null
                : col.ImportTemplateColumnAliases.FirstOrDefault(a => a.ImportTemplateColumnAliasId == ad.ImportTemplateColumnAliasId);

            if (alias is null)
            {
                alias = new AliasEntity();
                col.ImportTemplateColumnAliases.Add(alias);
            }

            alias.AliasText = ad.AliasText;
            alias.SortOrder = i + 1;
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

    /// <summary>
    /// Two active templates in the same scope can't share a name. Scope alone is no longer a conflict.
    /// </summary>
    private static Task<bool> HasActiveNameConflictAsync(
        EntrielContext ctx, string importType, int? subDistributorId, string? principal,
        string templateName, int excludeId, CancellationToken ct)
    {
        return ctx.ImportTemplates
            .AsNoTracking()
            .AnyAsync(t => t.IsActive
                           && t.ImportType == importType
                           && t.SubDistributorId == subDistributorId
                           && t.Principal == principal
                           && t.TemplateName == templateName
                           && t.ImportTemplateId != excludeId, ct);
    }

    /// <summary>
    /// A typed name is used as is (the conflict check handles clashes). A blank name gets a generated
    /// one, with " (2)", " (3)" ... added so it never repeats a template already in this scope.
    /// </summary>
    private static async Task<string> ResolveTemplateNameAsync(
        EntrielContext ctx, string importType, int? subDistributorId, string? principal,
        string? requested, int excludeId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim();

        var baseName = await BuildTemplateNameAsync(ctx, importType, subDistributorId, principal, ct);

        var taken = await ctx.ImportTemplates
            .AsNoTracking()
            .Where(t => t.ImportType == importType
                        && t.SubDistributorId == subDistributorId
                        && t.Principal == principal
                        && t.ImportTemplateId != excludeId)
            .Select(t => t.TemplateName)
            .ToListAsync(ct);
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(baseName)) return baseName;

        for (var n = 2; ; n++)
        {
            var suffix = $" ({n})";
            var stem = baseName.Length + suffix.Length <= 150 ? baseName : baseName[..(150 - suffix.Length)];
            var candidate = stem + suffix;
            if (!used.Contains(candidate)) return candidate;
        }
    }

    public async Task<IReadOnlyList<ImportTemplateEditDto>> GetCandidatesForImportAsync(
        string importType, int? subDistributorId, string? principal, CancellationToken ct = default)
    {
        var def = ImportFieldRegistry.Get(importType);
        if (def is null) return Array.Empty<ImportTemplateEditDto>();

        var principalKey = string.IsNullOrWhiteSpace(principal) ? null : principal.Trim();

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var q = ctx.ImportTemplates.AsNoTracking()
            .Where(t => t.IsActive && t.ImportType == importType);

        // Only the scope this import type uses counts, plus the global default.
        if (def.Scope == ImportScope.Principal)
        {
            q = principalKey is null
                ? q.Where(t => t.SubDistributorId == null && t.Principal == null)
                : q.Where(t => t.SubDistributorId == null && (t.Principal == null || t.Principal == principalKey));
        }
        else
        {
            q = subDistributorId is null
                ? q.Where(t => t.Principal == null && t.SubDistributorId == null)
                : q.Where(t => t.Principal == null && (t.SubDistributorId == null || t.SubDistributorId == subDistributorId));
        }

        var ids = await q
            .OrderBy(t => t.SubDistributorId == null && t.Principal == null ? 1 : 0)   // scope-specific first
            .ThenBy(t => t.TemplateName)
            .ThenBy(t => t.ImportTemplateId)
            .Select(t => t.ImportTemplateId)
            .ToListAsync(ct);

        var result = new List<ImportTemplateEditDto>(ids.Count);
        foreach (var id in ids)
        {
            var dto = await GetAsync(id, ct);
            if (dto is not null) result.Add(dto);
        }
        return result;
    }

    public async Task<ImportTemplateEditDto?> ResolveForImportAsync(string importType, int? subDistributorId, CancellationToken ct = default)
    {
        var candidates = await GetCandidatesForImportAsync(importType, subDistributorId, null, ct);

        var scoped = candidates.Where(c => !c.IsGlobalDefault).ToList();
        var globals = candidates.Where(c => c.IsGlobalDefault).ToList();

        if (scoped.Count > 1)
            throw new TemplateSelectionRequiredException(importType, scoped.Select(c => c.TemplateName).ToList());
        if (scoped.Count == 1) return scoped[0];

        if (globals.Count > 1)
            throw new TemplateSelectionRequiredException(importType, globals.Select(c => c.TemplateName).ToList());
        return globals.FirstOrDefault();
    }
    private static string NameConflictMessage(string name) =>
        $"An active template named \"{name}\" already exists for this scope. Use a different name.";
}