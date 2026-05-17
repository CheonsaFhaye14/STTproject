
using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.Profile.DTOS;

namespace STTproject.Features.User.Profile.Services;

public interface IProfileService
{
    Task<ProfileData?> GetProfileDataAsync(int userId);
    Task<bool> UpdateProfileAsync(int userId, ProfileData updatedData);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);

}

public class ProfileService : IProfileService
{
    private readonly IDbContextFactory<SttprojectContext> _dbContextFactory;

    public ProfileService(IDbContextFactory<SttprojectContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<ProfileData?> GetProfileDataAsync(int userId)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync();
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) return null;

        return new ProfileData
        {
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Role = user.Role,
            CreatedDate = user.CreatedDate,
            UpdatedDate = user.UpdatedDate
        };
    }

    public async Task<bool> UpdateProfileAsync(int userId, ProfileData updatedData)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync();
        var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) return false;

        user.FullName = updatedData.FullName;
        user.Email = updatedData.Email;
        user.UpdatedDate = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync();
        var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) return false;

        // Here you would typically hash the passwords and compare hashes
        if (user.Password != currentPassword) return false;

        user.Password = newPassword; // In a real application, hash this password!
        user.UpdatedDate = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return true;
    }
}
