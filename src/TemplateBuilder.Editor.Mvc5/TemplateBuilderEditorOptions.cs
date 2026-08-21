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

    // When false, the package never runs EF6 migrations and never attempts DDL. Use this for
    // DBA-managed databases: run the schema script shipped in the package (content/Scripts),
    // then let the app login work with DML-only rights.
    public bool ApplyMigrations { get; set; } = true;
}
