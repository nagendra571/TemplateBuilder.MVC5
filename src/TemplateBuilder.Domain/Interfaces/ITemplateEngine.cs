namespace TemplateBuilder.Domain.Interfaces;

public interface ITemplateEngine
{
    Task<string> RenderAsync(int templateId, object model, CancellationToken ct = default);
    Task<string> RenderByNameAsync(string templateName, object model, CancellationToken ct = default);
    Task<string> RenderBodyAsync(string body, object model, CancellationToken ct = default);
}