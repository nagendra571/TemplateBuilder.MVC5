using Ganss.Xss;

namespace TemplateBuilder.Application.Services;

public class HtmlSanitizerService : IHtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Add("mailto");
        return sanitizer;
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}