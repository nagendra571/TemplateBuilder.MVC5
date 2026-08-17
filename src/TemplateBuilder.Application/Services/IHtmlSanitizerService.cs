namespace TemplateBuilder.Application.Services;

public interface IHtmlSanitizerService
{
    string Sanitize(string html);
}