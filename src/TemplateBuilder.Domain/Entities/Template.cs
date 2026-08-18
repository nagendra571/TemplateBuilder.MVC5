namespace TemplateBuilder.Domain.Entities;

public class Template
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SampleData { get; set; }
    public int? CurrentVersionId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<TemplateVersion> Versions { get; set; } = new List<TemplateVersion>();
    public TemplateVersion? CurrentVersion { get; set; }
}