using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.CompanyItem.DTOs;

namespace STTproject.Features.Admin.CompanyItem.Services
{
    public class AdminCompanyItemService : IAdminCompanyItemService
    {
        private readonly IDbContextFactory<EntrielContext> _dbFactory;
        private readonly IConfiguration _config;
        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);

        public AdminCompanyItemService(IDbContextFactory<EntrielContext> dbFactory, IConfiguration config)
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

        public async Task<string?> GetCompanyItemNameByIdAsync(int? companyItemId)
        {
            if (companyItemId == null) return null;
            await using var db = _dbFactory.CreateDbContext();
            var companyItem = await db.CompanyItems.FindAsync(companyItemId.Value);
            return companyItem?.ItemName;
        }

        public async Task<CompanyItemListDto?> CreateCompanyItemAsync(CompanyItemCreateDto dto, CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();

            var entity = new Data.CompanyItem
            {
                ItemCode = dto.ItemCode ?? string.Empty,
                ItemName = dto.ItemName ?? string.Empty,
                Principal = dto.Principal ?? string.Empty,
                Ppg = dto.PPG ?? string.Empty,
                Category = dto.Category ?? string.Empty,
                StockPrice = dto.StockPrice,
                StockPriceRegional = dto.StockPriceRegional, 
                StockPriceNational = dto.StockPriceNational,
                StockUomName = dto.StockUomName ?? string.Empty,
                IsActive = dto.IsActive,
                CreatedDate = NowPh(),
                CreatedBy = dto.CreatedBy,
            };
            db.CompanyItems.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return await GetCompanyItemByIdAsync(entity.CompanyItemId);
        }

        public async Task<CompanyItemUpdateDto?> UpdateCompanyItemAsync(CompanyItemUpdateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.CompanyItems.FindAsync(dto.CompanyItemId);
            if (entity == null) return null;

            entity.ItemCode = dto.ItemCode ?? entity.ItemCode;
            entity.ItemName = dto.ItemName ?? entity.ItemName;
            entity.Principal = dto.Principal ?? entity.Principal;
            entity.Ppg = dto.PPG ?? entity.Ppg;
            entity.Category = dto.Category ?? entity.Category;
            entity.StockPrice = dto.StockPrice ?? entity.StockPrice;
            entity.StockPriceRegional = dto.StockPriceRegional ?? entity.StockPriceRegional;
            entity.StockPriceNational = dto.StockPriceNational ?? entity.StockPriceNational;
            entity.StockUomName = dto.StockUomName ?? entity.StockUomName;
            entity.IsActive = dto.IsActive;
            entity.UpdatedBy = dto.UpdatedBy;
            entity.UpdatedDate = NowPh();

            await db.SaveChangesAsync();
            return dto;
        }

        public async Task ToggleCompanyItemStatusAsync(int id, bool isActive, int? updatedBy)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.CompanyItems.FindAsync(id);
            if (entity == null) return;
            entity.IsActive = isActive;
            entity.UpdatedBy = updatedBy;
            entity.UpdatedDate = NowPh();
            await db.SaveChangesAsync();
        }

        public async Task<IEnumerable<CompanyItemListDto>> GetAllAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                .Select(u => new CompanyItemListDto
                {
                    CompanyItemId = u.CompanyItemId,
                    ItemCode = u.ItemCode,
                    ItemName = u.ItemName,
                    Principal = u.Principal,
                    PPG = u.Ppg,
                    Category = u.Category,
                    StockPrice = u.StockPrice,
                    StockPriceRegional = u.StockPriceRegional,
                    StockPriceNational = u.StockPriceNational,
                    StockUomName = u.StockUomName,
                    IsActive = u.IsActive,
                    CreatedDate = u.CreatedDate,
                    UpdatedDate = u.UpdatedDate,
                    CreatedBy = u.CreatedBy,
                    UpdatedBy = u.UpdatedBy,
                }).ToListAsync();
        }

        public async Task<(IEnumerable<CompanyItemListDto> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? status,
            string? principal,
            string? ppg,
            string? category,
            string? sortColumn = "ItemCode",
            bool sortAscending = true)
        {
            await using var db = _dbFactory.CreateDbContext();

            var query = db.CompanyItems
                .AsNoTracking()
                .Where(c => string.IsNullOrEmpty(status) ||
                    (status == "active" ? c.IsActive : !c.IsActive))
                .Where(c => string.IsNullOrEmpty(principal) || c.Principal == principal)
                .Where(c => string.IsNullOrEmpty(ppg) || c.Ppg == ppg)
                .Where(c => string.IsNullOrEmpty(category) || c.Category == category)
                .Where(c => string.IsNullOrEmpty(search) ||
                    (c.ItemCode != null && c.ItemCode.Contains(search)) ||
                    (c.ItemName != null && c.ItemName.Contains(search)) ||
                    (c.Principal != null && c.Principal.Contains(search)) ||
                    (c.Ppg != null && c.Ppg.Contains(search)) ||
                    (c.Category != null && c.Category.Contains(search)) ||
                    (c.StockUomName != null && c.StockUomName.Contains(search)));

            var total = await query.CountAsync();

            query = (sortColumn, sortAscending) switch
            {
                ("ItemCode", true) => query.OrderBy(c => c.ItemCode),
                ("ItemCode", false) => query.OrderByDescending(c => c.ItemCode),
                ("ItemName", true) => query.OrderBy(c => c.ItemName),
                ("ItemName", false) => query.OrderByDescending(c => c.ItemName),
                ("Principal", true) => query.OrderBy(c => c.Principal),
                ("Principal", false) => query.OrderByDescending(c => c.Principal),
                ("PPG", true) => query.OrderBy(c => c.Ppg),
                ("PPG", false) => query.OrderByDescending(c => c.Ppg),
                ("Category", true) => query.OrderBy(c => c.Category),
                ("Category", false) => query.OrderByDescending(c => c.Category),
                ("CreatedDate", false) => query.OrderBy(c => c.CreatedDate),
                ("CreatedDate", true) => query.OrderByDescending(c => c.CreatedDate),
                ("IsActive", false) => query.OrderBy(c => c.IsActive),
                ("IsActive", true) => query.OrderByDescending(c => c.IsActive),
                _ => query.OrderBy(c => c.ItemCode)
            };

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new CompanyItemListDto
                {
                    CompanyItemId = u.CompanyItemId,
                    ItemCode = u.ItemCode,
                    ItemName = u.ItemName,
                    Principal = u.Principal,
                    PPG = u.Ppg,
                    Category = u.Category,
                    StockPrice = u.StockPrice,
                    StockPriceRegional = u.StockPriceRegional,
                    StockPriceNational = u.StockPriceNational,
                    StockUomName = u.StockUomName,
                    IsActive = u.IsActive,
                    CreatedDate = u.CreatedDate,
                    UpdatedDate = u.UpdatedDate,
                    CreatedBy = u.CreatedBy,
                    UpdatedBy = u.UpdatedBy,
                })
                .ToListAsync();

            return (items, total);
        }

        public async Task<CompanyItemListDto?> GetCompanyItemByIdAsync(int id)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.CompanyItems
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.CompanyItemId == id);

            if (entity == null) return null;

            return new CompanyItemListDto
            {
                CompanyItemId = entity.CompanyItemId,
                ItemCode = entity.ItemCode,
                ItemName = entity.ItemName,
                Principal = entity.Principal,
                PPG = entity.Ppg,
                Category = entity.Category,
                StockPrice = entity.StockPrice,
                StockPriceRegional = entity.StockPriceRegional,
                StockPriceNational = entity.StockPriceNational,
                StockUomName = entity.StockUomName,
                IsActive = entity.IsActive,
                CreatedDate = entity.CreatedDate,
                UpdatedDate = entity.UpdatedDate,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy,
            };
        }

        public async Task<IReadOnlyList<string?>> GetAllPrincipalsAsync(CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                .Select(c => c.Principal)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync(cancellationToken);
        }
        public async Task<IReadOnlyList<string?>> GetAllPPGsAsync(CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                .Select(c => c.Ppg)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync(cancellationToken);
        }
        public async Task<IReadOnlyList<string?>> GetAllCategoriesAsync(CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                .Select(c => c.Category)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> CompanyItemExistsAsync(string itemCode, string itemName, int? excludeId = null)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                .AnyAsync(c => c.ItemCode == itemCode && c.ItemName == itemName && c.CompanyItemId != excludeId);
        }

        public async Task<bool> ItemCodeExistsAsync(string itemCode, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems
                 .AnyAsync(c => c.ItemCode == itemCode && c.CompanyItemId != excludeId, cancellationToken);
        }

        public async Task<IEnumerable<CompanyItemPriceHistoryDto>> GetPriceHistoryByCompanyItemIdAsync(int companyItemId)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItemPriceHistories
                .Where(h => h.CompanyItemId == companyItemId)
                .OrderByDescending(h => h.EffectivityDate)
                .Select(h => new CompanyItemPriceHistoryDto
                {
                    CompanyItemPriceHistoryId = h.CompanyItemPriceHistoryId,
                    CompanyItemId = h.CompanyItemId,
                    OldPrice = h.OldPrice,
                    NewPrice = h.NewPrice,
                    EffectivityDate = h.EffectivityDate,
                    CreatedDate = h.CreatedDate,
                    CreatedBy = h.CreatedBy,
                })
                .ToListAsync();
        }

        public async Task AddInitialPriceHistoryAsync(
            int companyItemId, decimal price, int userId,
            string? priceListType = null, CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            var now = NowPh();
            db.CompanyItemPriceHistories.Add(new Data.CompanyItemPriceHistory
            {
                CompanyItemId = companyItemId,
                PriceListType = priceListType,
                OldPrice = 0m,
                NewPrice = price,
                PriceIncreaseAmount = price,
                EffectivityDate = now,
                AppliedDate = now,
                CreatedDate = now,
                CreatedBy = userId > 0 ? userId : null,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task<CompanyItemListDto?> GetByItemCodeAsync(string itemCode, CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.CompanyItems
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ItemCode == itemCode, cancellationToken);

            if (entity == null) return null;

            return new CompanyItemListDto
            {
                CompanyItemId = entity.CompanyItemId,
                ItemCode = entity.ItemCode,
                ItemName = entity.ItemName,
                Principal = entity.Principal,
                PPG = entity.Ppg,
                Category = entity.Category,
                StockPrice = entity.StockPrice,
                StockPriceRegional = entity.StockPriceRegional,
                StockPriceNational = entity.StockPriceNational,
                StockUomName = entity.StockUomName,
                IsActive = entity.IsActive,
                CreatedDate = entity.CreatedDate,
                UpdatedDate = entity.UpdatedDate,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy,
            };
        }

        public async Task<bool> UpdateStockPricesAsync(
            int companyItemId, decimal? stock, decimal? regional, decimal? national,
            int? updatedBy, CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.CompanyItems.FindAsync(new object?[] { companyItemId }, cancellationToken);
            if (entity == null) return false;

            var now = NowPh();

            void Record(string? type, decimal? oldPrice, decimal newPrice) =>
                db.CompanyItemPriceHistories.Add(new Data.CompanyItemPriceHistory
                {
                    CompanyItemId = companyItemId,
                    PriceListType = type,
                    OldPrice = oldPrice ?? 0m,
                    NewPrice = newPrice,
                    PriceIncreaseAmount = newPrice - (oldPrice ?? 0m),
                    EffectivityDate = now,
                    AppliedDate = now,         
                    CreatedDate = now,
                    CreatedBy = updatedBy
                });

            if (stock.HasValue && stock != entity.StockPrice)
            {
                Record(null, entity.StockPrice, stock.Value);
                entity.StockPrice = stock;
            }
            if (regional.HasValue && regional != entity.StockPriceRegional)
            {
                Record("Regional", entity.StockPriceRegional, regional.Value);
                entity.StockPriceRegional = regional;
            }
            if (national.HasValue && national != entity.StockPriceNational)
            {
                Record("National", entity.StockPriceNational, national.Value);
                entity.StockPriceNational = national;
            }

            entity.UpdatedBy = updatedBy;
            entity.UpdatedDate = now;

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

            public async Task<IReadOnlyList<ExistingPriceHistoryRow>> GetPriceHistoryRowsAsync(
            int companyItemId, CancellationToken cancellationToken = default)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItemPriceHistories
                .AsNoTracking()
                .Where(h => h.CompanyItemId == companyItemId)
                .OrderBy(h => h.EffectivityDate)
                .Select(h => new ExistingPriceHistoryRow(
                    h.CompanyItemPriceHistoryId,
                    h.PriceListType,
                    h.EffectivityDate,
                    h.OldPrice,
                    h.NewPrice,
                    h.AppliedDate))
                .ToListAsync(cancellationToken);
        }
 
        // Writes historical / scheduled prices for ONE item in a single transaction.
        //  - Rows dated today or earlier are recorded as already applied.
        //  - Rows dated in the future are recorded as NOT applied, so the scheduled-price
        //    worker applies them on their date instead of them taking effect now.
        //  - The Old/New/Amount chain is rebuilt in date order for each price list.
        //  - The item's current price only changes when a file row is the newest applied price.
        //  - Re-running the same file inserts nothing (same date + same price is skipped).
        //  - A different price on a date that is already recorded, or a pending scheduled
        //    change on the same price list, cancels the whole item (nothing is saved).
        public async Task<HistoricalPriceApplyResult> ApplyHistoricalPricesAsync(
            int companyItemId, IReadOnlyList<HistoricalPriceRow> entries, int userId,
            CancellationToken cancellationToken = default)
        {
            if (entries.Count == 0)
                return new HistoricalPriceApplyResult(true, null, 0, 0);

            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var item = await db.CompanyItems
                .FirstOrDefaultAsync(c => c.CompanyItemId == companyItemId, cancellationToken);
            if (item == null)
                return new HistoricalPriceApplyResult(false, "Company item not found.", 0, 0);

            var history = await db.CompanyItemPriceHistories
                .Where(h => h.CompanyItemId == companyItemId)
                .ToListAsync(cancellationToken);

            var result = ApplyHistoricalCore(db, item, history, entries, userId);
            if (!result.Success)
                return result; // the transaction is never committed, so nothing is saved

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return result;
        }

        // Creates a company item that does not exist yet AND writes its prices in ONE transaction,
        // so a failed price insert never leaves a half-configured item behind. The item's current
        // price comes from the newest price dated today or earlier; later dates stay scheduled.
        public async Task<(HistoricalPriceApplyResult Result, int? CompanyItemId)> CreateItemWithHistoricalPricesAsync(
            CompanyItemCreateDto dto, IReadOnlyList<HistoricalPriceRow> entries, int userId,
            CancellationToken cancellationToken = default)
        {
            if (entries.Count == 0)
                return (new HistoricalPriceApplyResult(false, "There are no prices to import for this item.", 0, 0), null);

            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

            var item = new Data.CompanyItem
            {
                ItemCode = dto.ItemCode ?? string.Empty,
                ItemName = dto.ItemName ?? string.Empty,
                Principal = dto.Principal ?? string.Empty,
                Ppg = dto.PPG ?? string.Empty,
                Category = dto.Category ?? string.Empty,
                StockUomName = dto.StockUomName ?? string.Empty,
                IsActive = dto.IsActive,
                CreatedDate = NowPh(),
                CreatedBy = dto.CreatedBy,
            };
            db.CompanyItems.Add(item);
            await db.SaveChangesAsync(cancellationToken); // the price rows need the new item's id

            var result = ApplyHistoricalCore(db, item, new List<Data.CompanyItemPriceHistory>(), entries, userId);
            if (!result.Success)
                return (result, null); // rolled back, the item is not kept

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return (result, item.CompanyItemId);
        }

        // Shared by both methods above. Adds the price rows and moves the item's live price; the caller saves.
        private static HistoricalPriceApplyResult ApplyHistoricalCore(
            EntrielContext db, Data.CompanyItem item, List<Data.CompanyItemPriceHistory> history,
            IReadOnlyList<HistoricalPriceRow> entries, int userId)
        {
            var now = NowPh();
            var today = now.Date;
            var inserted = 0;
            var skipped = 0;

            static bool SameType(string? a, string? b) =>
                string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            foreach (var typeGroup in entries.GroupBy(e => e.PriceListType ?? string.Empty))
            {
                var type = typeGroup.First().PriceListType;
                var existing = history.Where(h => SameType(h.PriceListType, type)).ToList();

                if (existing.Any(h => h.AppliedDate == null))
                {
                    return new HistoricalPriceApplyResult(false,
                        "This item has a scheduled price change that has not taken effect yet. Resolve it before importing price history.",
                        0, 0);
                }

                var timeline = existing.Select(h => (Row: h, IsNew: false)).ToList();

                foreach (var entry in typeGroup.OrderBy(e => e.EffectivityDate))
                {
                    var date = entry.EffectivityDate.Date;
                    var clash = existing.FirstOrDefault(h => h.EffectivityDate.Date == date);

                    if (clash != null)
                    {
                        if (clash.NewPrice == entry.NewPrice) { skipped++; continue; }

                        return new HistoricalPriceApplyResult(false,
                            $"A different price ({clash.NewPrice:N2}) is already recorded for {date:yyyy-MM-dd}.", 0, 0);
                    }

                    var row = new Data.CompanyItemPriceHistory
                    {
                        CompanyItemId = item.CompanyItemId,
                        PriceListType = type,
                        NewPrice = entry.NewPrice,
                        EffectivityDate = date,
                        AppliedDate = date <= today ? date : null,   // future = still scheduled
                        CreatedDate = now,
                        CreatedBy = userId > 0 ? userId : null
                    };

                    db.CompanyItemPriceHistories.Add(row);
                    timeline.Add((row, true));
                    inserted++;
                }

                // Rebuild Old / Amount in date order. Only imported rows, and the existing row that
                // directly follows an imported one, are touched; other existing rows stay as they are.
                var ordered = timeline.OrderBy(t => t.Row.EffectivityDate).ToList();
                decimal previous = 0m;

                for (var i = 0; i < ordered.Count; i++)
                {
                    var (row, isNew) = ordered[i];
                    var followsImported = i > 0 && ordered[i - 1].IsNew;

                    if (isNew || followsImported)
                    {
                        row.OldPrice = previous;
                        row.PriceIncreaseAmount = row.NewPrice - previous;
                    }

                    previous = row.NewPrice;
                }

                // The live price only moves when an imported row is the newest applied price.
                var latestApplied = ordered.LastOrDefault(t => t.Row.AppliedDate != null);
                if (latestApplied.Row != null && latestApplied.IsNew)
                {
                    var price = latestApplied.Row.NewPrice;

                    if (string.IsNullOrEmpty(type)) item.StockPrice = price;
                    else if (SameType(type, "Regional")) item.StockPriceRegional = price;
                    else if (SameType(type, "National")) item.StockPriceNational = price;

                    item.UpdatedBy = userId > 0 ? userId : null;
                    item.UpdatedDate = now;
                }
            }

            return new HistoricalPriceApplyResult(true, null, inserted, skipped);
        }
 

    }
}