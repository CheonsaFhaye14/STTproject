namespace STTproject.Services;

public interface IUserContextService
{
    int? UserId { get; set; }
}

public sealed class UserContextService : IUserContextService
{
    public int? UserId { get; set; }
} 