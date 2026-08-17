using System.Web.Mvc;
using System.Web.Routing;
using TemplateBuilder.Editor.Mvc5;

namespace TemplateBuilder.SampleMvc5Host
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            TemplateBuilderEditorRouteConfig.RegisterRoutes(routes);

            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}