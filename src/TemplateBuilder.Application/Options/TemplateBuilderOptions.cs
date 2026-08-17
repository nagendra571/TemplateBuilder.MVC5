namespace TemplateBuilder.Application.Options;

public class TemplateBuilderOptions
{
    public TimeSpan ViewDiscoveryCacheDuration { get; set; } = TimeSpan.FromMinutes(10);
    public int SqlCommandTimeoutSeconds { get; set; } = 30;
    public string DefaultCulture { get; set; } = "en-US";
}