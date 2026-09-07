using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public class AdminCustomerService : IAdminCustomerService
    {
        private readonly IDbContextFactory<SttprojectContext> _dbFactory;
        private readonly IGeographicDataService _geographicDataService;

        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);

        public AdminCustomerService(IDbContextFactory<SttprojectContext> dbFactory, IGeographicDataService geographicDataService)
        {
            _dbFactory = dbFactory;
            _geographicDataService = geographicDataService;
        }

        public async Task<string?> GetUserNameByIdAsync(int? userId)
        {
            if (userId == null) return null;
            await using var db = _dbFactory.CreateDbContext();
            var user = await db.Users.FindAsync(userId.Value);
            return user?.FullName ?? user?.Username;
        }

        public async Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = new Customer
            {
                CustomerCode = dto.CustomerCode ?? string.Empty,
                CustomerName = dto.CustomerName ?? string.Empty,
                SubdCustCode = dto.SubdCustCode,
                SubdCustName = dto.SubdCustName,
                CustomerType = dto.CustomerType,
                SubDistributorId = dto.SubDistributorId,
                IsActive = dto.IsActive,
                AddressLine = dto.AddressLine,
                City = dto.City,
                Province = dto.Province,
                ZipCode = dto.ZipCode,
                CreatedDate = NowPh(),
                CreatedBy = dto.CreatedBy
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
            entity.SubdCustCode = dto.SubdCustCode;
            entity.SubdCustName = dto.SubdCustName;
            entity.CustomerType = dto.CustomerType;
            entity.SubDistributorId = dto.SubDistributorId;
            entity.IsActive = dto.IsActive;
            entity.AddressLine = dto.AddressLine;
            entity.City = dto.City;
            entity.Province = dto.Province;
            entity.ZipCode = dto.ZipCode;
            entity.UpdatedDate = NowPh();
            entity.UpdatedBy = dto.UpdatedBy;
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
            entity.UpdatedDate = NowPh();  // ← changed
            await db.SaveChangesAsync();
        }

        public async Task<IEnumerable<CustomerListDto>> GetAllAsync()
        {
            await using var db = _dbFactory.CreateDbContext();

            var flat = db.Customers
                .AsNoTracking()
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerCode,
                    c.CustomerName,
                    c.CustomerType,
                    c.SubDistributorId,
                    SubDistributorName = c.SubDistributor != null ? c.SubDistributor.SubdName : null,
                    c.IsActive,
                    c.CreatedDate
                });

            return await flat
                .GroupBy(c => new { c.CustomerCode, c.CustomerName, c.SubDistributorId })
                .Select(g => new CustomerListDto
                {
                    CustomerId = g.Min(c => c.CustomerId),
                    CustomerCode = g.Key.CustomerCode,
                    CustomerName = g.Key.CustomerName,
                    SubDistributorId = g.Key.SubDistributorId,
                    SubDistributorName = g.Select(c => c.SubDistributorName).FirstOrDefault(),
                    CustomerType = g.Select(c => c.CustomerType).FirstOrDefault(),
                    IsActive = g.Select(c => c.IsActive).FirstOrDefault(),
                    CreatedDate = g.Min(c => c.CreatedDate)
                })
                .ToListAsync();
        }
        
        public async Task<(IEnumerable<CustomerListDto> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? status,
            string? customerType,
            int? subDistributorId,
            string? sortColumn = "CustomerName",
            bool sortAscending = true)
        {
            await using var db = _dbFactory.CreateDbContext();

            var filtered = db.Customers
                .AsNoTracking()
                .Where(c => subDistributorId == null || c.SubDistributorId == subDistributorId)
                .Where(c => string.IsNullOrEmpty(customerType) || c.CustomerType == customerType)
                .Where(c => string.IsNullOrEmpty(status) ||
                    (status == "active" ? c.IsActive : !c.IsActive))
                .Where(c => string.IsNullOrEmpty(search) ||
                    c.CustomerName.Contains(search) ||
                    c.CustomerCode.Contains(search));

            // Flatten the join first so GroupBy doesn't have to touch the navigation property directly.
            var flat = filtered.Select(c => new
            {
                c.CustomerId,
                c.CustomerCode,
                c.CustomerName,
                c.CustomerType,
                c.SubDistributorId,
                SubDistributorName = c.SubDistributor != null ? c.SubDistributor.SubdName : null,
                c.IsActive,
                c.CreatedDate
            });

            // One row per (CustomerCode, CustomerName, SubDistributorId) — collapses sibling Subd-mapping rows.
            var grouped = flat
                .GroupBy(c => new { c.CustomerCode, c.CustomerName, c.SubDistributorId })
                .Select(g => new CustomerListDto
                {
                    CustomerId = g.Min(c => c.CustomerId),           // anchor row id — used for View/navigation
                    CustomerCode = g.Key.CustomerCode,
                    CustomerName = g.Key.CustomerName,
                    SubDistributorId = g.Key.SubDistributorId,
                    SubDistributorName = g.Select(c => c.SubDistributorName).FirstOrDefault(),
                    CustomerType = g.Select(c => c.CustomerType).FirstOrDefault(),
                    IsActive = g.Select(c => c.IsActive).FirstOrDefault(),
                    CreatedDate = g.Min(c => c.CreatedDate)
                });

            var total = await grouped.CountAsync();

            grouped = (sortColumn, sortAscending) switch
            {
                ("CustomerCode", true) => grouped.OrderBy(c => c.CustomerCode),
                ("CustomerCode", false) => grouped.OrderByDescending(c => c.CustomerCode),
                ("CustomerName", true) => grouped.OrderBy(c => c.CustomerName),
                ("CustomerName", false) => grouped.OrderByDescending(c => c.CustomerName),
                ("CustomerType", true) => grouped.OrderBy(c => c.CustomerType),
                ("CustomerType", false) => grouped.OrderByDescending(c => c.CustomerType),
                ("SubDistributor", true) => grouped.OrderBy(c => c.SubDistributorName),
                ("SubDistributor", false) => grouped.OrderByDescending(c => c.SubDistributorName),
                ("CreatedDate", true) => grouped.OrderBy(c => c.CreatedDate),
                ("CreatedDate", false) => grouped.OrderByDescending(c => c.CreatedDate),
                ("IsActive", true) => grouped.OrderBy(c => c.IsActive),
                ("IsActive", false) => grouped.OrderByDescending(c => c.IsActive),
                _ => grouped.OrderBy(c => c.CustomerName)
            };

            var items = await grouped
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, total);
        }

        public async Task<bool> CustomerCodeExistsAsync(string customerCode, int subDistributorId, IEnumerable<int>? excludeIds = null)
        {
            await using var db = _dbFactory.CreateDbContext();

            var query = db.Customers.Where(c =>
                c.CustomerCode == customerCode &&
                c.SubDistributorId == subDistributorId);

            if (excludeIds != null)
            {
                var idSet = excludeIds.ToHashSet();
                if (idSet.Count > 0)
                    query = query.Where(c => !idSet.Contains(c.CustomerId));
            }

            return await query.AnyAsync();
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
                .Select(s => new SubDistributorDto { SubDistributorId = s.SubDistributorId, SubDistributorName = s.SubdName ?? string.Empty })
                .Take(200)
                .ToListAsync();
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

        public async Task<CustomerDetailDto?> GetCustomerByIdAsync(int id)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Customers
                .AsNoTracking()
                .Include(c => c.SubDistributor)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (entity == null) return null;

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
                UpdatedDate = entity.UpdatedDate,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy
            };
        }

        public async Task<CustomerDetailDto?> UpdateCustomerAsync(CustomerUpdateDto dto)
            => await UpdateCustomerAsync(dto.CustomerId, dto);
    
        public async Task<IEnumerable<SubdMappingDto>> GetCustomerGroupMappingsAsync(int customerId)
        {
            await using var db = _dbFactory.CreateDbContext();

            var anchor = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId);
            if (anchor == null) return Enumerable.Empty<SubdMappingDto>();

            return await db.Customers
                .AsNoTracking()
                .Where(c => c.CustomerCode == anchor.CustomerCode)
                .OrderBy(c => c.CustomerId)
                .Select(c => new SubdMappingDto
                {
                    CustomerId = c.CustomerId,
                    SubDistributorId = c.SubDistributorId,
                    SubdCustCode = c.SubdCustCode,
                    SubdCustName = c.SubdCustName
                })
                .ToListAsync();
        }

        public async Task<(bool success, string? error)> UpdateCustomerGroupAsync(CustomerGroupUpdateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                // The anchor row's scalar fields (CustomerCode, CustomerName, IsActive, address, etc.)
                // must already have been saved via UpdateCustomerAsync before calling this — we read
                // them back here as the source of truth to keep sibling rows in sync.
                var anchor = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == dto.CustomerId);
                if (anchor == null)
                {
                    await tx.RollbackAsync();
                    return (false, "Customer not found.");
                }

                var siblings = await db.Customers
                    .Where(c => c.CustomerCode == anchor.CustomerCode && c.CustomerId != anchor.CustomerId)
                    .ToListAsync();

                var incomingIds = dto.Mappings
                    .Where(m => m.CustomerId > 0 && m.CustomerId != dto.CustomerId)
                    .Select(m => m.CustomerId)
                    .ToHashSet();

                // Sibling rows the user removed
                var toRemove = siblings.Where(s => !incomingIds.Contains(s.CustomerId)).ToList();
                if (toRemove.Count > 0)
                    db.Customers.RemoveRange(toRemove);

                var now = NowPh();

                // Update surviving siblings — keep shared fields in sync with the anchor
                foreach (var mapping in dto.Mappings.Where(m => m.CustomerId > 0 && m.CustomerId != dto.CustomerId))
                {
                    var entity = siblings.FirstOrDefault(s => s.CustomerId == mapping.CustomerId);
                    if (entity == null) continue;

                    entity.CustomerCode = anchor.CustomerCode;
                    entity.CustomerName = anchor.CustomerName;
                    entity.CustomerType = anchor.CustomerType;
                    entity.IsActive = anchor.IsActive;
                    entity.AddressLine = anchor.AddressLine;
                    entity.City = anchor.City;
                    entity.Province = anchor.Province;
                    entity.ZipCode = anchor.ZipCode;
                    entity.SubDistributorId = mapping.SubDistributorId;
                    entity.SubdCustCode = mapping.SubdCustCode;
                    entity.SubdCustName = mapping.SubdCustName;
                    entity.UpdatedDate = now;
                    entity.UpdatedBy = anchor.UpdatedBy;
                }

                // Insert newly added mapping rows
                foreach (var mapping in dto.Mappings.Where(m => m.CustomerId <= 0))
                {
                    db.Customers.Add(new Customer
                    {
                        CustomerCode = anchor.CustomerCode,
                        CustomerName = anchor.CustomerName,
                        CustomerType = anchor.CustomerType,
                        SubDistributorId = mapping.SubDistributorId,
                        IsActive = anchor.IsActive,
                        AddressLine = anchor.AddressLine,
                        City = anchor.City,
                        Province = anchor.Province,
                        ZipCode = anchor.ZipCode,
                        SubdCustCode = mapping.SubdCustCode,
                        SubdCustName = mapping.SubdCustName,
                        CreatedDate = now,
                        CreatedBy = anchor.UpdatedBy
                    });
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, null);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx && sqlEx.Number == 2601)
            {
                await tx.RollbackAsync();
                return (false, "One of the entered Customer Code / Subd Customer Code combinations already exists for the selected subdistributor.");
            }
        }

        public async Task<ImportMatchResult> CheckImportDuplicateAsync(
            string customerCode, string customerName, int subDistributorId,
            string? subdCustCode, string? subdCustName)
        {
            await using var db = _dbFactory.CreateDbContext();

            var candidates = await db.Customers
                .Where(c => c.CustomerCode == customerCode &&
                            c.CustomerName == customerName &&
                            c.SubDistributorId == subDistributorId)
                .Select(c => new { c.CustomerId, c.SubdCustCode, c.SubdCustName })
                .ToListAsync();

            var exact = candidates.FirstOrDefault(c =>
                string.Equals(c.SubdCustCode ?? "", subdCustCode ?? "", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.SubdCustName ?? "", subdCustName ?? "", StringComparison.OrdinalIgnoreCase));

            if (exact != null)
                return new ImportMatchResult(ImportMatchType.ExactDuplicate, exact.CustomerId);

            if (!string.IsNullOrWhiteSpace(subdCustCode) || !string.IsNullOrWhiteSpace(subdCustName))
            {
                var blank = candidates.FirstOrDefault(c =>
                    string.IsNullOrWhiteSpace(c.SubdCustCode) && string.IsNullOrWhiteSpace(c.SubdCustName));

                if (blank != null)
                    return new ImportMatchResult(ImportMatchType.FillableBlank, blank.CustomerId);
            }

            return new ImportMatchResult(ImportMatchType.None, null);
        }

        public async Task<CustomerDetailDto?> FillBlankSubdMappingAsync(
            int customerId, string? subdCustCode, string? subdCustName, int? updatedBy)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Customers.FindAsync(customerId);
            if (entity == null) return null;

            entity.SubdCustCode = subdCustCode;
            entity.SubdCustName = subdCustName;
            entity.UpdatedDate = NowPh();
            entity.UpdatedBy = updatedBy;
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
            
    }   
}