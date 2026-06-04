

namespace STTproject.Features.Admin.Customers.Services
{
    public interface IGeographicDataService
    {
    Task InitializeAsync();
    IReadOnlyList<string?> GetProvinces();
    IReadOnlyList<string?> GetCitiesMunicipalities(string? province);
    int? GetZipCode(string? province, string? cityMunicipality);
    }

}
