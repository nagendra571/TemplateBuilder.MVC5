namespace TemplateBuilder.Domain.Exceptions;

public class SchemaVersionMismatchException : Exception
{
    public SchemaVersionMismatchException()
    {
    }

    public SchemaVersionMismatchException(string message)
        : base(message)
    {
    }

    public SchemaVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}