using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Domain.Interfaces;

public class AuditQuery
{
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Search { get; set; }   // matches Action, Actor, Comment
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public interface IAuditRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct = default);
    Task<DateTime?> GetLastOccurrenceAsync(string entityType, int entityId, string action, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> QueryAsync(AuditQuery query, CancellationToken ct = default);
    Task<int> CountAsync(AuditQuery query, CancellationToken ct = default);
}

public record SnippetUsageStats(int SnippetId, int UsageCount, int TemplateCount, DateTime? LastUsedAt);