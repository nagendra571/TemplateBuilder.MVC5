using Ganss.Xss;

namespace TemplateBuilder.Application.Services;

public class HtmlSanitizerService : IHtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer = new();

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}