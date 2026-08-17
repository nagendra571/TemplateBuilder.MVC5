namespace TemplateBuilder.Editor.Mvc5.Authorization;

public class TemplateBuilderAuthorizationOptions
{
    public TemplateBuilderAuthorizationMode Mode { get; set; } = TemplateBuilderAuthorizationMode.Anonymous;
    public string[]? RoleNames { get; set; }
    public string? PolicyName { get; set; }
}