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
        // Get all active sub-distributors for this user
        var subDistributors = await context.SubDistributors
            .AsNoTracking()
            .Where(s => s.EncoderId == userId && s.IsActive)
            .OrderBy(s => s.SubdCode)
            .ToListAsync(cancellationToken);

        if (!subDistributors.Any())
        {
            return null;
        }

        // Map sub-distributors
        var subdDtos = subDistributors.Select(s => new SubDistributorInfoDto
        {
            SubDistributorId = s.SubDistributorId,
            SubdCode = s.SubdCode,
            SubdName = s.SubdName,
            CityMunicipality = s.CityMunicipality,
            Province = s.Province
        }).ToList();

        // Choose the first sub-distributor as default
        var selected = subDistributors.First();

        // Get all active customers for the selected sub-distributor
        var customers = await context.Customers
            .AsNoTracking()
            .Where(c => c.SubDistributorId == selected.SubDistributorId && c.IsActive)
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

        var subdDto = subdDtos.First(s => s.SubDistributorId == selected.SubDistributorId);

        return new CustomerListResponseDto
        {
            SubDistributor = subdDto,
            SubDistributors = subdDtos,
            Customers = customers
        };
    }

    public async Task<List<CustomerInfoDto>> GetCustomersForSubDistributorAsync(int userId, int subDistributorId, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Ensure the sub-distributor belongs to the user and is active
        var subd = await context.SubDistributors
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SubDistributorId == subDistributorId && s.EncoderId == userId && s.IsActive, cancellationToken);

        if (subd == null)
        {
            return new List<CustomerInfoDto>();
        }

        var customers = await context.Customers
            .AsNoTracking()
            .Where(c => c.SubDistributorId == subDistributorId && c.IsActive)
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

        return customers;
    }
}
