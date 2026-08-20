using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Editor.Mvc5.Models;

public class AuditIndexViewModel
{
    public IReadOnlyList<Domain.Entities.AuditLog> Rows { get; set; } = new List<Domain.Entities.AuditLog>();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? EntityType { get; set; }
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Search { get; set; }
    public AuditStats? Stats { get; set; }
    public IReadOnlyList<string> KnownActions { get; set; } = new List<string>();
}