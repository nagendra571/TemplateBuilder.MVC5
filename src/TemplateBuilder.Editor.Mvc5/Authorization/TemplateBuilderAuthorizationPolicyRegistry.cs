using System.Collections.Generic;
using System.Web.Mvc;

namespace TemplateBuilder.Editor.Mvc5.Authorization;

public static class TemplateBuilderAuthorizationPolicyRegistry
{
    private static readonly Dictionary<string, IAuthorizationFilter> Policies =
        new(System.StringComparer.OrdinalIgnoreCase);

    public static void Register(string name, IAuthorizationFilter filter) => Policies[name] = filter;

    internal static IAuthorizationFilter? Resolve(string name)
        => Policies.TryGetValue(name, out var f) ? f : null;
}