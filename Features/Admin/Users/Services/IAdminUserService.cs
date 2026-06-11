using STTproject.Features.Admin.Users.DTOs;

namespace STTproject.Features.Admin.Users.Services
{
    public interface IAdminUserService
    {
        Task<UserListDto?> CreateUserAsync(UserCreateDto dto);
        Task<UserUpdateDto?> UpdateUserAsync(UserUpdateDto dto);
        Task ToggleUserStatusAsync(int id, bool isActive);
        Task<IEnumerable<UserListDto>> GetAllAsync();
        Task<(IEnumerable<UserListDto> Items, int TotalCount)> GetPagedAsync(
            int page, int pageSize, string? search, string? status,
            string? userType, int? subDistributorId,
            string? sortColumn = "UserName", bool sortAscending = true);
        Task<UserListDto?> GetUserByIdAsync(int id);
        Task<string?> GetUserNameByIdAsync(int? userId);

    }
}