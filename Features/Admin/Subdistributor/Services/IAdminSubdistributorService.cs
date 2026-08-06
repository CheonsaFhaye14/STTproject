using STTproject.Features.Admin.Subdistributor.DTOs;
using STTproject.Features.Admin.Users.DTOs;

namespace STTproject.Features.Admin.Subdistributor.Services
{
    public interface IAdminSubDistributorService
    {
        Task<SubDistributorListDto?> CreateSubDistributorAsync(SubDistributorCreateDto dto);
        Task<SubDistributorUpdateDto?> UpdateSubDistributorAsync(SubDistributorUpdateDto dto);
        Task ToggleSubDistributorStatusAsync(int id, bool isActive);
        Task<IEnumerable<SubDistributorListDto>> GetAllAsync();
        Task<(IEnumerable<SubDistributorListDto> Items, int TotalCount)> GetPagedAsync(
            int page, int pageSize, string? search, string? status,
            string? province, string? sortColumn = "SubdName", bool sortAscending = true);
        Task<SubDistributorListDto?> GetSubDistributorByIdAsync(int id);

        // Encoder assignment — pulled from Users, restricted to Role == "Encoder"
        Task<IEnumerable<UserListDto>> GetEncoderUsersAsync();
        Task<bool> IsValidEncoderAsync(int userId);
    }
}