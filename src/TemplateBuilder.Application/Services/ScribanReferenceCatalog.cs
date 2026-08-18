namespace TemplateBuilder.Application.Services;

public sealed class ScribanReferenceEntry
{
    public required string Group { get; init; }
    public required string Label { get; init; }
    public required string Code { get; init; }
    public string? Expected { get; init; }
}

public static class ScribanReferenceCatalog
{
    public static IReadOnlyList<ScribanReferenceEntry> Entries { get; } = new[]
    {
        new ScribanReferenceEntry { Group = "Loops", Label = "Simple loop", Code = "{{ for item in model.Items }}{{ item.Name }}{{ end }}", Expected = "AB" },
        new ScribanReferenceEntry { Group = "Loops", Label = "Loop with separator", Code = "{{ for item in model.Items }}{{ item.Name }}{{ if !for.last }}, {{ end }}{{ end }}", Expected = "A, B" },
        new ScribanReferenceEntry { Group = "Conditionals", Label = "If / else", Code = "{{ if model.Status == \"Active\" }}Yes{{ else }}No{{ end }}", Expected = "Yes" },
        new ScribanReferenceEntry { Group = "Conditionals", Label = "Value exists", Code = "{{ if model.Status }}Present{{ else }}Missing{{ end }}", Expected = "Present" },
        new ScribanReferenceEntry { Group = "Missing values", Label = "Fallback value", Code = "{{ model.Missing ?? \"—\" }}", Expected = "—" },
        new ScribanReferenceEntry { Group = "Whitespace", Label = "Trim space", Code = "X {{- model.Name -}} Y", Expected = "Xjane doeY" }
    };
}