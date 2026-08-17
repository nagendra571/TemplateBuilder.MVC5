namespace TemplateBuilder.Editor.Mvc5.Models;

public class ErrorResult
{
    public ErrorResult(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }
}