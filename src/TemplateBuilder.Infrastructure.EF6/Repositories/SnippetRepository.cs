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

    public async Task<Snippet> UpdateWithVersionAsync(Snippet snippet, string oldBody, string? changeComment, string actor, CancellationToken ct = default)
    {
        snippet.UpdatedAt = DateTime.UtcNow;

        using var tx = _db.Database.BeginTransaction();
        try
        {
            if (!string.Equals(snippet.Body, oldBody, StringComparison.Ordinal))
            {
                var max = await _db.SnippetVersions.Where(v => v.SnippetId == snippet.Id)
                    .Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0;
                if (max == 0)
                {
                    _db.SnippetVersions.Add(new SnippetVersion
                    {
                        SnippetId = snippet.Id,
                        VersionNumber = 1,
                        Body = oldBody,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = actor
                    });
                }
                _db.SnippetVersions.Add(new SnippetVersion
                {
                    SnippetId = snippet.Id,
                    VersionNumber = max + (max == 0 ? 2 : 1),
                    Body = snippet.Body,
                    ChangeComment = changeComment,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = actor
                });
            }

            _db.Entry(snippet).State = EntityState.Modified;
            await _db.SaveChangesAsync(ct);
            tx.Commit();
            return snippet;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<SnippetVersion>> GetVersionHistoryAsync(int snippetId, CancellationToken ct = default)
        => await _db.SnippetVersions
            .Where(v => v.SnippetId == snippetId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync(ct);

    public async Task<SnippetVersion?> GetVersionAsync(int snippetId, int versionId, CancellationToken ct = default)
        => await _db.SnippetVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.SnippetId == snippetId, ct);

    public async Task<Snippet> RestoreVersionAsync(int snippetId, int sourceVersionId, string actor, CancellationToken ct = default)
    {
        var source = await GetVersionAsync(snippetId, sourceVersionId, ct);
        if (source is null)
            throw new InvalidOperationException($"Version {sourceVersionId} not found for snippet {snippetId}.");

        var snippet = await GetByIdAsync(snippetId, ct);
        if (snippet is null)
            throw new InvalidOperationException($"Snippet {snippetId} not found.");

        var oldBody = snippet.Body;
        snippet.Body = source.Body;
        return await UpdateWithVersionAsync(snippet, oldBody, $"Restored from v{source.VersionNumber}", actor, ct);
    }

    public async Task RecordUsageAsync(int snippetId, int templateId, string actor, CancellationToken ct = default)
    {
        _db.SnippetUsages.Add(new SnippetUsage
        {
            SnippetId = snippetId,
            TemplateId = templateId,
            UsedAt = DateTime.UtcNow,
            UsedBy = actor
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SnippetUsageStats>> GetUsageStatsAsync(CancellationToken ct = default)
    {
        var grouped = await _db.SnippetUsages
            .GroupBy(u => u.SnippetId)
            .Select(g => new
            {
                SnippetId = g.Key,
                UsageCount = g.Count(),
                TemplateCount = g.Select(x => x.TemplateId).Distinct().Count(),
                LastUsedAt = (DateTime?)g.Max(x => x.UsedAt)
            })
            .ToListAsync(ct);

        return grouped.Select(g => new SnippetUsageStats(g.SnippetId, g.UsageCount, g.TemplateCount, g.LastUsedAt))
            .Cast<SnippetUsageStats>()
            .ToList();
    }
}