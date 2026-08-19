namespace TemplateBuilder.Domain.Entities;

public class Snippet
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<SnippetVersion> Versions { get; set; } = new List<SnippetVersion>();
}