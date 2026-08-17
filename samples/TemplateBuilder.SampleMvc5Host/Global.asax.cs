using System.Web.Mvc;
using System.Web.Routing;
using RazorGenerator.Mvc;

namespace TemplateBuilder.SampleMvc5Host
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new PrecompiledMvcEngine(typeof(TemplateBuilder.Editor.Mvc5.UnityContainerExtensions).Assembly));
            ViewEngines.Engines.Add(new RazorViewEngine()); // for the sample host's own Views/Home/Index.cshtml

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            UnityConfig.RegisterComponents();
        }
    }
}