using System;
using System.Web.Mvc;

namespace TemplateBuilder.Editor.Mvc5.Authorization;

public sealed class TemplateBuilderAuthorizationFilter : IAuthorizationFilter
{
    private static TemplateBuilderAuthorizationOptions _options = new();

    internal static void Configure(TemplateBuilderAuthorizationOptions options)
        => _options = options ?? new TemplateBuilderAuthorizationOptions();

    public void OnAuthorization(AuthorizationContext filterContext)
    {
        var controllerType = filterContext.ActionDescriptor.ControllerDescriptor.ControllerType;
        if (controllerType.Assembly != typeof(TemplateBuilderAuthorizationFilter).Assembly)
            return; // not one of our controllers — leave the host's own auth alone

        bool useCustomPolicy = !string.IsNullOrWhiteSpace(_options.PolicyName);
        bool isSecured = useCustomPolicy || _options.Mode != TemplateBuilderAuthorizationMode.Anonymous;
        if (!isSecured) return;

        if (useCustomPolicy)
        {
            var hostFilter = TemplateBuilderAuthorizationPolicyRegistry.Resolve(_options.PolicyName!);
            if (hostFilter is null)
                throw new InvalidOperationException(
                    $"TemplateBuilder.Editor.Mvc5: no policy named '{_options.PolicyName}' was registered. " +
                    "Call TemplateBuilderAuthorizationPolicyRegistry.Register(name, filter) during application startup.");
            hostFilter.OnAuthorization(filterContext);
            return;
        }

        var user = filterContext.HttpContext.User;
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            filterContext.Result = new HttpUnauthorizedResult();
            return;
        }

        if (_options.Mode == TemplateBuilderAuthorizationMode.Role)
        {
            if (_options.RoleNames is not { Length: > 0 })
                throw new InvalidOperationException(
                    "TemplateBuilder.Editor.Mvc5: Authorization.RoleNames must contain at least one role when Mode is Role.");

            bool inAnyRole = false;
            foreach (var role in _options.RoleNames)
            {
                if (user.IsInRole(role)) { inAnyRole = true; break; }
            }
            if (!inAnyRole)
                filterContext.Result = new HttpStatusCodeResult(403);
        }
    }
}