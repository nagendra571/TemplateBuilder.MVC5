namespace TemplateBuilder.Domain.Entities;

public class SnippetUsage
{
    public int Id { get; set; }
    public int SnippetId { get; set; }
    public int TemplateId { get; set; }
    public DateTime UsedAt { get; set; }
    public string? UsedBy { get; set; }
}