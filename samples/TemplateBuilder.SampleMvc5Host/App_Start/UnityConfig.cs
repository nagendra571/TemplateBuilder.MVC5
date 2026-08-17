using System.Web.Mvc;
using TemplateBuilder.Editor.Mvc5;
using Unity;
using Unity.Mvc5;

namespace TemplateBuilder.SampleMvc5Host
{
    public static class UnityConfig
    {
        public static void RegisterComponents()
        {
            var container = new UnityContainer();

            container.RegisterTemplateBuilderEditor(options =>
            {
                options.ConnectionString =
                    System.Configuration.ConfigurationManager.ConnectionStrings["TemplateDb"].ConnectionString;
                // options.Authorization.Mode defaults to Anonymous for the sample host
            });

            // mono/xsp4 shim (BLOCKERS #12): restores HttpContext.Current on async
            // continuation threads so view rendering and the Unity resolver work.
            // Not needed on Windows/IIS.
            container.RegisterType<IActionInvoker, MonoFlowActionInvoker>();

            DependencyResolver.SetResolver(new UnityDependencyResolver(container));
        }
    }
}
