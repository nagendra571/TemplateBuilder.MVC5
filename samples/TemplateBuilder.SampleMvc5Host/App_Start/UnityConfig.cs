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
                // Demo of TemplateBuilderEditorOptions.ActorResolver: any custom identity
                // logic. This sample reads an optional X-TB-Actor header so the flow is
                // verifiable end-to-end (curl -H "X-TB-Actor: alice" ...). In a real app
                // resolve from claims/session instead — a raw header is spoofable and is
                // demo-only here. Falls back to User.Identity.Name -> "anonymous".
                options.ActorResolver = ctx =>
                {
                    var header = ctx?.Request.Headers["X-TB-Actor"];
                    return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
                };
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
