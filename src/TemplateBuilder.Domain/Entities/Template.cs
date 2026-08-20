namespace TemplateBuilder.Domain.Entities;

public class Template
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SampleData { get; set; }
    public Guid ExternalKey { get; set; } = Guid.NewGuid();
    public string? SourceView { get; set; }
    public string? SourceViewSnapshot { get; set; }
    public int? CurrentVersionId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    public string? DraftBody { get; set; }
    public string? ReviewComment { get; set; }
    public ICollection<TemplateVersion> Versions { get; set; } = new List<TemplateVersion>();
    public TemplateVersion? CurrentVersion { get; set; }
}