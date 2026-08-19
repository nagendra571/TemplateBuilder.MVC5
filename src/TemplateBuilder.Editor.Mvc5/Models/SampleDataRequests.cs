namespace TemplateBuilder.Editor.Mvc5.Models;

public class GenerateSampleDataRequest
{
    public string? ViewName { get; set; }
    public string? TemplateBody { get; set; }
}

public class SaveSampleDataRequest
{
    public string? SampleData { get; set; }
}