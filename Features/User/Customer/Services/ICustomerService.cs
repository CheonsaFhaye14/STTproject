using STTproject.Features.User.Customer.DTOs;

namespace STTproject.Features.User.Customer.Services;

public interface ICustomerService
{
    Task<CustomerListResponseDto?> GetCustomersWithBranchesAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<CustomerInfoDto>> GetCustomersForSubDistributorAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default);
}
