using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.Admin.Users.DTOs;

namespace STTproject.Features.Admin.Users.Services
{
    public class AdminUserService : IAdminUserService
    {
        private readonly IDbContextFactory<SttprojectContext> _dbFactory;
        private readonly IConfiguration _config;
        private static readonly TimeZoneInfo PhTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

        private static DateTime NowPh() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTimeZone);


        public AdminUserService(IDbContextFactory<SttprojectContext> dbFactory, IConfiguration config)
        {
            _dbFactory = dbFactory;
            _config = config;
        }

        public async Task<string?> GetUserNameByIdAsync(int? userId)
        {
            if (userId == null) return null;
            await using var db = _dbFactory.CreateDbContext();
            var user = await db.Users.FindAsync(userId.Value);
            return user?.FullName ?? user?.Username;
        }
        public async Task<UserListDto?> CreateUserAsync(UserCreateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();

            // Use provided password or generate a random one
            var plainPassword = !string.IsNullOrWhiteSpace(dto.Password)
                ? dto.Password
                : GenerateRandomPassword();

            var entity = new Data.User
            {
                Username = dto.UserName ?? string.Empty,
                Password = BCrypt.Net.BCrypt.HashPassword(plainPassword),
                FullName = dto.FullName ?? string.Empty,
                Role = dto.Role ?? string.Empty,
                IsActive = dto.IsActive,
                Email = dto.Email,
                CreatedDate = NowPh(),
                // CreatedBy = dto.CreatedBy,
            };
            db.Users.Add(entity);
            await db.SaveChangesAsync();
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                await SendAccountEmailAsync(
                    toEmail: dto.Email,
                    fullName: dto.FullName ?? dto.UserName ?? "",
                    username: dto.UserName ?? "",
                    plainPassword: plainPassword,
                    isNewUser: true);
            }
            return await GetUserByIdAsync(entity.UserId);
        }
        private async Task SendAccountEmailAsync(string toEmail, string fullName, string username, string plainPassword, bool isNewUser)
        {
            try
            {
                var settings = _config.GetSection("EmailSettings");

                var host = settings["Host"] ?? throw new InvalidOperationException("EmailSettings:Host is missing.");
                var port = int.Parse(settings["Port"] ?? "587");
                var smtpUser = settings["Username"] ?? throw new InvalidOperationException("EmailSettings:Username is missing.");
                var smtpPass = settings["Password"] ?? throw new InvalidOperationException("EmailSettings:Password is missing.");
                var fromName = settings["FromName"] ?? "STT Project";

                var subject = isNewUser ? "Your Account Has Been Created" : "Your Password Has Been Updated";
                var body = isNewUser
                    ? $"Hi {fullName},\n\nYour account has been created.\n\nUsername: {username}\nPassword: {plainPassword}\n\nPlease change your password after logging in."
                    : $"Hi {fullName},\n\nYour password has been updated.\n\nUsername: {username}\nNew Password: {plainPassword}\n\nIf you did not request this, contact your administrator.";

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, smtpUser));
                message.To.Add(new MailboxAddress(fullName, toEmail));
                message.Subject = subject;
                message.Body = new TextPart("plain") { Text = body };

                using var client = new SmtpClient();
                await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(smtpUser, smtpPass);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email send failed: {ex.Message}");
            }
        }
        private static string GenerateRandomPassword(int length = 12)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%";
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
        }

        public async Task<UserUpdateDto?> UpdateUserAsync(UserUpdateDto dto)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Users.FindAsync(dto.UserId);
            if (entity == null) return null;

            entity.Username = dto.UserName ?? entity.Username;
            entity.FullName = dto.FullName ?? entity.FullName;
            entity.Role = dto.Role ?? entity.Role;
            entity.IsActive = dto.IsActive;
            entity.Email = dto.Email ?? entity.Email;
            entity.UpdatedDate = NowPh();

            if (!string.IsNullOrWhiteSpace(dto.Password))
                entity.Password = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            await db.SaveChangesAsync();
            return dto;
        }

        public async Task ToggleUserStatusAsync(int id, bool isActive)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Users.FindAsync(id);
            if (entity == null) return;
            entity.IsActive = isActive;
            entity.UpdatedDate = NowPh();
            await db.SaveChangesAsync();
        }

        public async Task<IEnumerable<UserListDto>> GetAllAsync()
        {
            await using var db = _dbFactory.CreateDbContext();
            return await db.Users
                .Select(u => new UserListDto
                {
                    UserId = u.UserId,
                    UserName = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    IsActive = u.IsActive,
                    CreatedDate = u.CreatedDate,
                    UpdatedDate = u.UpdatedDate,
                    // CreatedBy = u.CreatedBy,
                    // UpdatedBy = u.UpdatedBy,
                }).ToListAsync();
        }

        public async Task<(IEnumerable<UserListDto> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? status,
            string? userType,
            int? subDistributorId,
            string? sortColumn = "UserName",
            bool sortAscending = true)
        {
            await using var db = _dbFactory.CreateDbContext();

            var query = db.Users
                .AsNoTracking()
                .Where(u => string.IsNullOrEmpty(status) ||
                    (status == "active" ? u.IsActive : !u.IsActive))
                .Where(u => string.IsNullOrEmpty(userType) || u.Role == userType)
                .Where(u => string.IsNullOrEmpty(search) ||
                    (u.Username != null && u.Username.Contains(search)) ||
                    (u.FullName != null && u.FullName.Contains(search)) ||
                    (u.Email != null && u.Email.Contains(search)));

            var total = await query.CountAsync();

            query = (sortColumn, sortAscending) switch
            {
                ("UserName", true) => query.OrderBy(u => u.Username),
                ("UserName", false) => query.OrderByDescending(u => u.Username),
                ("FullName", true) => query.OrderBy(u => u.FullName),
                ("FullName", false) => query.OrderByDescending(u => u.FullName),
                ("Role", true) => query.OrderBy(u => u.Role),
                ("Role", false) => query.OrderByDescending(u => u.Role),
                ("CreatedDate", true) => query.OrderBy(u => u.CreatedDate),
                ("CreatedDate", false) => query.OrderByDescending(u => u.CreatedDate),
                ("IsActive", true) => query.OrderBy(u => u.IsActive),
                ("IsActive", false) => query.OrderByDescending(u => u.IsActive),
                _ => query.OrderBy(u => u.Username)
            };

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new UserListDto
                {
                    UserId = u.UserId,
                    UserName = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    IsActive = u.IsActive,
                    CreatedDate = u.CreatedDate,
                    UpdatedDate = u.UpdatedDate,
                    // CreatedBy = u.CreatedBy,
                    // UpdatedBy = u.UpdatedBy,
                })
                .ToListAsync();

            return (items, total);
        }

        public async Task<UserListDto?> GetUserByIdAsync(int id)
        {
            await using var db = _dbFactory.CreateDbContext();
            var entity = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (entity == null) return null;

            return new UserListDto
            {
                UserId = entity.UserId,
                UserName = entity.Username,
                FullName = entity.FullName,
                Email = entity.Email,
                Role = entity.Role,
                IsActive = entity.IsActive,
                CreatedDate = entity.CreatedDate,
                UpdatedDate = entity.UpdatedDate,
                // CreatedBy = entity.CreatedBy,
                // UpdatedBy = entity.UpdatedBy,
            };
        }
    }
}