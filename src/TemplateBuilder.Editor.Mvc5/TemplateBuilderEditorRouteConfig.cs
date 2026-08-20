using System.Web.Mvc;
using System.Web.Routing;

namespace TemplateBuilder.Editor.Mvc5;

public static class TemplateBuilderEditorRouteConfig
{
    public static void RegisterRoutes(RouteCollection routes)
    {
        // Idempotent so a consumer that bootstraps this helper more than once in the
        // same application domain (e.g. a host that re-runs Application_Start on a
        // failed first start) does not hit "route name already in the collection".
        if (routes["TemplateBuilderEditorStaticAssets"] != null)
        {
            return;
        }

        routes.MapMvcAttributeRoutes();

        routes.Add("TemplateBuilderEditorStaticAssets", new TemplateBuilderStaticAssetsRoute());
    }
}