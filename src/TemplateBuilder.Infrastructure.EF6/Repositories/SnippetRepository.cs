using System.Data.Entity;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class SnippetRepository : ISnippetRepository
{
    private readonly TemplateBuilderDbContext _db;

    public SnippetRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task<IReadOnlyList<Snippet>> GetAllAsync(CancellationToken ct = default)
        => await _db.Snippets.OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<Snippet?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Snippets.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Snippet> CreateAsync(Snippet snippet, CancellationToken ct = default)
    {
        snippet.CreatedAt = DateTime.UtcNow;
        snippet.UpdatedAt = DateTime.UtcNow;
        _db.Snippets.Add(snippet);
        await _db.SaveChangesAsync(ct);
        return snippet;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var snippet = await _db.Snippets.FindAsync(ct, id);
        if (snippet is not null)
        {
            _db.Snippets.Remove(snippet);
            await _db.SaveChangesAsync(ct);
        }
    }
}