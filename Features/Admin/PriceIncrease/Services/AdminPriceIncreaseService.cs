using STTproject.Features.Admin.PriceIncrease.DTOs;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.Admin.PriceIncrease.Services
{
    public class AdminPriceIncreaseService : IAdminPriceIncreaseService
    {
        private readonly IDbContextFactory<SttprojectContext> _dbFactory;
        private readonly IConfiguration _config;

        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);

        public AdminPriceIncreaseService(IDbContextFactory<SttprojectContext> dbFactory, IConfiguration config)
        {
            _dbFactory = dbFactory;
            _config = config;
        }

        public async Task<string?> GetUserNameByIdAsync(int? userId)
        {
            if (userId == null) return null;
            await using var db = _dbFactory.CreateDbContext();
            var user = await db.Users.FindAsync(userId.Value);
            return user?.FullName ?? user?.Username;
        }

        public async Task<IEnumerable<PriceIncreaseTableListDto>> GetAllAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            var now = NowPh();

            return await db.CompanyItemPriceHistories
                .AsNoTracking()
                .Select(h => new PriceIncreaseTableListDto
                {
                    CompanyItemPriceHistoryId = h.CompanyItemPriceHistoryId,
                    CompanyItemId = h.CompanyItemId,
                    CompanyItemName = h.CompanyItem.ItemName,
                    CompanyItemCode = h.CompanyItem.ItemCode,
                    StockPrice = h.CompanyItem.StockPrice,
                    PriceIncreaseAmount = h.PriceIncreaseAmount,
                    EffectivityDate = h.EffectivityDate,
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy,
                    CreatedDate = h.CreatedDate,
                    Status = h.AppliedDate != null
                        ? "Applied"
                        : (h.EffectivityDate <= now ? "Overdue" : "Pending")
                })
                .OrderByDescending(x => x.CreatedDate)
                .ToListAsync();
        }

        public async Task<(IEnumerable<PriceIncreaseTableListDto> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? status,
            string? principal,
            string? sortColumn = "EffectivityDate",
            bool sortAscending = true)
        {
            await using var db = _dbFactory.CreateDbContext();
            var now = NowPh();

            var query = db.CompanyItemPriceHistories
                .AsNoTracking()
                .Include(h => h.CompanyItem)
                .Where(h => string.IsNullOrEmpty(principal) || h.CompanyItem.Principal == principal)
                .Where(h => string.IsNullOrEmpty(search) ||
                    (h.CompanyItem.ItemCode != null && h.CompanyItem.ItemCode.Contains(search)) ||
                    (h.CompanyItem.ItemName != null && h.CompanyItem.ItemName.Contains(search)) ||
                    (h.CompanyItem.Category != null && h.CompanyItem.Category.Contains(search)));

            query = status?.ToLowerInvariant() switch
            {
                "pending" => query.Where(h => h.AppliedDate == null && h.EffectivityDate > now),
                "overdue" => query.Where(h => h.AppliedDate == null && h.EffectivityDate <= now),
                "applied" => query.Where(h => h.AppliedDate != null),
                _ => query // "all" or null/empty
            };

            var total = await query.CountAsync();

            query = (sortColumn, sortAscending) switch
            {
                ("CompanyItemCode", true) => query.OrderBy(h => h.CompanyItem.ItemCode),
                ("CompanyItemCode", false) => query.OrderByDescending(h => h.CompanyItem.ItemCode),
                ("CompanyItemName", true) => query.OrderBy(h => h.CompanyItem.ItemName),
                ("CompanyItemName", false) => query.OrderByDescending(h => h.CompanyItem.ItemName),
                ("Principal", true) => query.OrderBy(h => h.CompanyItem.Principal),
                ("Principal", false) => query.OrderByDescending(h => h.CompanyItem.Principal),
                ("EffectivityDate", true) => query.OrderBy(h => h.EffectivityDate),
                ("EffectivityDate", false) => query.OrderByDescending(h => h.EffectivityDate),
                ("CreatedDate", true) => query.OrderBy(h => h.CreatedDate),
                ("CreatedDate", false) => query.OrderByDescending(h => h.CreatedDate),
                _ => query.OrderByDescending(h => h.EffectivityDate)
            };

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(h => new PriceIncreaseTableListDto
                {
                    CompanyItemPriceHistoryId = h.CompanyItemPriceHistoryId,
                    CompanyItemId = h.CompanyItemId,
                    CompanyItemName = h.CompanyItem.ItemName,
                    CompanyItemCode = h.CompanyItem.ItemCode,
                    StockPrice = h.CompanyItem.StockPrice,
                    PriceIncreaseAmount = h.PriceIncreaseAmount,
                    EffectivityDate = h.EffectivityDate,
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy,
                    CreatedDate = h.CreatedDate,
                    Status = h.AppliedDate != null
                        ? "Applied"
                        : (h.EffectivityDate <= now ? "Overdue" : "Pending")
                })
                .ToListAsync();

            return (items, total);
        }

        /// <summary>
        /// Lazy-loaded detail for one increase event — call only when a row is expanded.
        /// </summary>
        public async Task<IReadOnlyList<PriceIncreaseViewDto>> GetCascadedUomDetailsAsync(int companyItemPriceHistoryId)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.ItemsUomPriceHistories
                .AsNoTracking()
                .Where(h => h.CompanyItemPriceHistoryId == companyItemPriceHistoryId)
                .Select(h => new PriceIncreaseViewDto
                {
                    SubdItemId = h.ItemsUom.SubdItemId,
                    SubdItemCode = h.ItemsUom.SubdItem.SubdItemCode,
                    SubdItemName = h.ItemsUom.SubdItem.ItemName,
                    UomName = h.ItemsUom.UomName,
                    OldPrice = h.OldPrice,
                    NewPrice = h.NewPrice,
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy
                })
                .OrderBy(d => d.SubdItemCode)
                .ThenBy(d => d.UomName)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<string?>> GetAllPrincipalsAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                .Select(c => c.Principal)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync();
        }
        
        public async Task<PriceIncreaseTableListDto?> GetPriceIncreaseByIdAsync(int id)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.CompanyItemPriceHistories
                .AsNoTracking()
                .Include(h => h.CompanyItem)
                .FirstOrDefaultAsync(h => h.CompanyItemPriceHistoryId == id);

            if (entity == null) return null;

            return new PriceIncreaseTableListDto
            {
                CompanyItemPriceHistoryId = entity.CompanyItemPriceHistoryId,
                CompanyItemId = entity.CompanyItemId,
                CompanyItemName = entity.CompanyItem.ItemName,
                CompanyItemCode = entity.CompanyItem.ItemCode,
                StockPrice = entity.CompanyItem.StockPrice,
                PriceIncreaseAmount = entity.PriceIncreaseAmount,
                EffectivityDate = entity.EffectivityDate,
                AppliedDate = entity.AppliedDate,
                CreatedBy = entity.CreatedBy,
                CreatedDate = entity.CreatedDate,
                Status = entity.AppliedDate != null
                    ? "Applied"
                    : (entity.EffectivityDate <= NowPh() ? "Overdue" : "Pending")
            };
        }

        public async Task<IReadOnlyList<CompanyItemDropdownItem>> GetCompanyItemsForDropdownAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.ItemName)
                .Select(c => new CompanyItemDropdownItem
                {
                    CompanyItemId = c.CompanyItemId,
                    CompanyItemCode = c.ItemCode,
                    CompanyItemName = c.ItemName,
                    StockPrice = c.StockPrice,
                    Principal = c.Principal
                })
                .ToListAsync();
        }

        public async Task<bool> HasPendingIncreaseAsync(int companyItemId, int? excludeId = null)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItemPriceHistories
                .AsNoTracking()
                .AnyAsync(h => h.CompanyItemId == companyItemId
                    && h.AppliedDate == null
                    && (!excludeId.HasValue || h.CompanyItemPriceHistoryId != excludeId.Value));
        }

        public async Task<(bool success, string? error)> ScheduleIncreaseAsync(AddPriceIncreaseDto dto)
        {
            if (!dto.CompanyItemId.HasValue || !dto.PriceIncreaseAmount.HasValue || !dto.EffectivityDate.HasValue)
                return (false, "Missing required fields.");

            await using var db = _dbFactory.CreateDbContext();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    EXEC sp_SchedulePriceIncrease
                        @CompanyItemId = {dto.CompanyItemId.Value},
                        @PriceIncreaseAmount = {dto.PriceIncreaseAmount.Value},
                        @EffectivityDate = {dto.EffectivityDate.Value},
                        @CreatedBy = {dto.CreatedBy}");
                return (true, null);
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (sqlEx.Number == 50001)
            {
                return (false, "This company item already has a pending price change scheduled.");
            }
        }

        /// <summary>
        /// Edits a not-yet-applied increase: updates the company-level history row and
        /// recomputes every linked UOM-level history row (their OldPrice snapshot is
        /// preserved; only NewPrice and EffectivityDate change).
        /// </summary>
        public async Task<(bool success, string? error)> UpdatePendingIncreaseAsync(
            int companyItemPriceHistoryId, decimal priceIncreaseAmount, DateTime effectivityDate, int? updatedBy)
        {
            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var history = await db.CompanyItemPriceHistories
                    .FirstOrDefaultAsync(h => h.CompanyItemPriceHistoryId == companyItemPriceHistoryId);

                if (history == null)
                    return (false, "Price change not found.");

                if (history.AppliedDate != null)
                    return (false, "This change has already been applied and can no longer be edited.");

                var duplicateExists = await db.CompanyItemPriceHistories
                    .AnyAsync(h => h.CompanyItemId == history.CompanyItemId
                        && h.AppliedDate == null
                        && h.CompanyItemPriceHistoryId != companyItemPriceHistoryId);

                if (duplicateExists)
                    return (false, "This company item already has another pending price change.");

                history.PriceIncreaseAmount = priceIncreaseAmount;
                history.NewPrice = history.OldPrice + priceIncreaseAmount;
                history.EffectivityDate = effectivityDate;

                var uomRows = await db.ItemsUomPriceHistories
                    .Where(u => u.CompanyItemPriceHistoryId == companyItemPriceHistoryId && u.AppliedDate == null)
                    .Include(u => u.ItemsUom)
                    .ToListAsync();

                foreach (var uom in uomRows)
                {
                    var conversion = uom.ItemsUom?.ConversionToBase ?? 1m;
                    uom.NewPrice = uom.OldPrice + (priceIncreaseAmount * conversion);
                    uom.EffectivityDate = effectivityDate;
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return (false, $"Unable to update the price change: {ex.GetBaseException().Message}");
            }
        }

        public async Task<IReadOnlyList<CompanyItemUomPriceDto>> GetUomPricesByCompanyItemIdAsync(int companyItemId)
        {
            await using var db = _dbFactory.CreateDbContext();

            var uoms = await db.ItemsUoms
                .AsNoTracking()
                .Where(u => u.SubdItem.CompanyItemId == companyItemId
                    && u.IsActive
                    && u.SubdItem.IsActive)
                .Select(u => new
                {
                    u.ItemsUomId,
                    u.SubdItemId,
                    SubdItemCode = u.SubdItem.SubdItemCode,
                    SubdItemName = u.SubdItem.ItemName,
                    u.UomName,
                    u.ConversionToBase,
                    CurrentPrice = u.Price
                })
                .ToListAsync();

            var uomIds = uoms.Select(u => u.ItemsUomId).ToList();

            // latest pending history row per UOM, if any
            var pendingHistory = await db.ItemsUomPriceHistories
                .AsNoTracking()
                .Where(h => uomIds.Contains(h.ItemsUomId) && h.AppliedDate == null)
                .GroupBy(h => h.ItemsUomId)
                .Select(g => g.OrderByDescending(h => h.EffectivityDate)
                            .ThenByDescending(h => h.ItemsUomPriceHistoryId)
                            .First())
                .ToListAsync();

            var historyByUomId = pendingHistory.ToDictionary(h => h.ItemsUomId);

            return uoms
                .Select(u =>
                {
                    historyByUomId.TryGetValue(u.ItemsUomId, out var history);
                    return new CompanyItemUomPriceDto
                    {
                        SubdItemId = u.SubdItemId,
                        SubdItemCode = u.SubdItemCode,
                        SubdItemName = u.SubdItemName,
                        ItemsUomId = u.ItemsUomId,
                        UomName = u.UomName,
                        ConversionToBase = u.ConversionToBase ?? 1m,
                        OldPrice = history?.OldPrice,
                        NewPrice = history?.NewPrice
                    };
                })
                .OrderBy(x => x.SubdItemCode)
                .ThenBy(x => x.UomName)
                .ToList();
        }

        public async Task<(bool success, string? error)> CancelPendingIncreaseAsync(int companyItemPriceHistoryId)
        {
            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var history = await db.CompanyItemPriceHistories
                    .FirstOrDefaultAsync(h => h.CompanyItemPriceHistoryId == companyItemPriceHistoryId);

                if (history == null)
                    return (false, "Price change not found.");

                if (history.AppliedDate != null)
                    return (false, "This change has already been applied and can no longer be canceled.");

                // Remove the company-level history row
                db.CompanyItemPriceHistories.Remove(history);

                // Remove all linked UOM-level history rows
                var uomRows = await db.ItemsUomPriceHistories
                    .Where(u => u.CompanyItemPriceHistoryId == companyItemPriceHistoryId && u.AppliedDate == null)
                    .ToListAsync();

                db.ItemsUomPriceHistories.RemoveRange(uomRows);

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return (false, $"Unable to cancel the price change: {ex.GetBaseException().Message}");
            }
        }
    }
}