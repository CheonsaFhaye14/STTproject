using Microsoft.EntityFrameworkCore;
using STTproject.Data;
using STTproject.Features.User.Customer.DTOs;

namespace STTproject.Features.User.Customer.Services;

public class CustomerService : ICustomerService
{
    private readonly IDbContextFactory<EntrielContext> _contextFactory;

    public CustomerService(IDbContextFactory<EntrielContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CustomerListResponseDto?> GetCustomersWithBranchesAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var subDistributors = await context.SubDistributors
            .AsNoTracking()
            .Where(s => s.EncoderId == userId && s.IsActive)
            .OrderBy(s => s.SubdCode)
            .ToListAsync(cancellationToken);

        if (!subDistributors.Any())
        {
            return null;
        }

        var subdDtos = subDistributors.Select(s => new SubDistributorInfoDto
        {
            SubDistributorId = s.SubDistributorId,
            SubdCode = s.SubdCode,
            SubdName = s.SubdName,
            CityMunicipality = s.CityMunicipality ?? null,
            Province = s.Province ?? null
        }).ToList();

        var selected = subDistributors.First();

        var customers = await GetCustomersAsync(context, selected.SubDistributorId, cancellationToken);

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

        var subd = await context.SubDistributors
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SubDistributorId == subDistributorId && s.EncoderId == userId && s.IsActive, cancellationToken);

        if (subd == null)
        {
            return new List<CustomerInfoDto>();
        }

        return await GetCustomersAsync(context, subDistributorId, cancellationToken);
    }

    // One row per customer; its subd mappings come from SubdCustomer.
    private static async Task<List<CustomerInfoDto>> GetCustomersAsync(
        EntrielContext context,
        int subDistributorId,
        CancellationToken cancellationToken)
    {
        return await context.Customers
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
                Province = c.Province,
                City = c.City,
                AddressLine = c.AddressLine,
                ZipCode = c.ZipCode,
                SubdCustomers = c.SubdCustomers
                    .OrderBy(m => m.SubdCustomerId)
                    .Select(m => new SubdCustInfoDto
                    {
                        SubdCustCode = m.SubdCustCode,
                        SubdCustName = m.SubdCustName
                    })
                    .ToList()
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }
}