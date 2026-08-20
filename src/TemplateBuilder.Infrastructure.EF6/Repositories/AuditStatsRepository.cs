using System.Data.Entity;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class AuditStatsRepository : IAuditStatsRepository
{
    private const int DefaultWindowDays = 30;
    private readonly TemplateBuilderDbContext _db;

    public AuditStatsRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task<AuditStats> GetStatsAsync(AuditQuery query, CancellationToken ct = default)
    {
        var filtered = AuditFiltering.Apply(_db.AuditLogs, query);

        // EF6 forbids concurrent async operations on a single DbContext — run sequentially.
        var total = await filtered.CountAsync(ct);
        var templateCount = await filtered.CountAsync(a => a.EntityType == "Template", ct);
        var snippetCount = await filtered.CountAsync(a => a.EntityType == "Snippet", ct);
        var uniqueActors = await filtered.Select(a => a.Actor).Distinct().CountAsync(ct);
        var first = await filtered.MinAsync(a => (DateTime?)a.OccurredAt, ct);
        var last = await filtered.MaxAsync(a => (DateTime?)a.OccurredAt, ct);

        var (start, end) = ResolveWindow(query, last);
        var buckets = await ComputeDailyBucketsAsync(query, start, end, ct);

        return new AuditStats
        {
            Total = total,
            TemplateCount = templateCount,
            SnippetCount = snippetCount,
            UniqueActors = uniqueActors,
            FirstOccurrence = first,
            LastOccurrence = last,
            DailyBuckets = buckets
        };
    }

    private (DateTime start, DateTime end) ResolveWindow(AuditQuery query, DateTime? lastOccurrence)
    {
        if (query.From.HasValue || query.To.HasValue)
        {
            var end = query.To.HasValue ? query.To.Value.Date : lastOccurrence?.Date ?? DateTime.UtcNow.Date;
            var start = query.From.HasValue ? query.From.Value.Date : end.AddDays(-(DefaultWindowDays - 1));
            if (start > end) return (end, end);
            return (start, end);
        }
        return (DateTime.UtcNow.Date.AddDays(-(DefaultWindowDays - 1)), DateTime.UtcNow.Date);
    }

    private async Task<IReadOnlyList<AuditDailyBucket>> ComputeDailyBucketsAsync(AuditQuery query, DateTime start, DateTime end, CancellationToken ct)
    {
        var endExclusive = end.AddDays(1);
        var sparse = await AuditFiltering.Apply(_db.AuditLogs, query)
            .Where(a => a.OccurredAt >= start && a.OccurredAt < endExclusive)
            .GroupBy(a => DbFunctions.TruncateTime(a.OccurredAt))
            .Select(g => new { Date = g.Key.Value, Count = g.Count() })
            .ToListAsync(ct);

        var byDate = sparse.ToDictionary(s => s.Date.Date, s => s.Count);
        var buckets = new List<AuditDailyBucket>();
        for (var day = start; day <= end; day = day.AddDays(1))
            buckets.Add(new AuditDailyBucket { Date = day, Count = byDate.TryGetValue(day, out var c) ? c : 0 });
        return buckets;
    }
}
