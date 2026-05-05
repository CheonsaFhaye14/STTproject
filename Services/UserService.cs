using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using STTproject.Models;

namespace STTproject.Services
{
    public interface UserService
    {
        Task<List<SubDistributor>> GetSubDistributorsofUserAsync(int userId, CancellationToken cancellationToken = default);

    }
public class UserServices : UserService
    {
        private readonly IDbContextFactory<SttprojectContext> _contextFactory;

        public UserServices(IDbContextFactory<SttprojectContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }
        public async Task<List<SubDistributor>> GetSubDistributorsAsync(int userId, CancellationToken cancellationToken = default)
        {
            using var tempContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await tempContext.SubDistributors
                .AsNoTracking()
                .Where(s => s.EncoderId == userId && s.IsActive)
                .OrderBy(s => s.SubdCode)
                .ToListAsync(cancellationToken);
        }

    }
 
}
