using System.Web.Mvc;

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
        return Json(errorBody, JsonRequestBehavior.AllowGet);
    }

    protected ActionResult NoContentResult() => new HttpStatusCodeResult(204);
}