using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;

namespace STTproject.Features.Login.Services
{
    public interface ILoginService
    {
        Task<(bool Success, Data.User? User, string? ErrorCode)> AuthenticateAsync(string username, string password);
    }

    public class LoginService : ILoginService
    {
        private readonly IDbContextFactory<SttprojectContext> _contextFactory;
        private readonly ILogger<LoginService> _logger;

        public LoginService(IDbContextFactory<SttprojectContext> contextFactory, ILogger<LoginService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<(bool Success, Data.User? User, string? ErrorCode)> AuthenticateAsync(string username, string password)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, null, "missing");
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var user = await context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u =>
                        u.IsActive &&
                        u.Username == username &&
                        u.Password == password);

                if (user == null)
                {
                    return (false, null, "invalid");
                }

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
    }
}
