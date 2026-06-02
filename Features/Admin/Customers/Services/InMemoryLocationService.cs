using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace STTproject.Features.Shared.Services
{
    public class InMemoryLocationService : ILocationService
    {
        private static readonly Dictionary<string, List<string>> CityToProvinces = new()
        {
            { "Toronto", new List<string>{ "Ontario" } },
            { "Vancouver", new List<string>{ "British Columbia" } },
            { "Montreal", new List<string>{ "Quebec" } },
            // Add more mappings as needed
        };

        public Task<IEnumerable<string>> GetProvincesForCityAsync(string city)
        {
            if (string.IsNullOrWhiteSpace(city)) return Task.FromResult(Enumerable.Empty<string>());
            CityToProvinces.TryGetValue(city, out var provs);
            return Task.FromResult((IEnumerable<string>)(provs ?? new List<string>()));
        }

        public Task<IEnumerable<string>> GetCitiesAsync()
        {
            return Task.FromResult((IEnumerable<string>)CityToProvinces.Keys.ToList());
        }

        public Task<IEnumerable<string>> GetProvincesAsync()
        {
            var provinces = CityToProvinces.Values
                .SelectMany(x => x)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            return Task.FromResult((IEnumerable<string>)provinces);
        }

        public Task<IEnumerable<string>> GetCitiesForProvinceAsync(string province)
        {
            if (string.IsNullOrWhiteSpace(province)) return Task.FromResult(Enumerable.Empty<string>());

            var normalized = province.Trim();
            var cities = CityToProvinces
                .Where(kvp => kvp.Value.Any(p => string.Equals(p, normalized, System.StringComparison.OrdinalIgnoreCase)))
                .Select(kvp => kvp.Key)
                .ToList();

            return Task.FromResult((IEnumerable<string>)cities);
        }

        public Task<IEnumerable<ProvinceInfo>> GetProvinceInfosAsync()
        {
            var provinces = CityToProvinces.Values
                .SelectMany(x => x)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .Select(p => new ProvinceInfo { Name = p, Code = p })
                .ToList();

            return Task.FromResult((IEnumerable<ProvinceInfo>)provinces);
        }

        public Task<IEnumerable<string>> GetCitiesForProvinceByCodeAsync(string provinceCode)
        {
            if (string.IsNullOrWhiteSpace(provinceCode)) return Task.FromResult(Enumerable.Empty<string>());
            var normalized = provinceCode.Trim();
            var cities = CityToProvinces
                .Where(kvp => kvp.Value.Any(p => string.Equals(p.Trim(), normalized, System.StringComparison.OrdinalIgnoreCase)))
                .Select(kvp => kvp.Key)
                .ToList();

            return Task.FromResult((IEnumerable<string>)cities);
        }
    }
}
