using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Models;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class HealthController : TemplateBuilderControllerBase
{
    private readonly ITemplateRepository _repository;
    private readonly ITemplateHealthService _health;

    public HealthController(ITemplateRepository repository, ITemplateHealthService health)
    {
        _repository = repository;
        _health = health;
    }

    [Route("Health")]
    [HttpGet]
    public async Task<ActionResult> Index()
    {
        var templates = await _repository.GetAllAsync();
        var rows = new System.Collections.Generic.List<HealthRowViewModel>();
        foreach (var t in templates)
            rows.Add(new HealthRowViewModel { TemplateId = t.Id, Name = t.Name, Report = await _health.CheckAsync(t.Id) });
        return View(new HealthIndexViewModel { Rows = rows });
    }

    [Route("Health/Summaries")]
    [HttpGet]
    public async Task<ActionResult> Summaries(string? ids)
    {
        var list = new System.Collections.Generic.List<object>();
        foreach (var raw in (ids ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(raw, out var id)) continue;
            var report = await _health.CheckAsync(id);
            list.Add(new { templateId = id, severity = SeverityName(report.Worst), findingCount = report.Findings.Count(f => f.Severity != HealthSeverity.Info) });
        }
        return Content(JsonConvert.SerializeObject(list), "application/json");
    }

    private static string SeverityName(HealthSeverity s) => s switch
    {
        HealthSeverity.Critical => "critical",
        HealthSeverity.Warning => "warning",
        _ => "healthy"
    };
}
