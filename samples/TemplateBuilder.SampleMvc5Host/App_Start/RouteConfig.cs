using System.Web.Mvc;
using System.Web.Routing;

namespace TemplateBuilder.SampleMvc5Host
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: "Spike",
                url: "Spike/{action}",
                defaults: new { controller = "Spike", action = "Hello" });

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}