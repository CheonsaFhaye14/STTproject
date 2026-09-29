using STTproject.Features.Admin.ImportTemplate.DTOs;
namespace STTproject.Features.Admin.ImportTemplate.Services
{
    public interface IImportTemplateService
    {
        Task<List<ImportTemplateListItemDto>> GetListAsync(ImportTemplateFilterDto filter, CancellationToken ct = default);
        Task<ImportTemplateEditDto?> GetAsync(int importTemplateId, CancellationToken ct = default);
        Task<ImportTemplateSaveResult> SaveAsync(ImportTemplateEditDto dto, int userId, CancellationToken ct = default);
        Task<ImportTemplateSaveResult> CloneAsync(CloneImportTemplateDto dto, int userId, CancellationToken ct = default);
        Task<ImportTemplateSaveResult> SetActiveAsync(int importTemplateId, bool isActive, int userId, CancellationToken ct = default);
        Task<List<SubDistributorOptionDto>> GetSubDistributorsAsync(CancellationToken ct = default);
        Task<List<string>> GetPrincipalsAsync(CancellationToken ct = default);
    
    }
}