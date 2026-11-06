using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Services;

public interface IUserService
{
    Task<bool> IsAdminAsync(
        int userId,
        CancellationToken cancellationToken = default);
}

public class UserServices : IUserService
{
    private readonly IDbContextFactory<EntrielContext> _contextFactory;

    public UserServices(IDbContextFactory<EntrielContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<bool> IsAdminAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Users
            .AnyAsync(
                u => u.UserId == userId && u.Role == "Admin",
                cancellationToken);
    }
}