using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class TemplateRepository : ITemplateRepository
{
    private readonly TemplateBuilderDbContext _db;

    public TemplateRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task<Template?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Templates.Include(t => t.CurrentVersion).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Template?> GetByNameAsync(string name, CancellationToken ct = default)
        => await _db.Templates.Include(t => t.CurrentVersion).FirstOrDefaultAsync(t => t.Name == name, ct);

    public async Task<int?> GetCurrentVersionIdAsync(int templateId, CancellationToken ct = default)
    {
        var template = await _db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
        return template?.CurrentVersionId;
    }

    public async Task<string?> GetVersionBodyAsync(int versionId, CancellationToken ct = default)
    {
        var version = await _db.TemplateVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
        return version?.Body;
    }

    public async Task<TemplateVersion?> GetLastActiveVersionAsync(int templateId, CancellationToken ct = default)
        => await _db.TemplateVersions
            .Where(v => v.TemplateId == templateId && v.IsActive)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Template>> GetAllAsync(CancellationToken ct = default)
        => await _db.Templates.Include(t => t.CurrentVersion).OrderBy(t => t.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<TemplateVersion>> GetVersionHistoryAsync(int templateId, CancellationToken ct = default)
        => await _db.TemplateVersions.Where(v => v.TemplateId == templateId)
            .OrderByDescending(v => v.VersionNumber).ToListAsync(ct);

    public async Task<int> GetNextVersionNumberAsync(int templateId, CancellationToken ct = default)
    {
        var max = await _db.TemplateVersions.Where(v => v.TemplateId == templateId)
            .Select(v => (int?)v.VersionNumber).MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<Template> CreateAsync(Template template, CancellationToken ct = default)
    {
        if (template.ExternalKey == Guid.Empty)
            template.ExternalKey = Guid.NewGuid();
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        _db.Templates.Add(template);
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var template = await _db.Templates.FindAsync(ct, id);
        if (template is null) return false;
        template.CurrentVersionId = null;
        await _db.SaveChangesAsync(ct);
        var versions = await _db.TemplateVersions.Where(v => v.TemplateId == id).ToListAsync(ct);
        _db.TemplateVersions.RemoveRange(versions);
        _db.Templates.Remove(template);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task UpdateTemplateAsync(Template template, CancellationToken ct = default)
    {
        template.UpdatedAt = DateTime.UtcNow;
        _db.Entry(template).State = EntityState.Modified;
        await _db.SaveChangesAsync(ct);
    }

    public Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, CancellationToken ct = default)
        => PublishVersionAsync(templateId, version, null, ct);

    public async Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, Action<Template>? updateTemplate, CancellationToken ct = default)
    {
        version.CreatedAt = DateTime.UtcNow;
        version.TemplateId = templateId;

        for (var attempt = 0; ; attempt++)
        {
            var max = await _db.TemplateVersions.Where(v => v.TemplateId == templateId)
                .Select(v => (int?)v.VersionNumber).MaxAsync(ct);
            version.VersionNumber = (max ?? 0) + 1;

            using var tx = _db.Database.BeginTransaction();   // fresh transaction per attempt
            try
            {
                _db.TemplateVersions.Add(version);
                await _db.SaveChangesAsync(ct);

                var template = await _db.Templates.FirstAsync(t => t.Id == templateId, ct);
                template.CurrentVersionId = version.Id;
                template.UpdatedAt = DateTime.UtcNow;
                updateTemplate?.Invoke(template);
                await _db.SaveChangesAsync(ct);

                tx.Commit();
                return version;
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                // Unique (TemplateId, VersionNumber) violation from a concurrent publish — retry with a fresh number.
                _db.Entry(version).State = EntityState.Detached;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}