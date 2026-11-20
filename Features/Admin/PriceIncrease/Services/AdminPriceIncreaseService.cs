using System.Data;
using STTproject.Features.Admin.PriceIncrease.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using STTproject.Data;
using STTproject.Features.Admin.Subdistributor.DTOs;

namespace STTproject.Features.Admin.PriceIncrease.Services
{
    public class AdminPriceIncreaseService : IAdminPriceIncreaseService
    {
        private readonly IDbContextFactory<EntrielContext> _dbFactory;
        private readonly IConfiguration _config;

        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);

        public AdminPriceIncreaseService(IDbContextFactory<EntrielContext> dbFactory, IConfiguration config)
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
                    PriceListType = h.PriceListType,
                    StockPrice = h.PriceListType == "Regional" ? h.CompanyItem.StockPriceRegional
                            : h.PriceListType == "National" ? h.CompanyItem.StockPriceNational
                            : h.CompanyItem.StockPrice,
                    PriceIncreaseAmount = h.PriceIncreaseAmount,
                    EffectivityDate = h.EffectivityDate,
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy,
                    CreatedDate = h.CreatedDate,
                    Principal = h.CompanyItem.Principal,
                    StockUomName = h.CompanyItem.StockUomName,
                    OldPrice = h.OldPrice,
                    NewPrice = h.NewPrice,
                    Status = h.AppliedDate != null
                        ? "Applied"
                        : (h.EffectivityDate <= now ? "Overdue" : "Pending")
                })
                .OrderByDescending(x => x.CreatedDate)
                .ToListAsync();
        }
        public async Task<IReadOnlyList<string>> GetSubDistributorsWithoutPriceListAsync(int companyItemId)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.SubdItems.AsNoTracking()
                .Where(si => si.CompanyItemId == companyItemId
                        && si.IsActive
                        && si.SubDistributor.PriceListType == null
                        && si.ItemsUoms.Any(u => u.IsActive))
                .Select(si => si.SubDistributor.SubdName)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();
        }
        public async Task<(IEnumerable<PriceIncreaseTableListDto> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? status,
            string? principal,
            string? sortColumn = "CreatedDate",
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
                    (h.CompanyItem.Ppg != null && h.CompanyItem.Ppg.Contains(search)) ||
                    (h.CompanyItem.SubdItems.Any(si => si.SubdItemCode.Contains(search) || si.ItemName.Contains(search))));

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
                ("PriceIncreaseAmount", false) => query.OrderBy(h => h.PriceIncreaseAmount),
                ("PriceIncreaseAmount", true) => query.OrderByDescending(h => h.PriceIncreaseAmount),
                ("EffectivityDate", false) => query.OrderBy(h => h.EffectivityDate),
                ("EffectivityDate", true) => query.OrderByDescending(h => h.EffectivityDate),
                ("CreatedDate", false) => query.OrderBy(h => h.CreatedDate),
                ("CreatedDate", true) => query.OrderByDescending(h => h.CreatedDate),
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
                    PriceListType = h.PriceListType,
                    StockPrice = h.PriceListType == "Regional" ? h.CompanyItem.StockPriceRegional
                            : h.PriceListType == "National" ? h.CompanyItem.StockPriceNational
                            : h.CompanyItem.StockPrice,
                    PriceIncreaseAmount = h.PriceIncreaseAmount,
                    EffectivityDate = h.EffectivityDate,
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy,
                    CreatedDate = h.CreatedDate,
                    Principal = h.CompanyItem.Principal,
                    StockUomName = h.CompanyItem.StockUomName,
                    OldPrice = h.OldPrice,
                    NewPrice = h.NewPrice,
                    Status = h.AppliedDate != null
                        ? "Applied"
                        : (h.EffectivityDate <= now ? "Overdue" : "Pending")
                })
                .ToListAsync();

            return (items, total);
        }

        public async Task<(IEnumerable<GroupedPriceIncreaseDto> Items, int TotalCount)> GetGroupedPagedAsync(int page, int pageSize, string? search, string? status, string? principal, string? sortColumn = "CreatedDate", bool sortAscending = true)
        {
            var allRecords = (await GetAllAsync()).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(principal))
                allRecords = allRecords.Where(x => x.Principal == principal);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                allRecords = allRecords.Where(x =>
                    (x.CompanyItemCode?.Contains(
                        term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.CompanyItemName?.Contains(
                        term, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                allRecords = allRecords.Where(x =>
                    string.Equals(
                        x.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            var grouped = allRecords
                .GroupBy(x => x.GroupKey)
                .Select(g => new GroupedPriceIncreaseDto
                {
                    GroupKey = g.Key,
                    CompanyItemId = g.First().CompanyItemId,
                    CompanyItemCode = g.First().CompanyItemCode,
                    CompanyItemName = g.First().CompanyItemName,
                    Principal = g.First().Principal,
                    CreatedDate = g.Max(x => x.CreatedDate),

                    Schedules = g
                        .OrderBy(x =>
                            x.PriceListType == "Regional" ? 0 :
                            x.PriceListType == "National" ? 1 : 2)
                        .ThenByDescending(x => x.CreatedDate)
                        .ToList()
                });


            grouped = (sortColumn, sortAscending) switch
            {
                ("CompanyItemCode", true) =>
                    grouped.OrderBy(x => x.CompanyItemCode),

                ("CompanyItemCode", false) =>
                    grouped.OrderByDescending(x => x.CompanyItemCode),

                ("CompanyItemName", true) =>
                    grouped.OrderBy(x => x.CompanyItemName),

                ("CompanyItemName", false) =>
                    grouped.OrderByDescending(x => x.CompanyItemName),

                ("CreatedDate", false) =>
                    grouped.OrderBy(x => x.CreatedDate),

                ("CreatedDate", true) =>
                    grouped.OrderByDescending(x => x.CreatedDate),

                ("PriceIncreaseAmount", true) =>
                    grouped.OrderBy(x =>
                        x.Schedules.Min(s => s.PriceIncreaseAmount)),

                ("PriceIncreaseAmount", false) =>
                    grouped.OrderByDescending(x =>
                        x.Schedules.Max(s => s.PriceIncreaseAmount)),

                ("EffectivityDate", false) =>
                    grouped.OrderBy(x =>
                        x.Schedules.Min(s => s.EffectivityDate)),

                ("EffectivityDate", true) =>
                    grouped.OrderByDescending(x =>
                        x.Schedules.Max(s => s.EffectivityDate)),

                ("Status", true) =>
                    grouped.OrderBy(x =>
                        x.Schedules.First().Status),

                ("Status", false) =>
                    grouped.OrderByDescending(x =>
                        x.Schedules.First().Status),

                _ => grouped.OrderByDescending(x => x.CreatedDate)
            };

            var result = grouped.ToList();
            var total = result.Count;

            var items = result
                .Skip(Math.Max(0, page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return (items, total);
        }

        /// <summary>
        /// "By company item" view: one row per company item that has price changes, filtered and sorted in the
        /// database. Status filter keeps items that have at least one change with that status.
        /// </summary>
        public async Task<(IEnumerable<CompanyItemPriceSummaryDto> Items, int TotalCount)> GetCompanyItemSummaryPagedAsync(
            int page, int pageSize, string? search, string? status, string? principal,
            string? sortColumn = "CreatedDate", bool sortAscending = false)
        {
            await using var db = _dbFactory.CreateDbContext();
            var now = NowPh();

            var items = db.CompanyItems.AsNoTracking().Where(c => c.CompanyItemPriceHistories.Any());

            if (!string.IsNullOrWhiteSpace(principal))
                items = items.Where(c => c.Principal == principal);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                items = items.Where(c => c.ItemCode.Contains(term) || c.ItemName.Contains(term));
            }

            items = status?.ToLowerInvariant() switch
            {
                "pending" => items.Where(c => c.CompanyItemPriceHistories.Any(h => h.AppliedDate == null && h.EffectivityDate > now)),
                "overdue" => items.Where(c => c.CompanyItemPriceHistories.Any(h => h.AppliedDate == null && h.EffectivityDate <= now)),
                "applied" => items.Where(c => c.CompanyItemPriceHistories.Any(h => h.AppliedDate != null)),
                _ => items
            };

            var total = await items.CountAsync();

            var projected = items.Select(c => new CompanyItemPriceSummaryDto
            {
                CompanyItemId = c.CompanyItemId,
                CompanyItemCode = c.ItemCode,
                CompanyItemName = c.ItemName,
                Principal = c.Principal,
                StockUomName = c.StockUomName,
                StockPrice = c.StockPrice,
                StockPriceRegional = c.StockPriceRegional,
                StockPriceNational = c.StockPriceNational,
                ChangeCount = c.CompanyItemPriceHistories.Count(),
                PendingCount = c.CompanyItemPriceHistories.Count(h => h.AppliedDate == null),
                NextEffectivityDate = c.CompanyItemPriceHistories.Where(h => h.AppliedDate == null)
                    .Min(h => (DateTime?)h.EffectivityDate),
                LastEffectivityDate = c.CompanyItemPriceHistories.Where(h => h.AppliedDate != null)
                    .Max(h => (DateTime?)h.EffectivityDate),
                LastCreatedDate = c.CompanyItemPriceHistories.Max(h => h.CreatedDate)
            });

            projected = (sortColumn, sortAscending) switch
            {
                ("CompanyItemCode", true) => projected.OrderBy(x => x.CompanyItemCode),
                ("CompanyItemCode", false) => projected.OrderByDescending(x => x.CompanyItemCode),
                ("EffectivityDate", true) => projected.OrderBy(x => x.NextEffectivityDate ?? x.LastEffectivityDate),
                ("EffectivityDate", false) => projected.OrderByDescending(x => x.NextEffectivityDate ?? x.LastEffectivityDate),
                ("Status", true) => projected.OrderBy(x => x.PendingCount),
                ("Status", false) => projected.OrderByDescending(x => x.PendingCount),
                ("CreatedDate", true) => projected.OrderBy(x => x.LastCreatedDate),
                _ => projected.OrderByDescending(x => x.LastCreatedDate)
            };

            var list = await projected
                .Skip(Math.Max(0, page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (list, total);
        }

        public async Task<CompanyItemPriceSummaryDto?> GetCompanyItemSummaryAsync(int companyItemId)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItems.AsNoTracking()
                .Where(c => c.CompanyItemId == companyItemId)
                .Select(c => new CompanyItemPriceSummaryDto
                {
                    CompanyItemId = c.CompanyItemId,
                    CompanyItemCode = c.ItemCode,
                    CompanyItemName = c.ItemName,
                    Principal = c.Principal,
                    StockUomName = c.StockUomName,
                    StockPrice = c.StockPrice,
                    StockPriceRegional = c.StockPriceRegional,
                    StockPriceNational = c.StockPriceNational,
                    ChangeCount = c.CompanyItemPriceHistories.Count(),
                    PendingCount = c.CompanyItemPriceHistories.Count(h => h.AppliedDate == null),
                    NextEffectivityDate = c.CompanyItemPriceHistories.Where(h => h.AppliedDate == null)
                        .Min(h => (DateTime?)h.EffectivityDate),
                    LastEffectivityDate = c.CompanyItemPriceHistories.Where(h => h.AppliedDate != null)
                        .Max(h => (DateTime?)h.EffectivityDate),
                    LastCreatedDate = c.CompanyItemPriceHistories.Max(h => (DateTime?)h.CreatedDate) ?? new DateTime(1900, 1, 1)
                })
                .FirstOrDefaultAsync();
        }

        /// <summary>Every price change of one company item, newest first (all price lists).</summary>
        public async Task<IReadOnlyList<PriceIncreaseTableListDto>> GetCompanyItemHistoryAsync(int companyItemId)
        {
            await using var db = _dbFactory.CreateDbContext();
            var now = NowPh();

            return await db.CompanyItemPriceHistories.AsNoTracking()
                .Where(h => h.CompanyItemId == companyItemId)
                .OrderByDescending(h => h.EffectivityDate)
                .ThenByDescending(h => h.CompanyItemPriceHistoryId)
                .Select(h => new PriceIncreaseTableListDto
                {
                    CompanyItemPriceHistoryId = h.CompanyItemPriceHistoryId,
                    CompanyItemId = h.CompanyItemId,
                    CompanyItemCode = h.CompanyItem.ItemCode,
                    CompanyItemName = h.CompanyItem.ItemName,
                    Principal = h.CompanyItem.Principal,
                    StockUomName = h.CompanyItem.StockUomName,
                    PriceListType = h.PriceListType,
                    PriceIncreaseAmount = h.PriceIncreaseAmount,
                    OldPrice = h.OldPrice,
                    NewPrice = h.NewPrice,
                    EffectivityDate = h.EffectivityDate,
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy,
                    CreatedDate = h.CreatedDate,
                    Status = h.AppliedDate != null ? "Applied" : (h.EffectivityDate <= now ? "Overdue" : "Pending")
                })
                .ToListAsync();
        }

        /// <summary>
        /// What a price change means per sub-item UOM: old/new price = the change's old/new company price ×
        /// conversion, for the sub-items on that price list. Worked out live (UOM prices are not stored).
        /// </summary>
        public async Task<IReadOnlyList<PriceIncreaseViewDto>> GetCascadedUomDetailsAsync(int companyItemPriceHistoryId)
        {
            await using var db = _dbFactory.CreateDbContext();

            var h = await db.CompanyItemPriceHistories.AsNoTracking()
                .Where(x => x.CompanyItemPriceHistoryId == companyItemPriceHistoryId)
                .Select(x => new { x.CompanyItemId, x.PriceListType, x.OldPrice, x.NewPrice, x.PriceIncreaseAmount, x.AppliedDate, x.CreatedBy })
                .FirstOrDefaultAsync();
            if (h == null) return new List<PriceIncreaseViewDto>();

            var query = db.ItemsUoms.AsNoTracking()
                .Where(u => u.SubdItem.CompanyItemId == h.CompanyItemId && u.IsActive && u.SubdItem.IsActive);
            if (h.PriceListType != null)
                query = query.Where(u => u.SubdItem.SubDistributor.PriceListType == h.PriceListType);

            var uoms = await query
                .Select(u => new
                {
                    u.SubdItemId,
                    u.SubdItem.SubdItemCode,
                    SubdItemName = u.SubdItem.ItemName,
                    u.UomName,
                    u.ConversionToBase
                })
                .ToListAsync();

            static decimal? Times(decimal price, decimal? conv) =>
                conv is decimal c && c > 0 ? Math.Round(price * c, 2, MidpointRounding.AwayFromZero) : null;

            return uoms
                .Select(u => new PriceIncreaseViewDto
                {
                    SubdItemId = u.SubdItemId,
                    SubdItemCode = u.SubdItemCode,
                    SubdItemName = u.SubdItemName,
                    UomName = u.UomName,
                    ConversionToBase = u.ConversionToBase,
                    PriceListType = h.PriceListType,
                    OldPrice = Times(h.OldPrice, u.ConversionToBase),
                    NewPrice = Times(h.NewPrice, u.ConversionToBase),
                    PriceIncreaseAmount = Times(h.PriceIncreaseAmount, u.ConversionToBase),
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy
                })
                .OrderBy(x => x.SubdItemCode)
                .ThenBy(x => x.SubdItemId)
                .ThenByDescending(x => x.ConversionToBase)
                .ThenBy(x => x.UomName)
                .ToList();
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
                PriceListType = entity.PriceListType,
                StockPrice = entity.PriceListType switch
                {
                    PriceListTypes.Regional => entity.CompanyItem.StockPriceRegional,
                    PriceListTypes.National => entity.CompanyItem.StockPriceNational,
                    _ => entity.CompanyItem.StockPrice
                },
                PriceIncreaseAmount = entity.PriceIncreaseAmount,
                Principal = entity.CompanyItem.Principal,
                StockUomName = entity.CompanyItem.StockUomName,
                OldPrice = entity.OldPrice,
                NewPrice = entity.NewPrice,
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
                    Principal = c.Principal,
                    StockPriceRegional = c.StockPriceRegional,
                    StockPriceNational = c.StockPriceNational,
                    StockUomName = c.StockUomName,
                })
                .ToListAsync();
        }

        public async Task<bool> HasPendingIncreaseAsync(int companyItemId, string? priceListType = null, int? excludeId = null)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.CompanyItemPriceHistories
                .AsNoTracking()
                .AnyAsync(h => h.CompanyItemId == companyItemId
                    && h.PriceListType == priceListType
                    && h.AppliedDate == null
                    && (!excludeId.HasValue || h.CompanyItemPriceHistoryId != excludeId.Value));
        }

        public async Task<(bool success, string? error)> ScheduleIncreaseAsync(
            AddPriceIncreaseDto dto)
        {
            if (!dto.CompanyItemId.HasValue)
                return (false, "Missing required fields.");

            // Build lines first so legacy DTOs and the new multi-line DTO both work.
            var lines = dto.Lines?.Count > 0
                ? dto.Lines
                : dto.PriceIncreaseAmount.HasValue
                    ? new List<PriceIncreaseLineDto>
                    {
                        new()
                        {
                            PriceListType = string.IsNullOrWhiteSpace(dto.PriceListType)
                                ? null
                                : dto.PriceListType.Trim(),
                            Amount = dto.PriceIncreaseAmount.Value,
                            EffectivityDate = dto.EffectivityDate
                        }
                    }
                    : new List<PriceIncreaseLineDto>();

            if (lines.Count == 0)
                return (false, "Enter a price change amount.");

            // Resolve each line's date using the DTO-level date as a fallback.
            if (lines.Any(l => (l.EffectivityDate ?? dto.EffectivityDate) is null))
                return (false, "Effectivity date is required.");

            if (lines.Any(l => l.Amount == 0))
                return (false, "A price change amount can't be zero.");

            // Normalize price-list names before validation.
            foreach (var line in lines)
            {
                line.PriceListType = string.IsNullOrWhiteSpace(line.PriceListType)
                    ? null
                    : line.PriceListType.Trim();
            }

            if (lines.Any(l =>
                l.PriceListType != null &&
                !PriceListTypes.All.Contains(l.PriceListType)))
            {
                return (false, "Invalid price list.");
            }

            if (lines.GroupBy(l => l.PriceListType).Any(g => g.Count() > 1))
                return (false, "Each price list can only appear once.");

            await using var db = _dbFactory.CreateDbContext();

            // Determine the item's pricing structure.
            var prices = await db.CompanyItems
                .AsNoTracking()
                .Where(c => c.CompanyItemId == dto.CompanyItemId.Value)
                .Select(c => new
                {
                    c.StockPrice,
                    c.StockPriceRegional,
                    c.StockPriceNational
                })
                .FirstOrDefaultAsync();

            if (prices == null)
                return (false, "Company item not found.");

            var structure = PricingStructureResolver.Resolve(
                prices.StockPrice,
                prices.StockPriceRegional,
                prices.StockPriceNational);

            switch (structure)
            {
                case PricingStructure.Invalid:
                case PricingStructure.Incomplete:
                    return (false, PricingStructureResolver.Describe(structure));

                case PricingStructure.Single:
                    if (lines.Count != 1 || lines[0].PriceListType != null)
                    {
                        return (
                            false,
                            "This item uses a single stock price. Schedule one change without a price list.");
                    }
                    break;

                case PricingStructure.Dual:
                    if (lines.Any(l => l.PriceListType == null))
                    {
                        return (
                            false,
                            "This item uses Regional and National prices. Choose a price list for each change.");
                    }
                    break;
            }

            // Only the company price is scheduled. UOM prices are company price × conversion, worked out when
            // invoices are priced, so there is no UOM history to write.
            var connection = (SqlConnection)db.Database.GetDbConnection();
            var wasClosed = connection.State != ConnectionState.Open;

            if (wasClosed)
                await connection.OpenAsync();

            SqlTransaction? tx = null;
            string? currentPriceList = null;

            async Task RollbackQuietly()
            {
                try
                {
                    if (tx != null)
                        await tx.RollbackAsync();
                }
                catch
                {
                    // Preserve the original database error.
                }
            }

            try
            {
                tx = (SqlTransaction)await connection.BeginTransactionAsync();

                foreach (var line in lines)
                {
                    currentPriceList = line.PriceListType;

                    await using var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = "dbo.sp_SchedulePriceIncrease";

                    cmd.Parameters.Add(
                        new SqlParameter("@CompanyItemId", dto.CompanyItemId.Value));

                    cmd.Parameters.Add(
                        new SqlParameter("@PriceIncreaseAmount", line.Amount));

                    // Use the line's date first, falling back to the DTO-level date.
                    var effectiveDate = line.EffectivityDate ?? dto.EffectivityDate;

                    if (!effectiveDate.HasValue)
                        throw new InvalidOperationException(
                            "Effectivity date is required.");

                    cmd.Parameters.Add(
                        new SqlParameter("@EffectivityDate", effectiveDate.Value));

                    cmd.Parameters.Add(
                        new SqlParameter("@CreatedBy", (object?)dto.CreatedBy ?? DBNull.Value));

                    cmd.Parameters.Add(
                        new SqlParameter("@PriceListType", SqlDbType.VarChar, 20)
                        {
                            Value = (object?)line.PriceListType ?? DBNull.Value
                        });

                    // @UomOverrides (table-valued) is left out: it is optional and ignored by the procedure.

                    await cmd.ExecuteNonQueryAsync();
                }

                // Both price-list schedules succeed together, or neither is saved.
                await tx.CommitAsync();

                return (true, null);
            }
            catch (SqlException sqlEx)
                when (sqlEx.Number is >= 50001 and <= 50009)
            {
                await RollbackQuietly();

                var label = currentPriceList ?? "Stock";

                return sqlEx.Number switch
                {
                    50001 => (
                        false,
                        $"Price increase already pending: a {label} price change is already scheduled for this item. Cancel or wait for it to apply before scheduling another."),

                    50002 => (
                        false,
                        $"The {label} price isn't configured for this item."),

                    50003 => (
                        false,
                        "The price increase schedule could not be created because a required record or configuration is missing."),


                    50005 => (
                        false,
                        $"The {label} change would make a price negative."),

                    50006 => (
                        false,
                        "The price list doesn't match this item's pricing structure."),

                    50007 => (false, "Enter a price change amount."),

                    50008 => (false, "Effectivity date is required."),

                    50009 => (false, "A new price change must start today or later. To change past prices, edit an applied change."),

                    _ => (false, "Invalid price list.")
                };
            }
            catch (SqlException sqlEx)
                when (sqlEx.Number is 2601 or 2627)
            {
                await RollbackQuietly();

                return (
                    false,
                    $"Price increase already pending: a {currentPriceList ?? "Stock"} price change is already scheduled for this item.");
            }
            catch (Exception)
            {
                await RollbackQuietly();

                // Log the exception in your application's normal logging system.
                return (
                    false,
                    "The price increase could not be scheduled. No changes were saved.");
            }
            finally
            {
                if (tx != null)
                    await tx.DisposeAsync();

                if (wasClosed)
                    await connection.CloseAsync();
            }
        }

        public async Task<bool> HasPendingIncreaseOnDateAsync(
            int companyItemId,
            string? priceListType,
            DateTime effectivityDate)
        {
            await using var db = _dbFactory.CreateDbContext();

            var start = effectivityDate.Date;
            var end = start.AddDays(1);

            return await db.CompanyItemPriceHistories
                .AsNoTracking()
                .AnyAsync(h =>
                    h.CompanyItemId == companyItemId
                    && h.PriceListType == priceListType
                    && h.AppliedDate == null
                    && h.EffectivityDate >= start
                    && h.EffectivityDate < end);
        }

        /// <summary>
        /// Edits a price change, pending or applied (admin). The item's price chain for that price list is rebuilt
        /// afterwards (each row's old price = the previous row's new price) and the company item's current price is
        /// re-synced to the latest applied row. Moving an applied row into the future makes it pending again.
        /// </summary>
        public async Task<(bool success, string? error)> UpdatePriceChangeAsync(
            int companyItemPriceHistoryId, decimal priceIncreaseAmount, DateTime effectivityDate, int? updatedBy)
        {
            if (priceIncreaseAmount == 0)
                return (false, "Amount cannot be zero.");

            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var history = await db.CompanyItemPriceHistories
                    .FirstOrDefaultAsync(h => h.CompanyItemPriceHistoryId == companyItemPriceHistoryId);

                if (history == null)
                    return (false, "Price change not found.");

                var start = effectivityDate.Date;
                var end = start.AddDays(1);

                var duplicateExists = await db.CompanyItemPriceHistories
                    .AnyAsync(h =>
                        h.CompanyItemId == history.CompanyItemId
                        && h.CompanyItemPriceHistoryId != companyItemPriceHistoryId
                        && h.PriceListType == history.PriceListType
                        && h.EffectivityDate >= start
                        && h.EffectivityDate < end);

                if (duplicateExists)
                    return (false, "This item already has a price change for that price list on that date.");

                var basePrice = await ChainBasePriceAsync(db, history.CompanyItemId, history.PriceListType);

                history.PriceIncreaseAmount = priceIncreaseAmount;
                history.EffectivityDate = effectivityDate;

                // An applied change moved into the future is pending again; it applies on its new date.
                if (history.AppliedDate != null && effectivityDate > NowPh())
                    history.AppliedDate = null;

                await db.SaveChangesAsync();

                var chainError = await RechainAsync(db, history.CompanyItemId, history.PriceListType, basePrice, updatedBy);
                if (chainError != null)
                {
                    await tx.RollbackAsync();
                    return (false, chainError);
                }

                await tx.CommitAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return (false, $"Unable to update the price change: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>Kept for callers of the old name.</summary>
        public Task<(bool success, string? error)> UpdatePendingIncreaseAsync(
            int companyItemPriceHistoryId, decimal priceIncreaseAmount, DateTime effectivityDate, int? updatedBy) =>
            UpdatePriceChangeAsync(companyItemPriceHistoryId, priceIncreaseAmount, effectivityDate, updatedBy);

        /// <summary>Invoice lines priced from this price change (they keep their amount until recalculated).</summary>
        public async Task<int> CountInvoiceLinesUsingAsync(int companyItemPriceHistoryId)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.SalesInvoiceItems.AsNoTracking()
                .CountAsync(i => i.CompanyItemPriceHistoryId == companyItemPriceHistoryId);
        }

        /// <summary>The price before the item's first change on this price list (the start of the chain).</summary>
        private static async Task<decimal?> ChainBasePriceAsync(EntrielContext db, int companyItemId, string? priceListType)
        {
            return await db.CompanyItemPriceHistories
                .Where(h => h.CompanyItemId == companyItemId && h.PriceListType == priceListType)
                .OrderBy(h => h.EffectivityDate)
                .ThenBy(h => h.CompanyItemPriceHistoryId)
                .Select(h => (decimal?)h.OldPrice)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Rebuilds OldPrice/NewPrice for every change of the item on this price list, in date order, from
        /// <paramref name="basePrice"/>; then sets the company item's price for that list to the latest applied
        /// change (or the base price when none is applied). Returns an error message, or null.
        /// </summary>
        private async Task<string?> RechainAsync(EntrielContext db, int companyItemId, string? priceListType,
            decimal? basePrice, int? updatedBy)
        {
            var rows = await db.CompanyItemPriceHistories
                .Where(h => h.CompanyItemId == companyItemId && h.PriceListType == priceListType)
                .OrderBy(h => h.EffectivityDate)
                .ThenBy(h => h.CompanyItemPriceHistoryId)
                .ToListAsync();

            var running = basePrice ?? rows.FirstOrDefault()?.OldPrice;
            if (running is null) return null;   // nothing to chain

            foreach (var row in rows)
            {
                row.OldPrice = running.Value;
                row.NewPrice = running.Value + row.PriceIncreaseAmount;
                if (row.NewPrice < 0)
                    return $"The change on {row.EffectivityDate:MMM d, yyyy} would make the price negative ({row.NewPrice:N2}).";
                running = row.NewPrice;
            }

            var current = rows.LastOrDefault(r => r.AppliedDate != null)?.NewPrice ?? basePrice;

            var item = await db.CompanyItems.FirstOrDefaultAsync(c => c.CompanyItemId == companyItemId);
            if (item != null && current.HasValue)
            {
                switch (priceListType)
                {
                    case PriceListTypes.Regional: item.StockPriceRegional = current; break;
                    case PriceListTypes.National: item.StockPriceNational = current; break;
                    default: item.StockPrice = current; break;
                }
                item.UpdatedDate = NowPh();
                if (updatedBy.HasValue) item.UpdatedBy = updatedBy;
            }

            await db.SaveChangesAsync();
            return null;
        }

        /// <summary>
        /// Every active UOM of the company item's sub-items, priced from the company price: price = stock price for
        /// the sub-distributor's price list × conversion (conversions are relative to the stock UOM).
        /// </summary>
        public async Task<IReadOnlyList<CompanyItemUomPriceDto>> GetUomPricesByCompanyItemIdAsync(
            int companyItemId, string? priceListType = null)
        {
            await using var db = _dbFactory.CreateDbContext();

            var item = await db.CompanyItems.AsNoTracking()
                .Where(c => c.CompanyItemId == companyItemId)
                .Select(c => new { c.StockPrice, c.StockPriceRegional, c.StockPriceNational })
                .FirstOrDefaultAsync();
            if (item == null) return new List<CompanyItemUomPriceDto>();

            var usesPriceLists =
                PricingStructureResolver.Resolve(null, item.StockPriceRegional, item.StockPriceNational) == PricingStructure.Dual;

            var query = db.ItemsUoms.AsNoTracking()
                .Where(u => u.SubdItem.CompanyItemId == companyItemId && u.IsActive && u.SubdItem.IsActive);

            if (priceListType != null)
                query = query.Where(u => u.SubdItem.SubDistributor.PriceListType == priceListType);
            else if (usesPriceLists)
                query = query.Where(u => u.SubdItem.SubDistributor.PriceListType != null);

            var uoms = await query
                .Select(u => new
                {
                    u.ItemsUomId,
                    u.SubdItemId,
                    SubdItemCode = u.SubdItem.SubdItemCode,
                    SubdItemName = u.SubdItem.ItemName,
                    u.UomName,
                    u.ConversionToBase,
                    ListType = u.SubdItem.SubDistributor.PriceListType
                })
                .ToListAsync();

            decimal? PriceFor(string? list) => !usesPriceLists
                ? item.StockPrice
                : list switch
                {
                    PriceListTypes.Regional => item.StockPriceRegional,
                    PriceListTypes.National => item.StockPriceNational,
                    _ => null
                };

            return uoms
                .Select(u =>
                {
                    var stock = PriceFor(u.ListType);
                    var current = stock.HasValue && u.ConversionToBase is decimal c && c > 0
                        ? Math.Round(stock.Value * c, 2, MidpointRounding.AwayFromZero)
                        : (decimal?)null;
                    return new CompanyItemUomPriceDto
                    {
                        SubdItemId = u.SubdItemId,
                        SubdItemCode = u.SubdItemCode,
                        SubdItemName = u.SubdItemName,
                        ItemsUomId = u.ItemsUomId,
                        IsStockUom = u.ConversionToBase == 1m,
                        StockConversionToBase = 1m,
                        UomName = u.UomName,
                        ConversionToBase = u.ConversionToBase,
                        CurrentPrice = current,
                        OldPrice = current,
                        PriceListType = usesPriceLists ? u.ListType : null,
                    };
                })
                .OrderBy(x => x.SubdItemCode)
                .ThenByDescending(x => x.ConversionToBase)
                .ToList();
        }

        /// <summary>
        /// Deletes a price change, pending or applied (admin). Invoice lines priced from it are moved to the change in
        /// effect before it (their amounts stay until the invoices are recalculated). The chain is rebuilt and the
        /// company item's current price re-synced. Old UOM history rows linked to it are removed too.
        /// </summary>
        public async Task<(bool success, string? error)> DeletePriceChangeAsync(int companyItemPriceHistoryId, int? updatedBy = null)
        {
            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var history = await db.CompanyItemPriceHistories
                    .FirstOrDefaultAsync(h => h.CompanyItemPriceHistoryId == companyItemPriceHistoryId);

                if (history == null)
                    return (false, "Price change not found.");

                var basePrice = await ChainBasePriceAsync(db, history.CompanyItemId, history.PriceListType);

                // The change in effect before this one (same item and price list).
                var previousId = await db.CompanyItemPriceHistories
                    .Where(h => h.CompanyItemId == history.CompanyItemId
                                && h.PriceListType == history.PriceListType
                                && h.CompanyItemPriceHistoryId != history.CompanyItemPriceHistoryId
                                && (h.EffectivityDate < history.EffectivityDate
                                    || (h.EffectivityDate == history.EffectivityDate
                                        && h.CompanyItemPriceHistoryId < history.CompanyItemPriceHistoryId)))
                    .OrderByDescending(h => h.EffectivityDate)
                    .ThenByDescending(h => h.CompanyItemPriceHistoryId)
                    .Select(h => (int?)h.CompanyItemPriceHistoryId)
                    .FirstOrDefaultAsync();

                await db.SalesInvoiceItems
                    .Where(i => i.CompanyItemPriceHistoryId == companyItemPriceHistoryId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.CompanyItemPriceHistoryId, previousId));

                db.CompanyItemPriceHistories.Remove(history);
                await db.SaveChangesAsync();

                var chainError = await RechainAsync(db, history.CompanyItemId, history.PriceListType, basePrice, updatedBy);
                if (chainError != null)
                {
                    await tx.RollbackAsync();
                    return (false, chainError);
                }

                await tx.CommitAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return (false, $"Unable to delete the price change: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>Kept for callers of the old name.</summary>
        public Task<(bool success, string? error)> CancelPendingIncreaseAsync(int companyItemPriceHistoryId) =>
            DeletePriceChangeAsync(companyItemPriceHistoryId);
    }
}