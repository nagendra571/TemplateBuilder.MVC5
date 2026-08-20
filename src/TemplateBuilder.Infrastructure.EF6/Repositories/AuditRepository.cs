using System.Data.Entity;
using System.Linq;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly TemplateBuilderDbContext _db;

    public AuditRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task AddAsync(AuditLog entry, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DateTime?> GetLastOccurrenceAsync(string entityType, int entityId, string action, CancellationToken ct = default)
        => await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId && a.Action == action)
            .Select(a => (DateTime?)a.OccurredAt)
            .OrderByDescending(a => a)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AuditLog>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var q = AuditFiltering.Apply(_db.AuditLogs, query);
        q = q.OrderByDescending(a => a.OccurredAt);
        var rows = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return rows;
    }

    public async Task<int> CountAsync(AuditQuery query, CancellationToken ct = default)
        => await AuditFiltering.Apply(_db.AuditLogs, query).CountAsync(ct);
}