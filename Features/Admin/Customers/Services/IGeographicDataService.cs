using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public interface IGeographicDataService
    {
        Task<IReadOnlyList<string?>> GetIslandsAsync();
        Task<IReadOnlyList<string?>> GetProvincesByIslandAsync(string? island);
        Task<IReadOnlyList<string?>> GetCitiesMunicipalitiesByProvinceAsync(string? province); 
        Task<int?> GetZipCodeByCityMunicipalityAsync(string? cityMunicipality);       
        Task<string?> GetIslandByProvinceAsync(string? province);
        Task<IReadOnlyList<string?>> GetCitiesMunicipalitiesByIslandAsync(string? island);

        //TODO: Delete
        Task<IReadOnlyList<string?>> GetAllProvincesAsync();
        Task<IReadOnlyList<string?>> GetAllCitiesMunicipalitiesAsync();
        Task<IReadOnlyList<string?>> GetAllIslandsAsync();
        Task<IReadOnlyList<string?>> GetProvinceCitiesMunicipalitiesAsync(string? province);
        Task<IReadOnlyList<string?>> GetAllLocationsAsync(string? cityMunicipality, string? province, string? island);
        Task<int> GetZipCodeAsync(string? province, string? cityMunicipality);
        Task<string?> GetProvinceByCityAsync(string cityMunicipality);
        Task<GeographicDataDto?> GetGeographicDataAsync(string ? province, string ? cityMunicipality, string ? island);
    }
}
