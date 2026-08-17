using TemplateBuilder.Editor.Mvc5.Authorization;

namespace TemplateBuilder.Editor.Mvc5;

public class TemplateBuilderEditorOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public TemplateBuilderAuthorizationOptions Authorization { get; set; } = new();
}