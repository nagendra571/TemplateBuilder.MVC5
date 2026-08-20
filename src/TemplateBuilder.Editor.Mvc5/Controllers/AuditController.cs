using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Models;
using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class AuditController : TemplateBuilderControllerBase
{
    private readonly IAuditRepository _audit;
    private readonly IAuditStatsRepository _stats;

    public AuditController(IAuditRepository audit, IAuditStatsRepository stats)
    {
        _audit = audit;
        _stats = stats;
    }

    private static readonly string[] KnownActions = typeof(AuditActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(string))
        .Select(f => (string)f.GetValue(null))
        .OrderBy(a => a, StringComparer.Ordinal)
        .ToArray();

    [Route("Audit")]
    [HttpGet]
    public async Task<ActionResult> Index(string? entityType, string? actionName, string? actor,
        string? from, string? to, string? search, int page = 1)
    {
        var safePage = Math.Max(1, page);
        var query = BuildQuery(entityType, actionName, actor, from, to, search, safePage, 25);

        var rows = await _audit.QueryAsync(query);
        var total = await _audit.CountAsync(query);
        var stats = await _stats.GetStatsAsync(query);

        return View(new AuditIndexViewModel
        {
            Rows = rows,
            Total = total,
            Page = safePage,
            PageSize = 25,
            EntityType = entityType,
            Action = actionName,
            Actor = actor,
            From = from,
            To = to,
            Search = search,
            Stats = stats,
            KnownActions = KnownActions
        });
    }

    [Route("Audit/Stats")]
    [HttpGet]
    public async Task<ActionResult> Stats(string? entityType, string? actionName, string? actor,
        string? from, string? to, string? search)
    {
        var query = BuildQuery(entityType, actionName, actor, from, to, search, 1, 25);
        var stats = await _stats.GetStatsAsync(query);

        return Json(new
        {
            total = stats.Total,
            templateCount = stats.TemplateCount,
            snippetCount = stats.SnippetCount,
            uniqueActors = stats.UniqueActors,
            firstOccurrence = stats.FirstOccurrence?.ToString("o"),
            lastOccurrence = stats.LastOccurrence?.ToString("o"),
            buckets = stats.DailyBuckets.Select(b => new { date = b.Date.ToString("yyyy-MM-dd"), count = b.Count }).ToArray()
        }, JsonRequestBehavior.AllowGet);
    }

    [Route("Audit/Export")]
    [HttpGet]
    public async Task<ActionResult> Export(string? entityType, string? actionName, string? actor,
        string? from, string? to, string? search)
    {
        var query = BuildQuery(entityType, actionName, actor, from, to, search, 1, 50000);

        var rows = await _audit.QueryAsync(query);

        var sb = new StringBuilder();
        sb.AppendLine("OccurredAt,EntityType,EntityId,Action,Actor,Comment,BeforeState,AfterState");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                Quote(r.OccurredAt.ToString("u")),
                Quote(r.EntityType), r.EntityId.ToString(),
                Quote(r.Action), Quote(r.Actor), Quote(r.Comment ?? string.Empty),
                Quote(r.BeforeState ?? string.Empty), Quote(r.AfterState ?? string.Empty)));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        Response.AddHeader("Content-Disposition", "attachment; filename=template-builder-audit.csv");
        return File(bytes, "text/csv");
    }

    private static AuditQuery BuildQuery(string? entityType, string? actionName, string? actor,
        string? from, string? to, string? search, int page, int pageSize)
        => new AuditQuery
        {
            EntityType = entityType,
            Action = actionName,
            Actor = actor,
            From = ParseDate(from),
            To = ParseToDate(to),
            Search = search,
            Page = page,
            PageSize = pageSize
        };

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, out var parsed) ? parsed : (DateTime?)null;

    private static DateTime? ParseToDate(string? value)
        => DateTime.TryParse(value, out var parsed) ? parsed.AddDays(1).AddTicks(-1) : (DateTime?)null;

    private static string Quote(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
