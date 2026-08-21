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
        if (!template.IsActive)
            throw new TemplateInactiveException(templateId);

        var activeVersion = await _repository.GetLastActiveVersionAsync(templateId, ct)
            ?? throw new NoActiveVersionException(templateId);

        return await RenderBodyAsync(activeVersion.Body, model, ct);
    }

    public async Task<string> RenderByNameAsync(string templateName, object model, CancellationToken ct = default)
    {
        var template = await _repository.GetByNameAsync(templateName, ct);
        if (template is null)
            throw new TemplateNotFoundException($"Template '{templateName}' not found.");
        if (!template.IsActive)
            throw new TemplateInactiveException(template.Id);

        var activeVersion = await _repository.GetLastActiveVersionAsync(template.Id, ct)
            ?? throw new NoActiveVersionException(template.Id);

        return await RenderBodyAsync(activeVersion.Body, model, ct);
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
            if (!scriptObject.ContainsKey("model"))
            {
                var modelObject = new ScriptObject();
                modelObject.Import(model);
                scriptObject["model"] = modelObject;
            }
            var context = new TemplateContext
            {
                CancellationToken = ct
            };
            context.PushGlobal(scriptObject);
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