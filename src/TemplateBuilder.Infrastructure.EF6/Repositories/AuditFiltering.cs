using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

/// <summary>Shared AuditQuery → IQueryable filter translation (single source of truth).</summary>
internal static class AuditFiltering
{
    internal static IQueryable<AuditLog> Apply(IQueryable<AuditLog> source, AuditQuery query)
    {
        var q = source;
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
