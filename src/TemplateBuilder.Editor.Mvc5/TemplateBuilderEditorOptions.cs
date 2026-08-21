using System;
using System.Web;
using TemplateBuilder.Editor.Mvc5.Authorization;

namespace TemplateBuilder.Editor.Mvc5;

public class TemplateBuilderEditorOptions
{
    internal static TemplateBuilderEditorOptions? Current { get; set; }

    public string ConnectionString { get; set; } = string.Empty;
    public TemplateBuilderAuthorizationOptions Authorization { get; set; } = new();
    public Func<HttpContextBase, string?>? ActorResolver { get; set; }
}
