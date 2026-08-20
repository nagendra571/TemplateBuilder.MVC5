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
        var q = ApplyFilters(query);
        q = q.OrderByDescending(a => a.OccurredAt);
        var rows = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return rows;
    }

    public async Task<int> CountAsync(AuditQuery query, CancellationToken ct = default)
        => await ApplyFilters(query).CountAsync(ct);

    private IQueryable<AuditLog> ApplyFilters(AuditQuery query)
    {
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (query.EntityId.HasValue) q = q.Where(a => a.EntityId == query.EntityId.Value);
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.Actor)) q = q.Where(a => a.Actor.Contains(query.Actor));
        if (query.From.HasValue) q = q.Where(a => a.OccurredAt >= query.From.Value);
        if (query.To.HasValue)
        {
            var toExclusive = query.To.Value.Date.AddDays(1);
            q = q.Where(a => a.OccurredAt < toExclusive);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.Action.Contains(query.Search) || a.Actor.Contains(query.Search) || (a.Comment != null && a.Comment.Contains(query.Search)));
        return q;
    }
}