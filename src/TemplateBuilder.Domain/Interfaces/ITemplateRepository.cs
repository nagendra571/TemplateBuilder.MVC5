using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Domain.Interfaces;

public interface ITemplateRepository
{
    Task<Template?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Template?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<int?> GetCurrentVersionIdAsync(int templateId, CancellationToken ct = default);
    Task<TemplateVersion?> GetLastActiveVersionAsync(int templateId, CancellationToken ct = default);
    Task<string?> GetVersionBodyAsync(int versionId, CancellationToken ct = default);
    Task<IReadOnlyList<Template>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TemplateVersion>> GetVersionHistoryAsync(int templateId, CancellationToken ct = default);
    Task<int> GetNextVersionNumberAsync(int templateId, CancellationToken ct = default);
    Task<Template> CreateAsync(Template template, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    Task UpdateTemplateAsync(Template template, CancellationToken ct = default);
    Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, CancellationToken ct = default);
    Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, Action<Template>? updateTemplate, CancellationToken ct = default);
}