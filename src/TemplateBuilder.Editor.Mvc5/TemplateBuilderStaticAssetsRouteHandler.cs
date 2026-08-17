using System;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.Routing;

namespace TemplateBuilder.Editor.Mvc5;

public sealed class TemplateBuilderStaticAssetsRoute : Route
{
    public TemplateBuilderStaticAssetsRoute()
        : base("TemplateBuilderEditor/{*path}", new TemplateBuilderStaticAssetsRouteHandler())
    {
    }

    public override VirtualPathData GetVirtualPath(RequestContext requestContext, RouteValueDictionary values) => null;
}

public sealed class TemplateBuilderStaticAssetsRouteHandler : IRouteHandler, IHttpHandler
{
    private static readonly Assembly Asm = typeof(TemplateBuilderStaticAssetsRouteHandler).Assembly;

    public bool IsReusable => true;

    public IHttpHandler GetHttpHandler(RequestContext requestContext) => this;

    public void ProcessRequest(HttpContext context) => ProcessRequest(new HttpContextWrapper(context));

    private void ProcessRequest(HttpContextBase context)
    {
        var path = context.Request.RequestContext.RouteData.Values["path"] as string ?? string.Empty;
        var (resourceSuffix, contentType) = path switch
        {
            "css/template-editor.css" => ("StaticAssets.template-editor.css", "text/css"),
            "js/template-editor.js" => ("StaticAssets.template-editor.js", "application/javascript"),
            _ => (null, null)
        };

        if (resourceSuffix is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var resourceName = $"TemplateBuilder.Editor.Mvc5.{resourceSuffix}";
        using var stream = Asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        context.Response.ContentType = contentType;
        using var reader = new StreamReader(stream);
        context.Response.Write(reader.ReadToEnd());
    }

    void IHttpHandler.ProcessRequest(HttpContext context) => ProcessRequest(context);
}