using System.Web.Mvc;
using RazorGenerator.Mvc;
using TemplateBuilder.Editor.Mvc5;
using Unity;
using Unity.Mvc5;

namespace TemplateBuilder.SampleMvc5Host
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new PrecompiledMvcEngine(typeof(TemplateBuilder.Editor.Mvc5.UnityContainerExtensions).Assembly));

            var container = new UnityContainer();
            container.RegisterTemplateBuilderEditor(options =>
            {
                options.ConnectionString =
                    "Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;";
            });
            container.RegisterType<IActionInvoker, MonoFlowActionInvoker>();
            DependencyResolver.SetResolver(new UnityDependencyResolver(container));

            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(System.Web.Routing.RouteTable.Routes);
        }
    }
}