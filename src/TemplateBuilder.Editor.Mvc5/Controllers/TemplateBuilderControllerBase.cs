using System.Web.Mvc;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public abstract class TemplateBuilderControllerBase : Controller
{
    protected JsonResult JsonOk(object data) => Json(data, JsonRequestBehavior.AllowGet);

    protected ActionResult JsonError(int statusCode, object errorBody)
    {
        Response.StatusCode = statusCode;
        return Json(errorBody, JsonRequestBehavior.AllowGet);
    }

    protected ActionResult NoContentResult() => new HttpStatusCodeResult(204);
}