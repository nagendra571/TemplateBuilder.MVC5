using System.Web.Mvc;

namespace TemplateBuilder.SampleMvc5Host
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}
