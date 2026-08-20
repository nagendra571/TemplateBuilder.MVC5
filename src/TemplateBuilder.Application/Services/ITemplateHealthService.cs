namespace TemplateBuilder.Application.Services;

public enum HealthSeverity { Info = 0, Warning = 1, Critical = 2 }

public class HealthFinding
{
    public HealthSeverity Severity { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public class TemplateHealthReport
{
    public int TemplateId { get; set; }
    public string? SourceView { get; set; }
    public bool ViewMissing { get; set; }
    public List<string> Tokens { get; set; } = new();
    public List<HealthFinding> Findings { get; set; } = new();
    public DateTime? SnapshotTakenAt { get; set; }
    public HealthSeverity Worst => Findings.Count == 0 ? HealthSeverity.Info : Findings.Max(f => f.Severity);
}

public interface ITemplateHealthService
{
    Task<TemplateHealthReport> CheckAsync(int templateId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ExtractModelPathsAsync(string body, CancellationToken ct = default);
    Task<string> BuildSnapshotJsonAsync(string viewName, CancellationToken ct = default);
}
