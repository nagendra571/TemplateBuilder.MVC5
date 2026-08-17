using System.Web.Mvc;
using RazorGenerator.Mvc;

namespace TemplateBuilder.SampleMvc5Host
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new PrecompiledMvcEngine(typeof(TemplateBuilder.Editor.Mvc5.UnityContainerExtensions).Assembly));

            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(System.Web.Routing.RouteTable.Routes);
        }
    }
}