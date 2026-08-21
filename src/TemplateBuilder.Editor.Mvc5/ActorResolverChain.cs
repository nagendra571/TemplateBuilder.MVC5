using System;
using System.Web;

namespace TemplateBuilder.Editor.Mvc5;

internal static class ActorResolverChain
{
    private const int MaxActorLength = 200;

    public static string Resolve(Func<HttpContextBase, string?>? resolver, string? identityName, HttpContextBase? httpContext)
    {
        var actor = resolver?.Invoke(httpContext!);
        if (string.IsNullOrWhiteSpace(actor))
            actor = identityName;
        if (string.IsNullOrWhiteSpace(actor))
            actor = "anonymous";
        return actor.Length <= MaxActorLength ? actor : actor.Substring(0, MaxActorLength);
    }
}
