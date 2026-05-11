using Microsoft.AspNetCore.Http;

namespace STTproject.Services;

public interface IUserContextService
{
    int? UserId { get; set; }
}

public sealed class UserContextService : IUserContextService
{
    public const string UserIdCookieName = "sttproject_userid";

    public UserContextService(IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Request.Cookies.TryGetValue(UserIdCookieName, out var value) == true &&
            int.TryParse(value, out var userId))
        {
            UserId = userId;
        }
    }

    public int? UserId { get; set; }
}