namespace TemplateBuilder.Editor.Mvc5.Models;

public class SetupCheckResult
{
    public SetupCheckResult(string name, string description, bool passed, string fix, string? detail = null)
    {
        Name = name;
        Description = description;
        Passed = passed;
        Fix = fix;
        Detail = detail;
    }

    public string Name { get; }
    public string Description { get; }
    public bool Passed { get; }
    public string Fix { get; }
    public string? Detail { get; }
}