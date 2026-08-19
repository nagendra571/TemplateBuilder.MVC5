using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Application.Services;

public class TemplateWorkflowResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }       // NOT_FOUND | CONFLICT | VALIDATION_ERROR
    public string? ErrorMessage { get; init; }
    public Template? Template { get; init; }

    public static TemplateWorkflowResult Ok(Template template) => new() { Success = true, Template = template };
    public static TemplateWorkflowResult Fail(string code, string message) => new() { Success = false, ErrorCode = code, ErrorMessage = message };
}