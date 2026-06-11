using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Dashboard.DTOs;

namespace STTproject.Features.Admin.Dashboard.Services
{
    public class AdminDashboardService : IAdminDashboardService
    {
        private readonly IDbContextFactory<SttprojectContext> _dbContextFactory;

        public AdminDashboardService(IDbContextFactory<SttprojectContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<List<CustomerPerSubdDto>> GetCustomersPerSubdAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();
            return await db.SubDistributors
                .AsNoTracking()
                .Select(s => new CustomerPerSubdDto
                {
                    SubDistributorId = s.SubDistributorId,
                    SubdName = s.SubdName,
                    SubdCode = s.SubdCode,
                    ActiveCount = s.Customers.Count(c => c.IsActive),
                    InactiveCount = s.Customers.Count(c => !c.IsActive),
                })
                .OrderBy(s => s.SubdName)
                .ToListAsync();
        }

        public async Task<int> GetTotalCustomersAsync()
        {
            await using var db = _dbContextFactory.CreateDbContext();
            return await db.Customers.CountAsync();
        }
    }
}