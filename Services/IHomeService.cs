using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface IHomeService
{
    Task<List<SubDistributor>> GetSubDistributorsAsync(int userId, CancellationToken cancellationToken = default);
    Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
}

public class HomeService : IHomeService
{
    private readonly SttprojectContext _context;

    public HomeService(SttprojectContext context)
    {
        _context = context;
    }

    public Task<List<SubDistributor>> GetSubDistributorsAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _context.SubDistributors
            .AsNoTracking()
            .Where(s => s.EncoderId == userId && s.IsActive)
            .OrderBy(s => s.SubdCode)
            .ToListAsync(cancellationToken);
    }

    public Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
    }
}
