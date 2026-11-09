using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace STTproject.Services;

public interface IUserContextService
{
    int? UserId { get; set; }
    string? Username { get; set; }
    string? Role { get; set; }
}

public sealed class UserContextService : IUserContextService
{
    /// <summary>Name of the signed/encrypted auth cookie issued by cookie authentication.</summary>
    public const string AuthCookieName = "sttproject_auth";

    // Old plain-text cookies from the previous login system. Kept only so they can be deleted.
    public const string UserIdCookieName = "sttproject_userid";
    public const string UsernameCookieName = "sttproject_username";

    public UserContextService(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            UserId = userId;
        }

        Username = user.FindFirstValue(ClaimTypes.Name);
        Role = user.FindFirstValue(ClaimTypes.Role);
    }

    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string? Role { get; set; }

    /// <summary>Removes the old unsigned cookies left over from the previous login system.</summary>
    public static void DeleteLegacyCookies(HttpContext httpContext)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        };

        httpContext.Response.Cookies.Delete(UserIdCookieName, options);
        httpContext.Response.Cookies.Delete(UsernameCookieName, options);
    }
}