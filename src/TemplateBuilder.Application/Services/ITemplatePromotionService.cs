namespace TemplateBuilder.Application.Services;

public class ExporterInfo
{
    public string Name { get; set; } = "TemplateBuilder.Editor.Mvc5";
    public string Version { get; set; } = "1.2.0";
}

public class TemplateExportVersion
{
    public int VersionNumber { get; set; }
    public string Body { get; set; } = "";
    public string? ChangeComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TemplateExportTemplate
{
    public Guid ExternalKey { get; set; }
    public string Name { get; set; } = "";
    public string TemplateType { get; set; } = "";
    public string? Description { get; set; }
    public string? SampleData { get; set; }
    public bool IsActive { get; set; }
    public List<TemplateExportVersion> Versions { get; set; } = new();
}

public class TemplateExportDocument
{
    public int SchemaVersion { get; set; } = 2;
    public ExporterInfo Exporter { get; set; } = new();
    public DateTime ExportedAt { get; set; }
    public TemplateExportTemplate Template { get; set; } = new();
}

public class TemplateImportEntry
{
    public string? Name { get; set; }
    public string? Reason { get; set; }
    public Guid ExternalKey { get; set; }
    public int VersionsAppended { get; set; }
}

public class TemplateImportResult
{
    public List<TemplateImportEntry> Created { get; set; } = new();
    public List<TemplateImportEntry> Updated { get; set; } = new();
    public List<TemplateImportEntry> Skipped { get; set; } = new();
    public List<TemplateImportEntry> Errors { get; set; } = new();
}

public interface ITemplatePromotionService
{
    Task<TemplateExportDocument?> BuildExportAsync(int templateId, CancellationToken ct = default);
    string SerializeExport(TemplateExportDocument document);
    string SanitizeFileName(string name);
    Task<TemplateImportResult> ImportAsync(byte[] fileBytes, string actor, CancellationToken ct = default);
    Task<byte[]> BuildBulkZipAsync(IReadOnlyList<int> templateIds, CancellationToken ct = default);
}
