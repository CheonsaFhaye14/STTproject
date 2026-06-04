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
            var filePath = Path.Combine(env.ContentRootPath, "Data", "GeographicData.xlsx");
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheet(1);

            foreach (var row in worksheet.RowsUsed().Skip(1)) 
            {
                geographicData.Add(new GeographicDataDto
                {
                    Province = row.Cell(1).GetString().Trim(),
                    CityMunicipality = row.Cell(2).GetString().Trim(),
                    ZipCode = row.Cell(3).GetValue<int>()
                });
            }
            await Task.CompletedTask;
        }

        public IReadOnlyList<string?> GetProvinces()
        {
            return geographicData.Select(g => g.Province).Distinct().OrderBy(g => g).ToList();
        }
        public IReadOnlyList<string?> GetCitiesMunicipalities(string? province)
        {
            return geographicData.Where(g => g.Province == province)
                                 .Select(g => g.CityMunicipality)
                                 .Distinct()
                                 .OrderBy(g => g)
                                 .ToList();
        }
        public int? GetZipCode(string? province, string? cityMunicipality)
        {
            return geographicData.FirstOrDefault(g => g.Province == province && g.CityMunicipality == cityMunicipality)?.ZipCode;
        }

    }
}
