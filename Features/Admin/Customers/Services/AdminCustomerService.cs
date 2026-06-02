using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public class AdminCustomerService : IAdminCustomerService
    {
        private readonly IDbContextFactory<SttprojectContext> _dbFactory;
        private readonly STTproject.Features.Shared.Services.ILocationService _locationService;

        public AdminCustomerService(IDbContextFactory<SttprojectContext> dbFactory, STTproject.Features.Shared.Services.ILocationService locationService)
        {
            _dbFactory = dbFactory;
            _locationService = locationService;
        }

        public async Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = new Customer
            {
                CustomerCode = dto.CustomerCode ?? string.Empty,
                CustomerName = dto.CustomerName ?? string.Empty,
                CustomerType = dto.CustomerType ?? string.Empty,
                SubDistributorId = dto.SubDistributorId,
                IsActive = dto.IsActive,
                AddressLine = dto.AddressLine,
                City = dto.City,
                Province = dto.Province,
                ZipCode = dto.ZipCode,
                CreatedDate = System.DateTime.UtcNow
            };
            db.Customers.Add(entity);
            await db.SaveChangesAsync();
            return new CustomerDetailDto
            {
                CustomerId = entity.CustomerId,
                CustomerCode = entity.CustomerCode,
                CustomerName = entity.CustomerName,
                CustomerType = entity.CustomerType,
                SubDistributorId = entity.SubDistributorId,
                IsActive = entity.IsActive,
                AddressLine = entity.AddressLine,
                City = entity.City,
                Province = entity.Province,
                ZipCode = entity.ZipCode,
                CreatedDate = entity.CreatedDate
            };
        }

        public async Task<CustomerDetailDto?> UpdateCustomerAsync(int id, CustomerUpdateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Customers.FindAsync(id);
            if (entity == null) return null;
            entity.CustomerCode = dto.CustomerCode ?? entity.CustomerCode;
            entity.CustomerName = dto.CustomerName ?? entity.CustomerName;
            entity.CustomerType = dto.CustomerType ?? entity.CustomerType;
            entity.SubDistributorId = dto.SubDistributorId;
            entity.IsActive = dto.IsActive;
            entity.AddressLine = dto.AddressLine;
            entity.City = dto.City;
            entity.Province = dto.Province;
            entity.ZipCode = dto.ZipCode;
            entity.UpdatedDate = System.DateTime.UtcNow;
            await db.SaveChangesAsync();
            return new CustomerDetailDto
            {
                CustomerId = entity.CustomerId,
                CustomerCode = entity.CustomerCode,
                CustomerName = entity.CustomerName,
                CustomerType = entity.CustomerType,
                SubDistributorId = entity.SubDistributorId,
                IsActive = entity.IsActive,
                AddressLine = entity.AddressLine,
                City = entity.City,
                Province = entity.Province,
                ZipCode = entity.ZipCode,
                CreatedDate = entity.CreatedDate,
                UpdatedDate = entity.UpdatedDate
            };
        }

        public async Task ToggleCustomerStatusAsync(int id, bool isActive)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Customers.FindAsync(id);
            if (entity == null) return;
            entity.IsActive = isActive;
            entity.UpdatedDate = System.DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<bool> CustomerCodeExistsAsync(string code, int subDistributorId, int? excludeCustomerId = null)
        {
            await using var db = _dbFactory.CreateDbContext();
            var q = db.Customers.AsQueryable().Where(c => c.CustomerCode == code && c.SubDistributorId == subDistributorId);
            if (excludeCustomerId.HasValue) q = q.Where(c => c.CustomerId != excludeCustomerId.Value);
            return await q.AnyAsync();
        }

        public async Task<IEnumerable<CustomerListDto>> GetAllAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.Customers
                .Include(c => c.SubDistributor)
                .Select(c => new CustomerListDto
                {
                    CustomerId = c.CustomerId,
                    CustomerCode = c.CustomerCode,
                    CustomerName = c.CustomerName,
                    CustomerType = c.CustomerType,
                    SubDistributorId = c.SubDistributorId,
                    SubDistributorName = c.SubDistributor != null ? c.SubDistributor.SubdName : null,
                    IsActive = c.IsActive,
                    CreatedDate = c.CreatedDate
                }).ToListAsync();
        }

        public async Task<IEnumerable<SubDistributorDto>> GetSubDistributorsAsync(string? query = null)
        {
            await using var db = _dbFactory.CreateDbContext();
            var q = db.SubDistributors.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var qnorm = query.Trim().ToLower();
                q = q.Where(s => s.SubdName != null && s.SubdName.ToLower().Contains(qnorm));
            }

            return await q.OrderBy(s => s.SubdName)
                .Select(s => new SubDistributorDto { SubDistributorId = s.SubDistributorId, SubDistributorName = s.SubdName })
                .Take(200)
                .ToListAsync();
        }

        public async Task<IEnumerable<ProvinceDto>> GetProvincesAsync()
        {
            var provs = await _locationService.GetProvinceInfosAsync();
            return provs.Select(p => new ProvinceDto { Name = p.Name, Code = p.Code }).ToList();
        }

        public async Task<IEnumerable<CityDto>> GetCitiesForProvinceAsync(string provinceCode)
        {
            var cities = await _locationService.GetCitiesForProvinceByCodeAsync(provinceCode);
            return cities.Select(c => new CityDto { Name = c }).ToList();
        }

        public async Task<IEnumerable<string>> GetCustomerTypesAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.Customers
                .Where(c => !string.IsNullOrWhiteSpace(c.CustomerType))
                .Select(c => c.CustomerType!)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();
        }
    }
}
