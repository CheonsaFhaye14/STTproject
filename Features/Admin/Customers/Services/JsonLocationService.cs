using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace STTproject.Features.Shared.Services
{
    public class JsonLocationService : ILocationService
    {
        private readonly IWebHostEnvironment env;
        private readonly Lazy<Task<GeoRoot>> loader;

        public JsonLocationService(IWebHostEnvironment env)
        {
            this.env = env ?? throw new ArgumentNullException(nameof(env));
            loader = new Lazy<Task<GeoRoot>>(LoadAsync);
        }

        private async Task<GeoRoot> LoadAsync()
        {
            var webRoot = string.IsNullOrWhiteSpace(env.WebRootPath) ? env.ContentRootPath : env.WebRootPath;
            var path = Path.Combine(webRoot, "data", "PHGeographicData.json");

            if (!File.Exists(path)) return new GeoRoot();

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = JsonSerializer.Deserialize<GeoRoot>(json, options);
                return root ?? new GeoRoot();
            }
            catch
            {
                return new GeoRoot();
            }
        }

        public async Task<IEnumerable<string>> GetProvincesForCityAsync(string city)
        {
            if (string.IsNullOrWhiteSpace(city)) return Enumerable.Empty<string>();
            var data = await loader.Value;
            var match = (data.Cities ?? Enumerable.Empty<City>())
                .FirstOrDefault(c => string.Equals(c.Name?.Trim(), city.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null) return Enumerable.Empty<string>();

            if (!string.IsNullOrWhiteSpace(match.ProvinceCode))
            {
                var prov = (data.Provinces ?? Enumerable.Empty<Province>())
                    .FirstOrDefault(p => string.Equals(p.Code, match.ProvinceCode, StringComparison.OrdinalIgnoreCase));
                if (prov != null) return new[] { prov.Name ?? string.Empty }.Where(n => !string.IsNullOrWhiteSpace(n));
            }

            return Enumerable.Empty<string>();
        }

        public async Task<IEnumerable<string>> GetCitiesAsync()
        {
            var data = await loader.Value;
            return (data.Cities ?? Enumerable.Empty<City>())
            .Select(c => c.Name?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
        }

        public async Task<IEnumerable<string>> GetProvincesAsync()
        {
            var data = await loader.Value;
            return (data.Provinces ?? Enumerable.Empty<Province>())
            .Select(p => p.Name?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
        }

        public async Task<IEnumerable<ProvinceInfo>> GetProvinceInfosAsync()
        {
            var data = await loader.Value;
            return (data.Provinces ?? Enumerable.Empty<Province>())
                .Select(p => new ProvinceInfo { Name = p.Name?.Trim(), Code = p.Code?.Trim() })
                .Where(pi => !string.IsNullOrWhiteSpace(pi.Name))
                .GroupBy(pi => (Name: pi.Name ?? string.Empty, Code: pi.Code ?? string.Empty))
                .Select(g => g.First())
                .OrderBy(pi => pi.Name)
                .ToList();
        }

        public async Task<IEnumerable<string>> GetCitiesForProvinceAsync(string province)
        {
            if (string.IsNullOrWhiteSpace(province)) return Enumerable.Empty<string>();
            var data = await loader.Value;
            var prov = (data.Provinces ?? Enumerable.Empty<Province>())
                .FirstOrDefault(p => string.Equals(p.Name?.Trim(), province.Trim(), StringComparison.OrdinalIgnoreCase));

            if (prov == null || string.IsNullOrWhiteSpace(prov.Code)) return Enumerable.Empty<string>();

            return (data.Cities ?? Enumerable.Empty<City>())
                .Where(c => string.Equals(c.ProvinceCode?.Trim(), prov.Code.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }

        public async Task<IEnumerable<string>> GetCitiesForProvinceByCodeAsync(string provinceCode)
        {
            if (string.IsNullOrWhiteSpace(provinceCode)) return Enumerable.Empty<string>();
            var data = await loader.Value;

            return (data.Cities ?? Enumerable.Empty<City>())
                .Where(c => string.Equals(c.ProvinceCode?.Trim(), provinceCode.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }

        private class GeoRoot
        {
            public List<Region> Regions { get; set; } = new();
            public List<Province> Provinces { get; set; } = new();
            public List<City> Cities { get; set; } = new();
        }

        private class Region { public string? Name { get; set; } public string? Code { get; set; } public int? Id { get; set; } }
        private class Province { public string? Name { get; set; } public string? RegionCode { get; set; } public string? Code { get; set; } public int? Id { get; set; } }
        private class City { public string? Name { get; set; } public string? RegionCode { get; set; } public string? Code { get; set; } public int? Id { get; set; } [JsonPropertyName("province_code")] public string? ProvinceCode { get; set; } }
    }
}
