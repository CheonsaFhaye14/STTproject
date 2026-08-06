using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Subdistributor.DTOs;
using STTproject.Features.Admin.Users.DTOs;

namespace STTproject.Features.Admin.Subdistributor.Services
{
    public class AdminSubDistributorService : IAdminSubDistributorService
    {
        private const string EncoderRole = "Encoder";

        private readonly IDbContextFactory<SttprojectContext> _dbFactory;
        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);

        public AdminSubDistributorService(IDbContextFactory<SttprojectContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<UserListDto>> GetEncoderUsersAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.Users
                .AsNoTracking()
                .Where(u => u.Role == EncoderRole && u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new UserListDto
                {
                    UserId = u.UserId,
                    UserName = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    IsActive = u.IsActive,
                    CreatedDate = u.CreatedDate,
                    UpdatedDate = u.UpdatedDate,
                })
                .ToListAsync();
        }

        public async Task<bool> IsValidEncoderAsync(int userId)
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.Users
                .AsNoTracking()
                .AnyAsync(u => u.UserId == userId && u.Role == EncoderRole && u.IsActive);
        }

        public async Task<SubDistributorListDto?> CreateSubDistributorAsync(SubDistributorCreateDto dto)
        {
            if (dto.EncoderId.HasValue && !await IsValidEncoderAsync(dto.EncoderId.Value))
                throw new InvalidOperationException("Selected user is not an active Encoder.");

            await using var db = _dbFactory.CreateDbContext();

            var entity = new Data.SubDistributor
            {
                SubdCode = dto.SubdCode,
                SubdName = dto.SubdName,
                CityMunicipality = dto.CityMunicipality,
                Province = dto.Province,
                CompanySubdCode = dto.CompanySubdCode,
                EncoderId = dto.EncoderId,
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
            if (dto.EncoderId.HasValue && !await IsValidEncoderAsync(dto.EncoderId.Value))
                throw new InvalidOperationException("Selected user is not an active Encoder.");

            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.SubDistributors.FindAsync(dto.SubDistributorId);
            if (entity == null) return null;

            entity.SubdCode = dto.SubdCode ?? entity.SubdCode;
            entity.SubdName = dto.SubdName ?? entity.SubdName;
            entity.CityMunicipality = dto.CityMunicipality ?? entity.CityMunicipality;
            entity.Province = dto.Province ?? entity.Province;
            entity.CompanySubdCode = dto.CompanySubdCode ?? entity.CompanySubdCode;
            entity.EncoderId = dto.EncoderId;
            entity.IsActive = dto.IsActive;
            entity.UpdatedBy = dto.UpdatedBy;
            entity.UpdatedDate = NowPh();

            await db.SaveChangesAsync();
            return dto;
        }

        public async Task ToggleSubDistributorStatusAsync(int id, bool isActive)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.SubDistributors.FindAsync(id);
            if (entity == null) return;
            entity.IsActive = isActive;
            entity.UpdatedDate = NowPh();
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
                    CityMunicipality = s.CityMunicipality,
                    Province = s.Province,
                    CompanySubdCode = s.CompanySubdCode,
                    EncoderId = s.EncoderId,
                    EncoderName = s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null,
                    IsActive = s.IsActive,
                    CreatedDate = s.CreatedDate,
                    UpdatedDate = s.UpdatedDate,
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
                    s.CityMunicipality.Contains(search));

            var total = await query.CountAsync();

            query = (sortColumn, sortAscending) switch
            {
                ("SubdCode", true) => query.OrderBy(s => s.SubdCode),
                ("SubdCode", false) => query.OrderByDescending(s => s.SubdCode),
                ("SubdName", true) => query.OrderBy(s => s.SubdName),
                ("SubdName", false) => query.OrderByDescending(s => s.SubdName),
                ("Province", true) => query.OrderBy(s => s.Province),
                ("Province", false) => query.OrderByDescending(s => s.Province),
                ("CreatedDate", true) => query.OrderBy(s => s.CreatedDate),
                ("CreatedDate", false) => query.OrderByDescending(s => s.CreatedDate),
                ("IsActive", true) => query.OrderBy(s => s.IsActive),
                ("IsActive", false) => query.OrderByDescending(s => s.IsActive),
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
                    CityMunicipality = s.CityMunicipality,
                    Province = s.Province,
                    CompanySubdCode = s.CompanySubdCode,
                    EncoderId = s.EncoderId,
                    EncoderName = s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null,
                    IsActive = s.IsActive,
                    CreatedDate = s.CreatedDate,
                    UpdatedDate = s.UpdatedDate,
                })
                .ToListAsync();

            return (items, total);
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
                    CityMunicipality = s.CityMunicipality,
                    Province = s.Province,
                    CompanySubdCode = s.CompanySubdCode,
                    EncoderId = s.EncoderId,
                    EncoderName = s.Encoder != null ? (s.Encoder.FullName ?? s.Encoder.Username) : null,
                    IsActive = s.IsActive,
                    CreatedDate = s.CreatedDate,
                    UpdatedDate = s.UpdatedDate,
                })
                .FirstOrDefaultAsync();
        }
    }
}