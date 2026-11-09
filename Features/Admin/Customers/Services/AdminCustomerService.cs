using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public class AdminCustomerService : IAdminCustomerService
    {
        private readonly IDbContextFactory<EntrielContext> _dbFactory;
        private readonly IGeographicDataService _geographicDataService;

        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);

        public AdminCustomerService(IDbContextFactory<EntrielContext> dbFactory, IGeographicDataService geographicDataService)
        {
            _dbFactory = dbFactory;
            _geographicDataService = geographicDataService;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static CustomerDetailDto ToDetailDto(Customer c) => new()
        {
            CustomerId = c.CustomerId,
            CustomerCode = c.CustomerCode,
            CustomerName = c.CustomerName,
            CustomerType = c.CustomerType,
            SubDistributorId = c.SubDistributorId,
            IsActive = c.IsActive,
            AddressLine = c.AddressLine,
            City = c.City,
            Province = c.Province,
            ZipCode = c.ZipCode,
            CreatedDate = c.CreatedDate,
            UpdatedDate = c.UpdatedDate,
            CreatedBy = c.CreatedBy,
            UpdatedBy = c.UpdatedBy,
            Mappings = c.SubdCustomers
                .OrderBy(m => m.SubdCustomerId)
                .Select(m => new SubdMappingDto
                {
                    SubdCustomerId = m.SubdCustomerId,
                    SubdCustCode = m.SubdCustCode,
                    SubdCustName = m.SubdCustName
                })
                .ToList()
        };

        // Trims, applies the placeholder rule, drops blank pairs and repeats (first one wins).
        private static List<SubdMappingDto> CleanMappings(IEnumerable<SubdMappingDto>? mappings)
        {
            var seen = new HashSet<string>();
            var result = new List<SubdMappingDto>();

            foreach (var m in mappings ?? Enumerable.Empty<SubdMappingDto>())
            {
                var code = SubdCustomerKey.Code(m.SubdCustCode);
                var name = SubdCustomerKey.Name(m.SubdCustName);
                if (code.Length == 0 && name.Length == 0) continue;
                if (!seen.Add(SubdCustomerKey.Compare(code, name))) continue;

                result.Add(new SubdMappingDto
                {
                    SubdCustomerId = m.SubdCustomerId,
                    SubdCustCode = code,
                    SubdCustName = name
                });
            }

            return result;
        }

        // ── Users ─────────────────────────────────────────────────────────

        public async Task<string?> GetUserNameByIdAsync(int? userId)
        {
            if (userId == null) return null;
            await using var db = _dbFactory.CreateDbContext();
            var user = await db.Users.FindAsync(userId.Value);
            return user?.FullName ?? user?.Username;
        }

        // ── Create / update ───────────────────────────────────────────────

        public async Task<CustomerDetailDto?> CreateCustomerAsync(CustomerCreateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            var now = NowPh();

            var entity = new Customer
            {
                CustomerCode = dto.CustomerCode?.Trim() ?? string.Empty,
                CustomerName = dto.CustomerName?.Trim() ?? string.Empty,
                CustomerType = dto.CustomerType,
                SubDistributorId = dto.SubDistributorId,
                IsActive = dto.IsActive,
                AddressLine = dto.AddressLine,
                City = dto.City,
                Province = dto.Province,
                ZipCode = dto.ZipCode,
                CreatedDate = now,
                CreatedBy = dto.CreatedBy
            };

            foreach (var m in CleanMappings(dto.Mappings))
            {
                entity.SubdCustomers.Add(new SubdCustomer
                {
                    SubdCustCode = m.SubdCustCode!,
                    SubdCustName = m.SubdCustName!,
                    CreatedDate = now,
                    CreatedBy = dto.CreatedBy
                });
            }

            db.Customers.Add(entity);
            await db.SaveChangesAsync();
            return ToDetailDto(entity);
        }

        public async Task<CustomerDetailDto?> UpdateCustomerAsync(CustomerUpdateDto dto)
            => await UpdateCustomerAsync(dto.CustomerId, dto);

        // Saves the customer's fields and syncs its subd mappings (add / change / remove) in one transaction.
        public async Task<CustomerDetailDto?> UpdateCustomerAsync(int id, CustomerUpdateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            var entity = await db.Customers
                .Include(c => c.SubdCustomers)
                .FirstOrDefaultAsync(c => c.CustomerId == id);
            if (entity == null) return null;

            var now = NowPh();

            entity.CustomerCode = dto.CustomerCode?.Trim() ?? entity.CustomerCode;
            entity.CustomerName = dto.CustomerName?.Trim() ?? entity.CustomerName;
            entity.CustomerType = dto.CustomerType;
            entity.SubDistributorId = dto.SubDistributorId;
            entity.IsActive = dto.IsActive;
            entity.AddressLine = dto.AddressLine;
            entity.City = dto.City;
            entity.Province = dto.Province;
            entity.ZipCode = dto.ZipCode;
            entity.UpdatedDate = now;
            entity.UpdatedBy = dto.UpdatedBy;

            var incoming = CleanMappings(dto.Mappings);
            var keepIds = incoming
                .Where(m => m.SubdCustomerId > 0)
                .Select(m => m.SubdCustomerId)
                .ToHashSet();

            // 1. Delete removed mappings first and save, so re-adding the same code/name
            //    in this edit can't collide with the row being deleted.
            var removed = entity.SubdCustomers.Where(m => !keepIds.Contains(m.SubdCustomerId)).ToList();
            if (removed.Count > 0)
            {
                db.SubdCustomers.RemoveRange(removed);
                await db.SaveChangesAsync();
            }

            // 2. Update changed mappings and add new ones.
            foreach (var m in incoming)
            {
                if (m.SubdCustomerId > 0)
                {
                    var existing = entity.SubdCustomers.FirstOrDefault(x => x.SubdCustomerId == m.SubdCustomerId);
                    if (existing == null) continue; // not this customer's mapping: ignore

                    if (existing.SubdCustCode != m.SubdCustCode || existing.SubdCustName != m.SubdCustName)
                    {
                        existing.SubdCustCode = m.SubdCustCode!;
                        existing.SubdCustName = m.SubdCustName!;
                        existing.UpdatedDate = now;
                        existing.UpdatedBy = dto.UpdatedBy;
                    }
                }
                else
                {
                    entity.SubdCustomers.Add(new SubdCustomer
                    {
                        SubdCustCode = m.SubdCustCode!,
                        SubdCustName = m.SubdCustName!,
                        CreatedDate = now,
                        CreatedBy = dto.UpdatedBy
                    });
                }
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return ToDetailDto(entity);
        }

        public async Task ToggleCustomerStatusAsync(int id, bool isActive, int? updatedBy)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Customers.FindAsync(id);
            if (entity == null) return;
            entity.IsActive = isActive;
            entity.UpdatedDate = NowPh();
            entity.UpdatedBy = updatedBy;
            await db.SaveChangesAsync();
        }

        // ── Lists ─────────────────────────────────────────────────────────

        public async Task<IEnumerable<CustomerListDto>> GetAllAsync()
        {
            await using var db = _dbFactory.CreateDbContext();

            return await db.Customers
                .AsNoTracking()
                .Select(c => new CustomerListDto
                {
                    CustomerId = c.CustomerId,
                    CustomerCode = c.CustomerCode,
                    CustomerName = c.CustomerName,
                    CustomerType = c.CustomerType,
                    SubDistributorId = c.SubDistributorId,
                    SubDistributorName = c.SubDistributor.SubdName,
                    IsActive = c.IsActive,
                    CreatedDate = c.CreatedDate
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
                .Where(c => string.IsNullOrEmpty(customerType) ||
                    (customerType == "__NOTSET__"
                        ? string.IsNullOrWhiteSpace(c.CustomerType)
                        : c.CustomerType == customerType))
                .Where(c => string.IsNullOrEmpty(status) ||
                    (status == "active" ? c.IsActive : !c.IsActive))
.Where(c => string.IsNullOrEmpty(search) ||
    c.CustomerName.Contains(search) ||
    c.CustomerCode.Contains(search) ||
    c.City != null && c.City.Contains(search) ||
    c.Province != null && c.Province.Contains(search) ||
    c.SubdCustomers.Any(m => m.SubdCustCode.Contains(search) || m.SubdCustName.Contains(search)));

            var total = await filtered.CountAsync();

            var projected = filtered.Select(c => new CustomerListDto
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                CustomerType = c.CustomerType,
                SubDistributorId = c.SubDistributorId,
                SubDistributorName = c.SubDistributor.SubdName,
                IsActive = c.IsActive,
                CreatedDate = c.CreatedDate
            });

            projected = (sortColumn, sortAscending) switch
            {
                ("CustomerCode", true) => projected.OrderBy(c => c.CustomerCode),
                ("CustomerCode", false) => projected.OrderByDescending(c => c.CustomerCode),
                ("CustomerName", true) => projected.OrderBy(c => c.CustomerName),
                ("CustomerName", false) => projected.OrderByDescending(c => c.CustomerName),
                ("CustomerType", true) => projected.OrderBy(c => c.CustomerType),
                ("CustomerType", false) => projected.OrderByDescending(c => c.CustomerType),
                ("SubDistributor", true) => projected.OrderBy(c => c.SubDistributorName),
                ("SubDistributor", false) => projected.OrderByDescending(c => c.SubDistributorName),
                ("CreatedDate", false) => projected.OrderBy(c => c.CreatedDate),
                ("CreatedDate", true) => projected.OrderByDescending(c => c.CreatedDate),
                ("IsActive", false) => projected.OrderBy(c => c.IsActive),
                ("IsActive", true) => projected.OrderByDescending(c => c.IsActive),
                _ => projected.OrderBy(c => c.CustomerName)
            };

            var items = await projected
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, total);
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
                .Include(c => c.SubdCustomers)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            return entity == null ? null : ToDetailDto(entity);
        }

        // ── Validation ────────────────────────────────────────────────────

        public async Task<bool> CustomerCodeExistsAsync(string customerCode, int subDistributorId, int? excludeCustomerId = null)
        {
            await using var db = _dbFactory.CreateDbContext();
            var code = customerCode.Trim();

            return await db.Customers.AnyAsync(c =>
                c.CustomerCode == code &&
                c.SubDistributorId == subDistributorId &&
                (excludeCustomerId == null || c.CustomerId != excludeCustomerId));
        }

        // Returns the pairs that already belong to a DIFFERENT customer in this subdistributor.
        // The database enforces the same rule (vw_SubdCustomerKey); this gives a readable message first.
        public async Task<List<SubdMappingConflictDto>> FindMappingConflictsAsync(
            int subDistributorId, IEnumerable<SubdMappingDto> mappings, int? excludeCustomerId = null)
        {
            var clean = CleanMappings(mappings);
            if (clean.Count == 0) return new();

            var codes = clean.Select(m => m.SubdCustCode!).Distinct().ToList();
            var names = clean.Select(m => m.SubdCustName!).Distinct().ToList();

            await using var db = _dbFactory.CreateDbContext();
            var candidates = await db.SubdCustomers
                .AsNoTracking()
                .Where(m => m.Customer.SubDistributorId == subDistributorId
                            && (excludeCustomerId == null || m.CustomerId != excludeCustomerId)
                            && codes.Contains(m.SubdCustCode)
                            && names.Contains(m.SubdCustName))
                .Select(m => new SubdMappingConflictDto
                {
                    SubdCustCode = m.SubdCustCode,
                    SubdCustName = m.SubdCustName,
                    CustomerId = m.CustomerId,
                    CustomerCode = m.Customer.CustomerCode,
                    CustomerName = m.Customer.CustomerName
                })
                .ToListAsync();

            // The query matches code and name separately; keep only exact pairs.
            var wanted = clean.Select(m => SubdCustomerKey.Compare(m.SubdCustCode, m.SubdCustName)).ToHashSet();
            return candidates
                .Where(c => wanted.Contains(SubdCustomerKey.Compare(c.SubdCustCode, c.SubdCustName)))
                .ToList();
        }

        // ── Import support ────────────────────────────────────────────────

        public async Task<ImportMatchResult> CheckImportDuplicateAsync(
            string customerCode, int subDistributorId, string? subdCustCode, string? subdCustName)
        {
            await using var db = _dbFactory.CreateDbContext();

            var code = customerCode.Trim();
            var customerId = await db.Customers
                .Where(c => c.SubDistributorId == subDistributorId && c.CustomerCode == code)
                .Select(c => (int?)c.CustomerId)
                .FirstOrDefaultAsync();

            var sc = SubdCustomerKey.Code(subdCustCode);
            var sn = SubdCustomerKey.Name(subdCustName);

            // Row has no subd mapping: it can only match an existing customer.
            if (sc.Length == 0 && sn.Length == 0)
                return customerId == null
                    ? new ImportMatchResult(ImportMatchType.None, null)
                    : new ImportMatchResult(ImportMatchType.ExactDuplicate, customerId);

            var owner = await db.SubdCustomers
                .Where(m => m.Customer.SubDistributorId == subDistributorId
                            && m.SubdCustCode == sc
                            && m.SubdCustName == sn)
                .Select(m => new { m.CustomerId, m.Customer.CustomerCode })
                .FirstOrDefaultAsync();

            if (owner != null)
                return owner.CustomerId == customerId
                    ? new ImportMatchResult(ImportMatchType.ExactDuplicate, customerId)
                    : new ImportMatchResult(ImportMatchType.MappingConflict, owner.CustomerId, owner.CustomerCode);

            return customerId == null
                ? new ImportMatchResult(ImportMatchType.None, null)
                : new ImportMatchResult(ImportMatchType.AddMapping, customerId);
        }

        // Adds one subd mapping to an existing customer. Does nothing if it is blank or already there.
        public async Task<CustomerDetailDto?> AddSubdMappingAsync(
            int customerId, string? subdCustCode, string? subdCustName, int? createdBy)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Customers
                .Include(c => c.SubdCustomers)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);
            if (entity == null) return null;

            var sc = SubdCustomerKey.Code(subdCustCode);
            var sn = SubdCustomerKey.Name(subdCustName);
            var key = SubdCustomerKey.Compare(sc, sn);

            if ((sc.Length > 0 || sn.Length > 0) &&
                !entity.SubdCustomers.Any(m => SubdCustomerKey.Compare(m.SubdCustCode, m.SubdCustName) == key))
            {
                entity.SubdCustomers.Add(new SubdCustomer
                {
                    SubdCustCode = sc,
                    SubdCustName = sn,
                    CreatedDate = NowPh(),
                    CreatedBy = createdBy
                });
                await db.SaveChangesAsync();
            }

            return ToDetailDto(entity);
        }

        // The customer with this code in this subdistributor (0 or 1 row), untracked.
        public async Task<List<Customer>> GetCustomersByKeyAsync(int subdistributorId, string customerCode)
        {
            await using var db = _dbFactory.CreateDbContext();
            var code = customerCode.Trim();
            return await db.Customers
                .AsNoTracking()
                .Where(c => c.SubDistributorId == subdistributorId && c.CustomerCode == code)
                .ToListAsync();
        }

        // Applies incoming address / city / province / zip / type. Blank or 0 values are skipped.
        // Returns how many customers changed (0 or 1).
        public async Task<int> UpdateGroupDetailsAsync(
            int subdistributorId, string customerCode, CustomerDetailsUpdate details, int userId)
        {
            await using var db = _dbFactory.CreateDbContext();
            var code = customerCode.Trim();

            var rows = await db.Customers
                .Where(c => c.SubDistributorId == subdistributorId && c.CustomerCode == code)
                .ToListAsync();

            var now = NowPh();
            var changed = 0;

            foreach (var c in rows)
            {
                if (details.Apply(c, commit: true).Count == 0) continue;
                c.UpdatedDate = now;
                c.UpdatedBy = userId;
                changed++;
            }

            if (changed > 0)
                await db.SaveChangesAsync();

            return changed;
        }
    }
}