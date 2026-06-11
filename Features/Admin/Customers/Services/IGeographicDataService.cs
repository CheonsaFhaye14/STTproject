
namespace STTproject.Features.Admin.Customers.Services
{
    public interface IGeographicDataService
    {
        Task<IReadOnlyList<string?>> GetAllProvincesAsync();
        Task<IReadOnlyList<string?>> GetAllCitiesMunicipalitiesAsync();
        Task<IReadOnlyList<string?>> GetProvinceCitiesMunicipalitiesAsync(string? province);
        Task<int> GetZipCodeAsync(string? province, string? cityMunicipality);
        Task<string?> GetProvinceByCityAsync(string cityMunicipality);
    }

}
