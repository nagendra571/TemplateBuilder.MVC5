using System.Web.Mvc;

namespace TemplateBuilder.SampleMvc5Host.Controllers
{
    public class SpikeController : Controller
    {
        public ActionResult Hello() => View("~/Views/Spike/Hello.cshtml");
    }
}