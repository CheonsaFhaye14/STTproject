using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.Customer.DTOs;

namespace STTproject.Features.User.Customer.Services;

public class CustomerService : ICustomerService
{
    private readonly IDbContextFactory<SttprojectContext> _contextFactory;

    public CustomerService(IDbContextFactory<SttprojectContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CustomerListResponseDto?> GetCustomersWithBranchesAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get user's assigned sub-distributor
        var subDistributor = await context.SubDistributors
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.EncoderId == userId && s.IsActive, cancellationToken);

        if (subDistributor == null)
        {
            return null;
        }

        // Get all active customers for this sub-distributor
            var customers = await context.Customers
            .AsNoTracking()
            .Where(c => c.SubDistributorId == subDistributor.SubDistributorId && c.IsActive)
            .OrderBy(c => c.CustomerName)
            .Select(c => new CustomerInfoDto
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                CustomerType = c.CustomerType,
                IsActive = c.IsActive,
                AddressLine = c.AddressLine,
                City = c.City,
                Province = c.Province,
                ZipCode = c.ZipCode
            })
            .ToListAsync(cancellationToken);

        var subdDto = new SubDistributorInfoDto
        {
            SubDistributorId = subDistributor.SubDistributorId,
            SubdCode = subDistributor.SubdCode,
            SubdName = subDistributor.SubdName,
            CityMunicipality = subDistributor.CityMunicipality,
            Province = subDistributor.Province
        };

        return new CustomerListResponseDto
        {
            SubDistributor = subdDto,
            Customers = customers
        };
    }
}
