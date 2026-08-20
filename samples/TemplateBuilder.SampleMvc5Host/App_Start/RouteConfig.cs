using System.Web.Mvc;
using System.Web.Routing;
using TemplateBuilder.Editor.Mvc5;

namespace TemplateBuilder.SampleMvc5Host
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            // mono/xsp4 fires Application_Start twice in the same AppDomain (~10s apart);
            // guard the named registrations so the second pass is a no-op.
            TemplateBuilderEditorRouteConfig.RegisterRoutes(routes);

            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            if (routes["Default"] == null)
            {
                routes.MapRoute(
                    name: "Default",
                    url: "{controller}/{action}/{id}",
                    defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional });
            }
        }
    }
}
