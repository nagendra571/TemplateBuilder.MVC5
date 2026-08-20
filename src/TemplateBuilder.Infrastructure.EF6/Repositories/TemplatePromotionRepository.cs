using System.Data.Entity;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class TemplatePromotionRepository : ITemplatePromotionRepository
{
    private readonly TemplateBuilderDbContext _db;
    public TemplatePromotionRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task<Template?> GetByExternalKeyAsync(Guid externalKey, CancellationToken ct = default)
        => await _db.Templates.FirstOrDefaultAsync(t => t.ExternalKey == externalKey, ct);

    public async Task<Template> AddWithVersionsAsync(Template template, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default)
    {
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        if (template.ExternalKey == Guid.Empty) template.ExternalKey = Guid.NewGuid();
        _db.Templates.Add(template);
        foreach (var v in versions)
        {
            v.Template = template;
            v.CreatedAt = v.CreatedAt == default ? DateTime.UtcNow : v.CreatedAt;
            _db.TemplateVersions.Add(v);
        }
        await _db.SaveChangesAsync(ct);
        template.CurrentVersionId = versions.LastOrDefault()?.Id;
        template.CurrentVersion = versions.LastOrDefault();
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<int[]> AppendVersionsAsync(int templateId, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default)
    {
        var next = await GetMaxVersionNumberAsync(templateId, ct) + 1;
        var assigned = new int[versions.Count];
        for (var i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            v.TemplateId = templateId;
            v.VersionNumber = next + i;
            v.CreatedAt = v.CreatedAt == default ? DateTime.UtcNow : v.CreatedAt;
            _db.TemplateVersions.Add(v);
            assigned[i] = next + i;
        }
        await _db.SaveChangesAsync(ct);
        return assigned;
    }

    public async Task<int[]> UpdateFromImportAsync(Template template, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default)
    {
        template.UpdatedAt = DateTime.UtcNow;
        var next = await GetMaxVersionNumberAsync(template.Id, ct) + 1;
        var assigned = new int[versions.Count];
        for (var i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            v.TemplateId = template.Id;
            v.VersionNumber = next + i;
            v.CreatedAt = v.CreatedAt == default ? DateTime.UtcNow : v.CreatedAt;
            _db.TemplateVersions.Add(v);
            assigned[i] = next + i;
        }
        await _db.SaveChangesAsync(ct);
        return assigned;
    }

    public async Task<int> GetMaxVersionNumberAsync(int templateId, CancellationToken ct = default)
        => await _db.TemplateVersions.Where(v => v.TemplateId == templateId).Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0;
}
