using System.Web.Mvc;
using TemplateBuilder.Editor.Mvc5.Models;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public abstract class TemplateBuilderControllerBase : Controller
{
    protected string CurrentActor => User?.Identity?.Name ?? "anonymous";

    protected override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        base.OnActionExecuting(filterContext);
        if (filterContext.HttpContext.Request.ContentType?.StartsWith("application/json", System.StringComparison.OrdinalIgnoreCase) != true)
            return;
        filterContext.Controller.ValueProvider = new ValueProviderCollection(new IValueProvider[]
        {
            new RouteDataValueProvider(filterContext),
            new QueryStringValueProvider(filterContext),
            new HttpFileCollectionValueProvider(filterContext)
        });
    }

    protected JsonResult JsonOk(object data) => Json(data, JsonRequestBehavior.AllowGet);

    protected ActionResult JsonError(int statusCode, object errorBody)
    {
        Response.StatusCode = statusCode;
        var body = errorBody is ErrorResult e
            ? (object)new { code = e.Code, message = e.Message }
            : errorBody;
        return Json(body, JsonRequestBehavior.AllowGet);
    }

    protected ActionResult NoContentResult() => new HttpStatusCodeResult(204);
}