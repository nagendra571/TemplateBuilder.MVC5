namespace TemplateBuilder.Domain.Exceptions;

public class TemplateRenderException : Exception
{
    public TemplateRenderException()
    {
    }

    public TemplateRenderException(string message)
        : base(message)
    {
    }

    public TemplateRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}