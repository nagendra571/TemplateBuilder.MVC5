namespace TemplateBuilder.Domain.Exceptions;

public class TemplateNotFoundException : Exception
{
    public TemplateNotFoundException()
    {
    }

    public TemplateNotFoundException(string message)
        : base(message)
    {
    }

    public TemplateNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}