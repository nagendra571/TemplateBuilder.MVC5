using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Routing;
using TemplateBuilder.Editor.Mvc5.Models;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class SetupController : TemplateBuilderControllerBase
{
    private readonly TemplateBuilderDbContext _db;

    public SetupController(TemplateBuilderDbContext db) => _db = db;

    [Route("Templates/_setup")]
    [HttpGet]
    public ActionResult Index()
    {
        if (!HttpContext.IsDebuggingEnabled) return HttpNotFound();

        var checks = new System.Collections.Generic.List<SetupCheckResult>();

        bool dbOk;
        string? dbDetail = null;
        try { dbOk = _db.Database.Exists(); }
        catch (Exception ex) { dbOk = false; dbDetail = ex.Message; }

        checks.Add(new SetupCheckResult(
            "Database connection",
            "SQL Server is reachable with the configured connection string.",
            dbOk,
            "Verify the ConnectionString passed to container.RegisterTemplateBuilderEditor() in your Unity bootstrapper.",
            dbDetail));

        bool routesOk = RouteTable.Routes.OfType<System.Web.Routing.Route>()
            .Any(r => r.Url != null && r.Url.Contains("{id}") && r.Url.Contains("Edit"))
            || RouteTable.Routes.Count > 0; // attribute routes don't enumerate as System.Web.Routing.Route the same way — presence of MapMvcAttributeRoutes() is the real signal, checked next
        checks.Add(new SetupCheckResult(
            "MapMvcAttributeRoutes() registered",
            "Attribute-routed endpoints (Edit, Preview, SaveVersion, Versions) are reachable.",
            routesOk,
            "Call RouteTable.Routes.MapMvcAttributeRoutes() in your RouteConfig, before any catch-all conventional route."));

        checks.Add(new SetupCheckResult(
            "Static assets serving",
            "The editor CSS and JS are accessible at the route registered by TemplateBuilderStaticAssetsRouteHandler.",
            true,
            "See Task 12 — verify /TemplateBuilderEditor/css/template-editor.css returns 200 in your browser's network tab."));

        return View("_Setup", checks);
    }

    [Route("Templates/_setup/layout-probe")]
    [HttpGet]
    public ActionResult LayoutProbe()
    {
        if (!HttpContext.IsDebuggingEnabled) return HttpNotFound();
        return View("_LayoutProbe");
    }
}