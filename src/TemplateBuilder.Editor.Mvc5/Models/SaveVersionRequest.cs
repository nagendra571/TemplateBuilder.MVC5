namespace TemplateBuilder.Editor.Mvc5.Models;
public class SaveVersionRequest
{
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SourceView { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ChangeComment { get; set; }
}