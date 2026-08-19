namespace TemplateBuilder.Domain.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;   // "Template" | "Snippet"
    public int EntityId { get; set; }                        // no FK — survives hard deletes
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? Comment { get; set; }
}