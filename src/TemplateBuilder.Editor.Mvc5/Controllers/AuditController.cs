using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Models;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class AuditController : TemplateBuilderControllerBase
{
    private readonly IAuditRepository _audit;

    public AuditController(IAuditRepository audit) => _audit = audit;

    [Route("Audit")]
    [HttpGet]
    public async Task<ActionResult> Index(string? entityType, string? actionName, string? actor,
        string? from, string? to, string? search, int page = 1)
    {
        var safePage = Math.Max(1, page);
        var query = new AuditQuery
        {
            EntityType = entityType,
            Action = actionName,
            Actor = actor,
            From = ParseDate(from),
            To = ParseToDate(to),
            Search = search,
            Page = safePage,
            PageSize = 25
        };

        var rows = await _audit.QueryAsync(query);
        var total = await _audit.CountAsync(query);

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
            Search = search
        });
    }

    [Route("Audit/Export")]
    [HttpGet]
    public async Task<ActionResult> Export(string? entityType, string? actionName, string? actor,
        string? from, string? to, string? search)
    {
        var query = new AuditQuery
        {
            EntityType = entityType,
            Action = actionName,
            Actor = actor,
            From = ParseDate(from),
            To = ParseToDate(to),
            Search = search,
            Page = 1,
            PageSize = 50000
        };

        var rows = await _audit.QueryAsync(query);

        var sb = new StringBuilder();
        sb.AppendLine("OccurredAt,EntityType,EntityId,Action,Actor,Comment");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                Quote(r.OccurredAt.ToString("u")),
                Quote(r.EntityType), r.EntityId.ToString(),
                Quote(r.Action), Quote(r.Actor), Quote(r.Comment ?? string.Empty)));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        Response.AddHeader("Content-Disposition", "attachment; filename=template-builder-audit.csv");
        return File(bytes, "text/csv");
    }

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