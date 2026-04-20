using Microsoft.EntityFrameworkCore;
using STTproject.Models;

namespace STTproject.Services;

public interface IHomeService
{
    Task<List<SubDistributor>> GetSubDistributorsAsync(CancellationToken cancellationToken = default);
}

public class HomeService : IHomeService
{
    private readonly SttprojectContext _context;

    public HomeService(SttprojectContext context)
    {
        _context = context;
    }

    public Task<List<SubDistributor>> GetSubDistributorsAsync(CancellationToken cancellationToken = default)
    {
        return _context.SubDistributors
            .AsNoTracking()
            .OrderBy(s => s.SubdCode)
            .ToListAsync(cancellationToken);
    }
}
