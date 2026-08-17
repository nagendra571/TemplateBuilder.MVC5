using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Domain.Interfaces;

public interface ISnippetRepository
{
    Task<IReadOnlyList<Snippet>> GetAllAsync(CancellationToken ct = default);
    Task<Snippet?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Snippet> CreateAsync(Snippet snippet, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}