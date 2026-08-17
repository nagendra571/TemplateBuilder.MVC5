using System;
using System.Web.Helpers;
using System.Web.Mvc;

namespace TemplateBuilder.Editor.Mvc5;

public sealed class ValidateJsonAntiForgeryTokenAttribute : FilterAttribute, IAuthorizationFilter
{
    private const string TokenFieldName = "__RequestVerificationToken";

    public void OnAuthorization(AuthorizationContext filterContext)
    {
        if (filterContext is null) throw new ArgumentNullException(nameof(filterContext));
        var request = filterContext.HttpContext.Request;
        var cookie = request.Cookies[AntiForgeryConfig.CookieName];
        if (cookie is null || string.IsNullOrEmpty(cookie.Value))
            throw new HttpAntiForgeryException($"The required anti-forgery cookie \"{AntiForgeryConfig.CookieName}\" is not present.");
        var formToken = request.Headers["RequestVerificationToken"];
        if (string.IsNullOrEmpty(formToken))
            formToken = request.Form[TokenFieldName];
        if (string.IsNullOrEmpty(formToken))
            throw new HttpAntiForgeryException($"The required anti-forgery form field \"{TokenFieldName}\" is not present.");
        AntiForgery.Validate(cookie.Value, formToken);
    }
}