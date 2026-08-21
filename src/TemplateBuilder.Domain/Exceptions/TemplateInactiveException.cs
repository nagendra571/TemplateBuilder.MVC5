using System;
namespace TemplateBuilder.Domain.Exceptions;

public class TemplateInactiveException : Exception
{
    public TemplateInactiveException(int templateId)
        : base($"Template {templateId} is inactive and not servable.") { }
}
