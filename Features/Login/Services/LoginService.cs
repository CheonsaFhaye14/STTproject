using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.Login.Services
{
    public interface ILoginService
    {
        Task<(bool Success, Data.User? User, string? ErrorCode)> AuthenticateAsync(string username, string password);

        /// <summary>Returns the user if they still exist and are active; otherwise null.</summary>
        Task<Data.User?> GetActiveUserAsync(int userId);
    }

    public class LoginService : ILoginService
    {
        private readonly IDbContextFactory<EntrielContext> _contextFactory;
        private readonly ILogger<LoginService> _logger;

        public LoginService(IDbContextFactory<EntrielContext> contextFactory, ILogger<LoginService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<(bool Success, STTproject.Data.User? User, string? ErrorCode)> AuthenticateAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, null, "missing");

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();

                // NOTE: passwords are still compared as plain text. Switch to hashed
                // passwords (e.g. PasswordHasher<User>) when the Users table is migrated.
                var user = await context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.IsActive && u.Username == username && u.Password == password);

                if (user == null)
                    return (false, null, "invalid");

                return (true, user, null);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database error while attempting login for username '{Username}'.", username);
                return (false, null, "db");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected login error for username '{Username}'.", username);
                return (false, null, "db");
            }
        }

        public async Task<STTproject.Data.User?> GetActiveUserAsync(int userId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
        }
    }
}