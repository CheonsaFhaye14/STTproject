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

        var customers = await GetGroupedCustomersAsync(context, selected.SubDistributorId, cancellationToken);

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

        return await GetGroupedCustomersAsync(context, subDistributorId, cancellationToken);
    }

    // Pulls the flat customer rows for a subdistributor, then groups them by
    // (CustomerCode, CustomerName) so every SubdCust mapping for the same logical
    // customer lands together in one CustomerInfoDto.SubdCustomers list, instead
    // of each mapping producing its own duplicate customer row.
    private static async Task<List<CustomerInfoDto>> GetGroupedCustomersAsync(
        SttprojectContext context,
        int subDistributorId,
        CancellationToken cancellationToken)
    {
        var flat = await context.Customers
            .AsNoTracking()
            .Where(c => c.SubDistributorId == subDistributorId && c.IsActive)
            .Select(c => new
            {
                c.CustomerId,
                c.CustomerCode,
                c.CustomerName,
                c.CustomerType,
                c.IsActive,
                c.Province,
                c.City,
                c.AddressLine,
                c.ZipCode,
                c.SubdCustCode,
                c.SubdCustName
            })
            .ToListAsync(cancellationToken);

        return flat
            .GroupBy(c => new { c.CustomerCode, c.CustomerName })
            .Select(g => new CustomerInfoDto
            {
                CustomerId = g.Min(c => c.CustomerId),
                CustomerCode = g.Key.CustomerCode,
                CustomerName = g.Key.CustomerName,
                CustomerType = g.Select(c => c.CustomerType).FirstOrDefault(),
                IsActive = g.Select(c => c.IsActive).FirstOrDefault(),
                Province = g.Select(c => c.Province).FirstOrDefault(),
                City = g.Select(c => c.City).FirstOrDefault(),
                AddressLine = g.Select(c => c.AddressLine).FirstOrDefault(),
                ZipCode = g.Select(c => c.ZipCode).FirstOrDefault(),
                SubdCustomers = g
                    .Where(c => !string.IsNullOrWhiteSpace(c.SubdCustCode) || !string.IsNullOrWhiteSpace(c.SubdCustName))
                    .Select(c => (Code: c.SubdCustCode ?? string.Empty, Name: c.SubdCustName ?? string.Empty))
                    .Distinct()
                    .Select(x => new SubdCustInfoDto
                    {
                        SubdCustCode = x.Code,
                        SubdCustName = x.Name
                    })
                    .ToList()
            })
            .OrderBy(c => c.CustomerName)
            .ToList();
    }
}