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
                var province = row.Cell(1).GetString().Trim();
                var city = row.Cell(2).GetString().Trim();
                var zipText = row.Cell(3).GetString().Trim();

                if (string.IsNullOrWhiteSpace(province) ||
                    string.IsNullOrWhiteSpace(city) ||
                    string.IsNullOrWhiteSpace(zipText))
                {
                    continue;
                }

                if (!int.TryParse(zipText, out var zipCode))
                {
                    continue;
                }

                geographicData.Add(new GeographicDataDto
                {
                    Province = province,
                    CityMunicipality = city,
                    ZipCode = zipCode
                });
            }
            await Task.CompletedTask;
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

        public async Task<IReadOnlyList<string?>> GetProvinceCitiesMunicipalitiesAsync(string? province)
        {
            await InitializeAsync();
            return geographicData.Where(g => g.Province == province)
                                 .Select(g => g.CityMunicipality)
                                 .Distinct()
                                 .OrderBy(g => g)
                                 .ToList();
        }
        public async Task<int> GetZipCodeAsync(string? province, string? cityMunicipality)
        {
            await InitializeAsync();
            return geographicData.FirstOrDefault(g => g.Province == province && g.CityMunicipality == cityMunicipality)?.ZipCode ?? 0;
        }

    }
}
