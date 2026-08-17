namespace TemplateBuilder.Domain.Entities;

public class TemplateVersion
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ChangeComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public Template Template { get; set; } = null!;
}