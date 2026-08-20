using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Domain.Interfaces;

public interface ITemplatePromotionRepository
{
    Task<Template?> GetByExternalKeyAsync(Guid externalKey, CancellationToken ct = default);
    Task<Template> AddWithVersionsAsync(Template template, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default);
    Task<int[]> AppendVersionsAsync(int templateId, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default);
    Task<int[]> UpdateFromImportAsync(Template template, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default);
    Task<int> GetMaxVersionNumberAsync(int templateId, CancellationToken ct = default);
}
