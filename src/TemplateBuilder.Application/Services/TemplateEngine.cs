using System.Globalization;
using TemplateBuilder.Application.Options;
using TemplateBuilder.Domain.Exceptions;
using TemplateBuilder.Domain.Interfaces;
using Scriban;
using Scriban.Runtime;

namespace TemplateBuilder.Application.Services;

public class TemplateEngine : ITemplateEngine
{
    private readonly ITemplateRepository _repository;
    private readonly TemplateBuilderOptions _options;

    public TemplateEngine(ITemplateRepository repository, TemplateBuilderOptions options)
    {
        _repository = repository;
        _options = options;
    }

    public async Task<string> RenderAsync(int templateId, object model, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null)
            throw new TemplateNotFoundException($"Template {templateId} not found.");

        return await RenderBodyAsync(template.CurrentVersion?.Body ?? string.Empty, model, ct);
    }

    public async Task<string> RenderByNameAsync(string templateName, object model, CancellationToken ct = default)
    {
        var template = await _repository.GetByNameAsync(templateName, ct);
        if (template is null)
            throw new TemplateNotFoundException($"Template '{templateName}' not found.");

        return await RenderBodyAsync(template.CurrentVersion?.Body ?? string.Empty, model, ct);
    }

    public async Task<string> RenderBodyAsync(string body, object model, CancellationToken ct = default)
    {
        var parsed = Template.Parse(body);
        if (parsed.HasErrors)
        {
            var messages = string.Join("; ", parsed.Messages.Select(m => m.Message));
            throw new TemplateRenderException($"Template parsing failed: {messages}");
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(_options.DefaultCulture);
            var scriptObject = new ScriptObject();
            scriptObject.Import(model);
            var context = new TemplateContext(scriptObject)
            {
                CancellationToken = ct
            };
            context.PushCulture(culture);
            try
            {
                return await parsed.RenderAsync(context);
            }
            finally
            {
                context.PopCulture();
            }
        }
        catch (TemplateRenderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TemplateRenderException($"Template rendering failed: {ex.Message}", ex);
        }
    }
}