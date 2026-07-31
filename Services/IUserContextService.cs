using Microsoft.AspNetCore.Http;

namespace STTproject.Services;

public interface IUserContextService
{
    int? UserId { get; set; }
    string? Username { get; set; }
}

public sealed class UserContextService : IUserContextService
{
    public const string UserIdCookieName = "sttproject_userid";
    public const string UsernameCookieName = "sttproject_username";

    public UserContextService(IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;

        if (httpContext?.Request.Cookies.TryGetValue(UserIdCookieName, out var idValue) == true &&
            int.TryParse(idValue, out var userId))
        {
            UserId = userId;
        }

        if (httpContext?.Request.Cookies.TryGetValue(UsernameCookieName, out var username) == true)
        {
            Username = username;
        }
    }

    public int? UserId { get; set; }
    public string? Username { get; set; }
}