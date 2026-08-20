using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class AuditDailyBucket
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class AuditStats
{
    public int Total { get; set; }
    public int TemplateCount { get; set; }
    public int SnippetCount { get; set; }
    public int UniqueActors { get; set; }
    public DateTime? FirstOccurrence { get; set; }
    public DateTime? LastOccurrence { get; set; }
    public IReadOnlyList<AuditDailyBucket> DailyBuckets { get; set; } = new List<AuditDailyBucket>();
}

/// <summary>
/// Fork-specific (Editor.Mvc5) aggregate statistics over the audit log — kept out of
/// Domain/Application to preserve those projects as verbatim ports of the origin repo.
/// </summary>
public interface IAuditStatsRepository
{
    Task<AuditStats> GetStatsAsync(AuditQuery query, CancellationToken ct = default);
}
