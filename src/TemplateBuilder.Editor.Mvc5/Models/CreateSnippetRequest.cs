namespace TemplateBuilder.Editor.Mvc5.Models;

public class CreateSnippetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Body { get; set; } = string.Empty;
}