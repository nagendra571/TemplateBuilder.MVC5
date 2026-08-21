using System;
namespace TemplateBuilder.Domain.Exceptions;

public class NoActiveVersionException : Exception
{
    public NoActiveVersionException(int templateId)
        : base($"Template {templateId} has no active version to serve.") { }
}
