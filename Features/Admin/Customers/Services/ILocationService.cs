using System.Collections.Generic;
using System.Threading.Tasks;

namespace STTproject.Features.Shared.Services
{
    public interface ILocationService
    {
        Task<IEnumerable<string>> GetProvincesForCityAsync(string city);
        Task<IEnumerable<string>> GetCitiesAsync();
        Task<IEnumerable<string>> GetProvincesAsync();
        Task<IEnumerable<string>> GetCitiesForProvinceAsync(string province);
        Task<IEnumerable<ProvinceInfo>> GetProvinceInfosAsync();
        Task<IEnumerable<string>> GetCitiesForProvinceByCodeAsync(string provinceCode);
    }
    public class ProvinceInfo
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
    }
}
