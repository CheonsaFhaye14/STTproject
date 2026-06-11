using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.CompanyItem.DTOs;

namespace STTproject.Features.Admin.CompanyItem.Services
{
    public class AdminCompanyItemService : IAdminCompanyItemService
    {
        private readonly IDbContextFactory<SttprojectContext> _dbFactory;
        private readonly IConfiguration _config;
        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);


        public AdminCompanyItemService(IDbContextFactory<SttprojectContext> dbFactory, IConfiguration config)
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
        public async Task<CompanyItemListDto?> CreateCompanyItemAsync(CompanyItemCreateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();

            var entity = new Data.CompanyItem
            {
                ItemCode = dto.ItemCode ?? string.Empty,
                ItemName = dto.ItemName ?? string.Empty,
                Principal = dto.Principal ?? string.Empty,
                Category = dto.Category ?? string.Empty,
                IsActive = dto.IsActive,
                CreatedDate = NowPh(),
                // CreatedBy = dto.CreatedBy,
            };
            db.CompanyItems.Add(entity);
            await db.SaveChangesAsync();
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
            entity.Category = dto.Category ?? entity.Category;
            entity.IsActive = dto.IsActive;
            entity.UpdatedDate = NowPh();

            await db.SaveChangesAsync();
            return dto;
        }
        public async Task ToggleCompanyItemStatusAsync(int id, bool isActive)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.CompanyItems.FindAsync(id);
            if (entity == null) return;
            entity.IsActive = isActive;
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
                    Category = u.Category,
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
            string? sortColumn = "ItemCode",
            bool sortAscending = true)
        {
            await using var db = _dbFactory.CreateDbContext();

            var query = db.CompanyItems
                .AsNoTracking()
                .Where(c => string.IsNullOrEmpty(status) ||
                    (status == "active" ? c.IsActive : !c.IsActive))
                .Where(c => string.IsNullOrEmpty(principal) || c.Principal == principal)
                .Where(c => string.IsNullOrEmpty(search) ||
                    (c.ItemCode != null && c.ItemCode.Contains(search)) ||
                    (c.ItemName != null && c.ItemName.Contains(search)) ||
                    (c.Category != null && c.Category.Contains(search)));

            var total = await query.CountAsync();

            query = (sortColumn, sortAscending) switch
            {
                ("ItemCode", true) => query.OrderBy(c => c.ItemCode),
                ("ItemCode", false) => query.OrderByDescending(c => c.ItemCode),
                ("ItemName", true) => query.OrderBy(c => c.ItemName),
                ("ItemName", false) => query.OrderByDescending(c => c.ItemName),
                ("Category", true) => query.OrderBy(c => c.Category),
                ("Category", false) => query.OrderByDescending(c => c.Category),
                ("CreatedDate", true) => query.OrderBy(c => c.CreatedDate),
                ("CreatedDate", false) => query.OrderByDescending(c => c.CreatedDate),
                ("IsActive", true) => query.OrderBy(c => c.IsActive),
                ("IsActive", false) => query.OrderByDescending(c => c.IsActive),
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
                    Category = u.Category,
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
                Category = entity.Category,
                IsActive = entity.IsActive,
                CreatedDate = entity.CreatedDate,
                UpdatedDate = entity.UpdatedDate,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy,
            };
        }
    }
}