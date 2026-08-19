using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class AuditService : IAuditService
{
    private static readonly TimeSpan DraftSaveThrottle = TimeSpan.FromMinutes(5);

    private readonly IAuditRepository _repository;

    public AuditService(IAuditRepository repository) => _repository = repository;

    public async Task RecordAsync(string entityType, int entityId, string action, string actor,
        string? beforeState = null, string? afterState = null, string? comment = null,
        CancellationToken ct = default)
    {
        if (action == AuditActions.DraftSaved)
        {
            var last = await _repository.GetLastOccurrenceAsync(entityType, entityId, action, ct);
            if (last.HasValue && DateTime.UtcNow - last.Value < DraftSaveThrottle)
                return;
        }

        await _repository.AddAsync(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Actor = actor,
            OccurredAt = DateTime.UtcNow,
            BeforeState = beforeState,
            AfterState = afterState,
            Comment = comment
        }, ct);
    }
}