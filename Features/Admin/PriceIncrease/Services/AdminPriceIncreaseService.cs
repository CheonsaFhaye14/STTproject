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
                    ConversionToBase = h.ItemsUom.ConversionToBase,
                    PriceListType = h.ItemsUom.SubdItem.SubDistributor.PriceListType,
                    UomName = h.ItemsUom.UomName,
                    OldPrice = h.OldPrice,
                    NewPrice = h.NewPrice,
                    AppliedDate = h.AppliedDate,
                    CreatedBy = h.CreatedBy
                }).OrderBy(x => x.SubdItemCode)
                .ThenBy(x => x.SubdItemId)
                .ThenBy(x => x.ConversionToBase ?? int.MinValue)
                .ThenBy(x => x.UomName).ToListAsync();
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

            // Build UOM overrides.
            var skippedUomIds = dto.SkippedUomIds
                ?? new List<int>();

            var manualUomPrices = dto.ManualUomPrices
                ?? new Dictionary<int, decimal>();

            var overridesTable = new DataTable();
            overridesTable.Columns.Add("ItemsUomId", typeof(int));
            overridesTable.Columns.Add("Skip", typeof(bool));
            overridesTable.Columns.Add("NewPrice", typeof(decimal));

            foreach (var uomId in skippedUomIds)
            {
                overridesTable.Rows.Add(uomId, true, DBNull.Value);
            }

            foreach (var kv in manualUomPrices)
            {
                if (skippedUomIds.Contains(kv.Key))
                    continue;

                overridesTable.Rows.Add(kv.Key, false, kv.Value);
            }

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

                    var tvp = cmd.Parameters.AddWithValue(
                        "@UomOverrides", overridesTable);

                    tvp.SqlDbType = SqlDbType.Structured;
                    tvp.TypeName = "dbo.UomOverrideTableType";

                    await cmd.ExecuteNonQueryAsync();
                }

                // Both price-list schedules succeed together, or neither is saved.
                await tx.CommitAsync();

                return (true, null);
            }
            catch (SqlException sqlEx)
                when (sqlEx.Number is >= 50001 and <= 50006)
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

                    50004 => (
                        false,
                        "Some UOMs can't be calculated automatically (missing stock UOM, conversion or price). Enter a price or choose \"Don't change price\" for them."),

                    50005 => (
                        false,
                        $"The {label} change would make a price negative."),

                    50006 => (
                        false,
                        "The price list doesn't match this item's pricing structure."),

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

                var start = effectivityDate.Date;
                var end = start.AddDays(1);

                var duplicateExists = await db.CompanyItemPriceHistories
                    .AnyAsync(h =>
                        h.CompanyItemId == history.CompanyItemId
                        && h.AppliedDate == null
                        && h.CompanyItemPriceHistoryId != companyItemPriceHistoryId
                        && h.PriceListType == history.PriceListType
                        && h.EffectivityDate >= start
                        && h.EffectivityDate < end);

                if (duplicateExists)
                {
                    return (false,
                        "This item already has a pending price change for that price list and effectivity date.");
                }

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
                    var conversion = uom.ItemsUom?.ConversionToBase ?? 1;
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

        public async Task<IReadOnlyList<CompanyItemUomPriceDto>> GetUomPricesByCompanyItemIdAsync(
            int companyItemId, string? priceListType = null)
        {
            await using var db = _dbFactory.CreateDbContext();

            var item = await db.CompanyItems.AsNoTracking()
                .Where(c => c.CompanyItemId == companyItemId)
                .Select(c => new { c.StockPriceRegional, c.StockPriceNational })
                .FirstOrDefaultAsync();
            var usesPriceLists = item != null &&
                PricingStructureResolver.Resolve(null, item.StockPriceRegional, item.StockPriceNational) == PricingStructure.Dual;

            var query = db.ItemsUoms
                .AsNoTracking()
                .Where(u => u.SubdItem.CompanyItemId == companyItemId
                    && u.IsActive
                    && u.SubdItem.IsActive);

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
                    IsStockUom = u.IsStockUom,
                    StockConversion = u.SubdItem.ItemsUoms
                        .Where(x => x.IsStockUom && x.IsActive)
                        .Select(x => x.ConversionToBase)
                        .FirstOrDefault(),
                    u.UomName,
                    u.ConversionToBase,
                    CurrentPrice = u.Price,
                    ListType = u.SubdItem.SubDistributor.PriceListType
                })
                .ToListAsync();

            var uomIds = uoms.Select(u => u.ItemsUomId).ToList();

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
                        IsStockUom = u.IsStockUom,
                        StockConversionToBase = u.StockConversion,
                        UomName = u.UomName,
                        CurrentPrice = u.CurrentPrice,
                        ConversionToBase = u.ConversionToBase,
                        OldPrice = history?.OldPrice ?? u.CurrentPrice,
                        NewPrice = history?.NewPrice,
                        PriceListType = u.ListType,
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