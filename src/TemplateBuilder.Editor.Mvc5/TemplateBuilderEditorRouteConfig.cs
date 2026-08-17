using System.Web.Mvc;
using System.Web.Routing;

namespace TemplateBuilder.Editor.Mvc5;

public static class TemplateBuilderEditorRouteConfig
{
    public static void RegisterRoutes(RouteCollection routes)
    {
        routes.MapMvcAttributeRoutes();

        routes.Add("TemplateBuilderEditorStaticAssets", new Route(
            "TemplateBuilderEditor/{*path}",
            new TemplateBuilderStaticAssetsRouteHandler()));
    }
}