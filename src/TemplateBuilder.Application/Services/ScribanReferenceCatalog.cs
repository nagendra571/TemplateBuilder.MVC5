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
        new ScribanReferenceEntry { Group = "Dates", Label = "Format a date", Code = "{{ model.DueDate | date.to_string \"%m/%d/%Y\" }}", Expected = "08/18/2026" },
        new ScribanReferenceEntry { Group = "Dates", Label = "Date and time", Code = "{{ model.UpdatedAt | date.to_string \"%m/%d/%Y %H:%M\" }}", Expected = "08/18/2026 10:30" },
        new ScribanReferenceEntry { Group = "Dates", Label = "Today's date", Code = "{{ date.now | date.to_string \"%B %d, %Y\" }}" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Uppercase", Code = "{{ model.Name | string.upcase }}", Expected = "JANE DOE" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Capitalize", Code = "{{ model.Name | string.capitalize }}", Expected = "Jane doe" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Escape HTML", Code = "{{ model.RichHtml | html.escape }}", Expected = "&lt;b&gt;x&lt;/b&gt;" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Truncate", Code = "{{ model.Name | string.truncate 6 }}", Expected = "jan..." },
        new ScribanReferenceEntry { Group = "Numbers", Label = "Round", Code = "{{ model.Amount | math.round }}", Expected = "1250" },
        new ScribanReferenceEntry { Group = "Numbers", Label = "Fixed decimals", Code = "{{ model.Amount | math.format \"0.00\" }}", Expected = "1250.00" },
        new ScribanReferenceEntry { Group = "Missing values", Label = "Fallback value", Code = "{{ model.Missing ?? \"—\" }}", Expected = "—" },
        new ScribanReferenceEntry { Group = "Whitespace", Label = "Trim space", Code = "X {{- model.Name -}} Y", Expected = "Xjane doeY" }
    };
}