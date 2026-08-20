using Newtonsoft.Json;
using Scriban.Syntax;
using TemplateBuilder.Application.DTOs;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class TemplateHealthService : ITemplateHealthService
{
    private readonly ITemplateRepository _repository;
    private readonly ISqlViewDiscoveryService _discovery;

    public TemplateHealthService(ITemplateRepository repository, ISqlViewDiscoveryService discovery)
    {
        _repository = repository;
        _discovery = discovery;
    }

    public Task<IReadOnlyList<string>> ExtractModelPathsAsync(string body, CancellationToken ct = default)
    {
        var parsed = Scriban.Template.Parse(body);
        if (parsed.HasErrors) return Task.FromResult<IReadOnlyList<string>>(new List<string>());
        var members = new List<ScriptMemberExpression>();
        Collect(parsed.Page.Children, members);
        var leaves = members.Where(m => !members.Any(other => other != m && IsInTargetChain(other.Target, m))).ToList();
        var paths = leaves.Select(ToPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult<IReadOnlyList<string>>(paths);
    }

    private static void Collect(IEnumerable<ScriptNode> nodes, List<ScriptMemberExpression> acc)
    {
        foreach (var node in nodes)
        {
            if (node is ScriptMemberExpression m && IsRootedAtModel(m)) acc.Add(m);
            if (node.ChildrenCount > 0) Collect(node.Children, acc);
        }
    }

    private static bool IsRootedAtModel(ScriptMemberExpression m)
        => m.Target is ScriptVariable sv && sv.Name == "model"
           || (m.Target is ScriptMemberExpression inner && IsRootedAtModel(inner));

    private static bool IsInTargetChain(ScriptExpression target, ScriptMemberExpression needle)
        => ReferenceEquals(target, needle) || (target is ScriptMemberExpression inner && IsInTargetChain(inner.Target, needle));

    private static string ToPath(ScriptMemberExpression m)
        => m.Target is ScriptVariable v ? m.Member.Name : ToPath((ScriptMemberExpression)m.Target) + "." + m.Member.Name;

    public async Task<string> BuildSnapshotJsonAsync(string viewName, CancellationToken ct = default)
    {
        var columns = await _discovery.GetViewColumnsAsync(viewName, ct);
        return JsonConvert.SerializeObject(new { takenAt = DateTime.UtcNow, columns });
    }

    public async Task<TemplateHealthReport> CheckAsync(int templateId, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        var report = new TemplateHealthReport { TemplateId = templateId };
        if (template is null)
        {
            report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Critical, Code = "template_missing", Message = $"Template {templateId} does not exist." });
            return report;
        }
        report.SourceView = template.SourceView;
        var body = template.CurrentVersion?.Body ?? string.Empty;
        report.Tokens = (await ExtractModelPathsAsync(body, ct)).ToList();

        SnapshotData? snapshot = null;
        if (!string.IsNullOrWhiteSpace(template.SourceViewSnapshot))
            try { snapshot = JsonConvert.DeserializeObject<SnapshotData>(template.SourceViewSnapshot); }
            catch { snapshot = null; }
        report.SnapshotTakenAt = snapshot?.TakenAt;

        if (string.IsNullOrWhiteSpace(template.SourceView))
        {
            if (report.Tokens.Count > 0)
                report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "unbound_tokens", Message = "Template references model fields but is not bound to a SQL view." });
            else
                report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Info, Code = "unbound_no_tokens", Message = "Template is not bound to a SQL view (not schema-checkable)." });
            return report;
        }

        IReadOnlyList<SqlColumnInfo> live;
        try { live = await _discovery.GetViewColumnsAsync(template.SourceView, ct); }
        catch
        {
            report.ViewMissing = true;
            report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Critical, Code = "view_missing", Message = $"View '{template.SourceView}' no longer exists." });
            return report;
        }
        if (live.Count == 0)
        {
            report.ViewMissing = true;
            report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Critical, Code = "view_missing", Message = $"View '{template.SourceView}' no longer exists." });
            return report;
        }

        var liveByName = live.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var token in report.Tokens)
        {
            if (!liveByName.ContainsKey(token))
                report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Critical, Code = "column_missing", Message = $"Column '{token}' is missing from view '{template.SourceView}'." });
        }

        if (snapshot is { Columns: not null })
        {
            foreach (var expected in snapshot.Columns)
            {
                if (!liveByName.TryGetValue(expected.Name, out var actual)) continue;
                var typeChanged = !string.Equals(expected.DataType, actual.DataType, StringComparison.OrdinalIgnoreCase);
                var lengthChanged = expected.MaxLength != actual.MaxLength;
                var nullabilityChanged = expected.IsNullable != actual.IsNullable;
                if (typeChanged)
                    report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "column_type_changed", Message = $"Column '{expected.Name}' type changed {expected.DataType} → {actual.DataType}." });
                else if (lengthChanged)
                    report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "column_length_changed", Message = $"Column '{expected.Name}' length changed {expected.MaxLength?.ToString() ?? "—"} → {actual.MaxLength?.ToString() ?? "—"}." });
                if (nullabilityChanged)
                    report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "column_nullability_changed", Message = $"Column '{expected.Name}' nullability changed." });
            }
        }
        return report;
    }

    private class SnapshotData
    {
        public DateTime TakenAt { get; set; }
        public List<SqlColumnInfo>? Columns { get; set; }
    }
}
