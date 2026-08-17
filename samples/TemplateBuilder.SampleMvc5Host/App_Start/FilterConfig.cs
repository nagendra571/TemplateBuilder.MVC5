using System.Web.Mvc;
using TemplateBuilder.Editor.Mvc5.Authorization;

namespace TemplateBuilder.SampleMvc5Host
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new TemplateBuilderAuthorizationFilter());
        }
    }
}
