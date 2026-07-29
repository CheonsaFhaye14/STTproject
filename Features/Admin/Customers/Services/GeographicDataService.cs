using ClosedXML.Excel;
using STTproject.Features.Admin.Customers.DTOs;

namespace STTproject.Features.Admin.Customers.Services
{
    public class GeographicDataService : IGeographicDataService
    {
        private readonly IWebHostEnvironment env;
        private readonly List<GeographicDataDto> geographicData = new();

        public GeographicDataService(IWebHostEnvironment env)
        {
            this.env = env;
        }

        public async Task InitializeAsync()
        {
            if (geographicData.Count > 0) return;
            var filePath = Path.Combine(env.WebRootPath, "data", "PHGeographicData.xlsx");
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheet(1);

            foreach (var row in worksheet.RowsUsed().Skip(1))
            {
                var city = row.Cell(1).GetString().Trim();
                var province = row.Cell(2).GetString().Trim();
                var island = row.Cell(3).GetString().Trim();
                var zipText = row.Cell(4).GetString().Trim();

                if (string.IsNullOrWhiteSpace(city) ||
                    string.IsNullOrWhiteSpace(province) ||
                    string.IsNullOrWhiteSpace(island) ||
                    string.IsNullOrWhiteSpace(zipText))
                    continue;

                if (!int.TryParse(zipText, out var zipCode))
                    continue;

                geographicData.Add(new GeographicDataDto
                {
                    CityMunicipality = city,
                    Province = province,
                    Island = island,
                    ZipCode = zipCode
                });
            }
            await Task.CompletedTask;
            Console.WriteLine($"Loaded {geographicData.Count} geographic records.");
        }

        public async Task<IReadOnlyList<string?>> GetIslandsAsync()
        {
            await InitializeAsync();
            return geographicData.Select(g => g.Island).Distinct().OrderBy(g => g).ToList();
        }

        public async Task<IReadOnlyList<string?>> GetProvincesByIslandAsync(string? island)
        {
            await InitializeAsync();
            return geographicData.Where(g => g.Island == island)
                                 .Select(g => g.Province)
                                 .Distinct()
                                 .OrderBy(g => g)
                                 .ToList();
        }

        public async Task<IReadOnlyList<string?>> GetCitiesMunicipalitiesByProvinceAsync(string? province)
        {
            await InitializeAsync();
            return geographicData.Where(g => g.Province == province)
                                 .Select(g => g.CityMunicipality)
                                 .Distinct()
                                 .OrderBy(g => g)
                                 .ToList();
        }

        public async Task<int?> GetZipCodeByCityMunicipalityAsync(string? cityMunicipality)
        {
            await InitializeAsync();
            return geographicData
                .FirstOrDefault(g => g.CityMunicipality == cityMunicipality)
                ?.ZipCode;
        }

        public async Task<string?> GetIslandByProvinceAsync(string? province)
        {
            await InitializeAsync();
            return geographicData
                .FirstOrDefault(g => g.Province == province)
                ?.Island;
        }
        public async Task<IReadOnlyList<string?>> GetCitiesMunicipalitiesByIslandAsync(string? island)
        {
            await InitializeAsync();
            return geographicData.Where(g => g.Island == island)
                                 .Select(g => g.CityMunicipality)
                                 .Distinct()
                                 .OrderBy(g => g)
                                 .ToList();
        }
        //TODO: Delete

        public async Task<string?> GetProvinceByCityAsync(string cityMunicipality)
        {
            await InitializeAsync();
            return geographicData
                .FirstOrDefault(g => g.CityMunicipality == cityMunicipality)
                ?.Province;
        }

        public async Task<IReadOnlyList<string?>> GetAllProvincesAsync()
        {
            await InitializeAsync();
            return geographicData.Select(g => g.Province).Distinct().OrderBy(g => g).ToList();
        }

        public async Task<IReadOnlyList<string?>> GetAllCitiesMunicipalitiesAsync()
        {
            await InitializeAsync();
            return geographicData.Select(g => g.CityMunicipality).Distinct().OrderBy(g => g).ToList();
        }

        public async Task<IReadOnlyList<string?>> GetAllIslandsAsync()
        {
            await InitializeAsync();
            return geographicData.Select(g => g.Island).Distinct().OrderBy(g => g).ToList();
        }

        public async Task<IReadOnlyList<string?>> GetProvinceCitiesMunicipalitiesAsync(string? province)
        {
            await InitializeAsync();
            return geographicData.Where(g => g.Province == province)
                                 .Select(g => g.CityMunicipality)
                                 .Distinct()
                                 .OrderBy(g => g)
                                 .ToList();
        }

        public async Task<IReadOnlyList<string?>> GetAllLocationsAsync(string? cityMunicipality, string? province, string? island)
        {
            await InitializeAsync();
            return geographicData.Where(g =>
                (string.IsNullOrEmpty(cityMunicipality) || g.CityMunicipality == cityMunicipality) &&
                (string.IsNullOrEmpty(province) || g.Province == province) &&
                (string.IsNullOrEmpty(island) || g.Island == island))
                .Select(g => $"{g.CityMunicipality}, {g.Province}, {g.Island}")
                .Distinct()
                .OrderBy(g => g)
                .ToList();
        }

        public async Task<GeographicDataDto?> GetGeographicDataAsync(string? province, string? cityMunicipality, string? island)
        {
            await InitializeAsync();
            return geographicData.FirstOrDefault(g =>
                (string.IsNullOrEmpty(province) || g.Province == province) &&
                (string.IsNullOrEmpty(cityMunicipality) || g.CityMunicipality == cityMunicipality) &&
                (string.IsNullOrEmpty(island) || g.Island == island));
        }

        public async Task<int> GetZipCodeAsync(string? province, string? cityMunicipality)
        {
            await InitializeAsync();
            return geographicData
                .FirstOrDefault(g => g.Province == province && g.CityMunicipality == cityMunicipality)
                ?.ZipCode ?? 0;
        }
    }
}