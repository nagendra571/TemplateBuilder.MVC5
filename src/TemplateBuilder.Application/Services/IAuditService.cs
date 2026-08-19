using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Application.Services;

public interface IAuditService
{
    Task RecordAsync(string entityType, int entityId, string action, string actor,
        string? beforeState = null, string? afterState = null, string? comment = null,
        CancellationToken ct = default);
}