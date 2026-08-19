namespace TemplateBuilder.Domain.Entities;

public class SnippetVersion
{
    public int Id { get; set; }
    public int SnippetId { get; set; }
    public int VersionNumber { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ChangeComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public Snippet Snippet { get; set; } = null!;
}