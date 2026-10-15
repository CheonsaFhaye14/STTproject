using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Subdistributor.DTOs;
using STTproject.Features.Admin.Users.DTOs;

namespace STTproject.Features.Admin.Subdistributor.Services
{
    public class AdminSubDistributorService : IAdminSubDistributorService
    {
        private const string EncoderRole = "Encoder";

        private readonly IDbContextFactory<EntrielContext> _dbFactory;
        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);

        public AdminSubDistributorService(IDbContextFactory<EntrielContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }
        public async Task<SubDistributorListDto?> CreateSubDistributorAsync(SubDistributorCreateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();

            var entity = new Data.SubDistributor
            {
                SubdCode = dto.SubdCode,
                SubdName = dto.SubdName,
                CityMunicipality = dto.CityMunicipality ?? null,
                Province = dto.Province ?? null,
                EncoderId = dto.EncoderId,
                PriceListType = NormalizePriceListType(dto.PriceListType),
                IsActive = dto.IsActive,
                CreatedDate = NowPh(),
                CreatedBy = dto.CreatedBy,
            };

            db.SubDistributors.Add(entity);
            await db.SaveChangesAsync();

            return await GetSubDistributorByIdAsync(entity.SubDistributorId);
        }

        public async Task<SubDistributorUpdateDto?> UpdateSubDistributorAsync(SubDistributorUpdateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.SubDistributors.FindAsync(dto.SubDistributorId);
            if (entity == null) return null;

            entity.SubdCode = dto.SubdCode ?? entity.SubdCode;
            entity.SubdName = dto.SubdName ?? entity.SubdName;
            entity.CityMunicipality = dto.CityMunicipality ?? entity.CityMunicipality ?? null;
            entity.Province = dto.Province ?? entity.Province ?? null;
            entity.EncoderId = dto.EncoderId;
            entity.PriceListType = NormalizePriceListType(dto.PriceListType);
            entity.IsActive = dto.IsActive;
            entity.UpdatedBy = dto.UpdatedBy;
            entity.UpdatedDate = NowPh();

            await db.SaveChangesAsync();
            return dto;
        }

        public async Task ToggleSubDistributorStatusAsync(int id, bool isActive, int? updatedBy)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.SubDistributors.FindAsync(id);
            if (entity == null) return;
            entity.IsActive = isActive;
            entity.UpdatedDate = NowPh();
            entity.UpdatedBy = updatedBy;
            await db.SaveChangesAsync();
        }

        public async Task<IEnumerable<SubDistributorListDto>> GetAllAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.SubDistributors
                .AsNoTracking()
                .Select(s => new SubDistributorListDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdCode = s.SubdCode,
                    SubdName = s.SubdName,
                    CityMunicipality = s.CityMunicipality ?? null,
                    Province = s.Province ?? null,
                    PriceListType = s.PriceListType,
                    EncoderId = s.EncoderId,
                    EncoderName = s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null,
                    IsActive = s.IsActive,
                    CreatedDate = s.CreatedDate,
                    UpdatedDate = s.UpdatedDate,
                    CreatedByName = s.CreatedByNavigation == null
                        ? null
                        : (s.CreatedByNavigation.FullName ?? s.CreatedByNavigation.Username),

                    UpdatedByName = s.UpdatedByNavigation == null
                        ? null
                        : (s.UpdatedByNavigation.FullName ?? s.UpdatedByNavigation.Username)
                })
                .ToListAsync();
        }

        public async Task<(IEnumerable<SubDistributorListDto> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? status,
            string? province,
            string? sortColumn = "SubdName",
            bool sortAscending = true)
        {
            await using var db = _dbFactory.CreateDbContext();

            var query = db.SubDistributors
                .AsNoTracking()
                .Where(s => string.IsNullOrEmpty(status) ||
                    (status == "active" ? s.IsActive : !s.IsActive))
                .Where(s => string.IsNullOrEmpty(province) || s.Province == province)
                .Where(s => string.IsNullOrEmpty(search) ||
                    s.SubdCode.Contains(search) ||
                    s.SubdName.Contains(search) ||
                    (s.CityMunicipality != null && s.CityMunicipality.Contains(search)) ||
                    (s.Province != null && s.Province.Contains(search)) ||
                    (s.Encoder != null && (s.Encoder.FullName != null && s.Encoder.FullName.Contains(search) || s.Encoder.Username.Contains(search))));

            var total = await query.CountAsync();

            query = (sortColumn, sortAscending) switch
            {
                ("SubdCode", true) => query.OrderBy(s => s.SubdCode),
                ("SubdCode", false) => query.OrderByDescending(s => s.SubdCode),
                ("SubdName", true) => query.OrderBy(s => s.SubdName),
                ("SubdName", false) => query.OrderByDescending(s => s.SubdName),
                ("EncoderName", true) => query.OrderBy(s => s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null),
                ("EncoderName", false) => query.OrderByDescending(s => s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null),
                ("Province", true) => query.OrderBy(s => s.Province),
                ("Province", false) => query.OrderByDescending(s => s.Province),
                ("PriceListType", true)  => query.OrderBy(s => s.PriceListType),
                ("PriceListType", false) => query.OrderByDescending(s => s.PriceListType),
                ("CreatedDate", false) => query.OrderBy(s => s.CreatedDate),
                ("CreatedDate", true) => query.OrderByDescending(s => s.CreatedDate),
                ("IsActive", false) => query.OrderBy(s => s.IsActive),
                ("IsActive", true) => query.OrderByDescending(s => s.IsActive),
                _ => query.OrderBy(s => s.SubdName)
            };

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SubDistributorListDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdCode = s.SubdCode,
                    SubdName = s.SubdName,
                    PriceListType = s.PriceListType,
                    CityMunicipality = s.CityMunicipality ?? null,
                    Province = s.Province ?? null,
                    EncoderId = s.EncoderId,
                    EncoderName = s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null,
                    IsActive = s.IsActive,
                    CreatedDate = s.CreatedDate,
                    UpdatedDate = s.UpdatedDate,
                    CreatedByName = s.CreatedByNavigation == null
                        ? null
                        : (s.CreatedByNavigation.FullName ?? s.CreatedByNavigation.Username),

                    UpdatedByName = s.UpdatedByNavigation == null
                        ? null
                        : (s.UpdatedByNavigation.FullName ?? s.UpdatedByNavigation.Username)
                })
                .ToListAsync();

            return (items, total);
        }
        private static string? NormalizePriceListType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var match = PriceListTypes.All.FirstOrDefault(
                t => t.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

            return match ?? throw new ArgumentException($"Invalid price list type: {value}");
        }
        public async Task<SubDistributorListDto?> GetSubDistributorByIdAsync(int id)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.SubDistributors
                .AsNoTracking()
                .Where(s => s.SubDistributorId == id)
                .Select(s => new SubDistributorListDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdCode = s.SubdCode,
                    SubdName = s.SubdName,
                    CityMunicipality = s.CityMunicipality ?? null,
                    Province = s.Province ?? null,
                    EncoderId = s.EncoderId,
                    EncoderName = s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null,
                    IsActive = s.IsActive,
                    PriceListType = s.PriceListType,
                    CreatedDate = s.CreatedDate,
                    UpdatedDate = s.UpdatedDate,
                    CreatedByName = s.CreatedByNavigation == null
                        ? null
                        : (s.CreatedByNavigation.FullName ?? s.CreatedByNavigation.Username),
                    UpdatedByName = s.UpdatedByNavigation == null
                        ? null
                        : (s.UpdatedByNavigation.FullName ?? s.UpdatedByNavigation.Username)
                })
                .FirstOrDefaultAsync();
        }

        public async Task<List<EncoderUserDropdownDto>> GetEncoderUsersAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.Users
                .AsNoTracking()
                .Where(u => u.Role == EncoderRole && u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new EncoderUserDropdownDto
                {
                    UserId = u.UserId,
                    FullName = u.FullName,
                    UserName = u.Username
                })
                .ToListAsync();
        }

    }
}