using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Domain.Interfaces;

public interface ISnippetRepository
{
    Task<IReadOnlyList<Snippet>> GetAllAsync(CancellationToken ct = default);
    Task<Snippet?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Snippet> CreateAsync(Snippet snippet, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<Snippet> UpdateWithVersionAsync(Snippet snippet, string oldBody, string? changeComment, string actor, CancellationToken ct = default);
    Task<IReadOnlyList<SnippetVersion>> GetVersionHistoryAsync(int snippetId, CancellationToken ct = default);
    Task<SnippetVersion?> GetVersionAsync(int snippetId, int versionId, CancellationToken ct = default);
    Task<Snippet> RestoreVersionAsync(int snippetId, int sourceVersionId, string actor, CancellationToken ct = default);
    Task RecordUsageAsync(int snippetId, int templateId, string actor, CancellationToken ct = default);
    Task<IReadOnlyList<SnippetUsageStats>> GetUsageStatsAsync(CancellationToken ct = default);
}