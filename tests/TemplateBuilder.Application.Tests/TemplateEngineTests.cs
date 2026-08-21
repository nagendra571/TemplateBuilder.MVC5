using FluentAssertions;
using Moq;
using TemplateBuilder.Application.Options;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Exceptions;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Tests;

public class TemplateEngineTests
{
    private static TemplateEngine CreateEngine(Mock<ITemplateRepository>? repo = null)
        => new(repo?.Object ?? new Mock<ITemplateRepository>().Object, new TemplateBuilderOptions());

    [Fact]
    public async Task RenderBodyAsync_renders_plain_text_body()
    {
        var engine = CreateEngine();
        var result = await engine.RenderBodyAsync("Hello, world!", new { });
        result.Should().Be("Hello, world!");
    }

    [Fact]
    public async Task RenderBodyAsync_renders_model_properties()
    {
        var engine = CreateEngine();
        var result = await engine.RenderBodyAsync("Hi {{name}}, you have {{count}} messages.", new { name = "Alice", count = 3 });
        result.Should().Be("Hi Alice, you have 3 messages.");
    }

    [Fact]
    public async Task RenderBodyAsync_renders_model_prefixed_properties()
    {
        var engine = CreateEngine();
        var result = await engine.RenderBodyAsync("Hi {{model.name}}, you have {{model.count}} messages.", new { name = "Alice", count = 3 });
        result.Should().Be("Hi Alice, you have 3 messages.");
    }

    [Fact]
    public async Task RenderBodyAsync_renders_model_prefixed_loop()
    {
        var engine = CreateEngine();
        var result = await engine.RenderBodyAsync(
            "{{ for item in model.Items }}{{ item.Name }}{{ end }}",
            new Dictionary<string, object>
            {
                ["Items"] = new object[]
                {
                    new Dictionary<string, object> { ["Name"] = "A" },
                    new Dictionary<string, object> { ["Name"] = "B" }
                }
            });
        result.Should().Be("AB");
    }

    [Fact]
    public async Task RenderBodyAsync_does_not_override_caller_provided_model_member()
    {
        var engine = CreateEngine();
        var result = await engine.RenderBodyAsync("Hi {{model.name}}!", new { model = new { name = "Bob" } });
        result.Should().Be("Hi Bob!");
    }

    [Fact]
    public async Task RenderBodyAsync_renders_html_markup()
    {
        var engine = CreateEngine();
        var result = await engine.RenderBodyAsync("<h1>{{title}}</h1>", new { title = "Report" });
        result.Should().Be("<h1>Report</h1>");
    }

    [Fact]
    public async Task RenderBodyAsync_throws_TemplateRenderException_on_parse_error()
    {
        var engine = CreateEngine();
        var act = () => engine.RenderBodyAsync("{{ 1 + }}", new { });
        await act.Should().ThrowAsync<TemplateRenderException>()
            .WithMessage("*parsing failed*");
    }

    [Fact]
    public async Task RenderBodyAsync_throws_TemplateRenderException_wrapping_runtime_error()
    {
        var engine = CreateEngine();
        var act = () => engine.RenderBodyAsync("{{ unknown_func() }}", new { });
        await act.Should().ThrowAsync<TemplateRenderException>()
            .WithMessage("*rendering failed*");
    }

    [Fact]
    public async Task RenderBodyAsync_returns_empty_for_empty_body()
    {
        var engine = CreateEngine();
        var result = await engine.RenderBodyAsync(string.Empty, new { });
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_renders_current_version_body()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template
            {
                Id = 7,
                Name = "Welcome",
                IsActive = true,
                CurrentVersion = new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" }
            });
        repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" });

        var engine = CreateEngine(repo);
        var result = await engine.RenderAsync(7, new { user = "bob" });

        result.Should().Be("Welcome bob");
    }

    [Fact]
    public async Task RenderAsync_throws_TemplateNotFoundException_when_template_missing()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Template?)null);

        var engine = CreateEngine(repo);
        var act = () => engine.RenderAsync(99, new { });

        await act.Should().ThrowAsync<TemplateNotFoundException>();
    }

    [Fact]
    public async Task RenderAsync_throws_NoActiveVersionException_when_template_has_no_versions()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template { Id = 1, Name = "Empty", IsActive = true, CurrentVersion = null });
        repo.Setup(r => r.GetLastActiveVersionAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TemplateVersion?)null);

        var engine = CreateEngine(repo);
        var act = () => engine.RenderAsync(1, new { });

        await act.Should().ThrowAsync<NoActiveVersionException>();
    }

    [Fact]
    public async Task RenderByNameAsync_renders_current_version_body()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByNameAsync("Welcome", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template
            {
                Id = 7,
                Name = "Welcome",
                IsActive = true,
                CurrentVersion = new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" }
            });
        repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" });

        var engine = CreateEngine(repo);
        var result = await engine.RenderByNameAsync("Welcome", new { user = "carol" });

        result.Should().Be("Welcome carol");
    }

    [Fact]
    public async Task RenderByNameAsync_throws_TemplateNotFoundException_when_template_missing()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByNameAsync("Missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Template?)null);

        var engine = CreateEngine(repo);
        var act = () => engine.RenderByNameAsync("Missing", new { });

        await act.Should().ThrowAsync<TemplateNotFoundException>();
    }

    [Fact]
    public async Task RenderAsync_throws_when_template_is_inactive()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template { Id = 7, IsActive = false, CurrentVersion = new TemplateVersion { Body = "x" } });

        var engine = CreateEngine(repo);
        var act = () => engine.RenderAsync(7, new { });

        await act.Should().ThrowAsync<TemplateInactiveException>();
    }

    [Fact]
    public async Task RenderAsync_throws_when_no_active_version_exists()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template { Id = 7, IsActive = true });
        repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TemplateVersion?)null);

        var engine = CreateEngine(repo);
        var act = () => engine.RenderAsync(7, new { });

        await act.Should().ThrowAsync<NoActiveVersionException>();
    }

    [Fact]
    public async Task RenderAsync_renders_last_active_version_when_latest_is_draft()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template
            {
                Id = 7, IsActive = true,
                CurrentVersion = new TemplateVersion { VersionNumber = 3, Body = "<p>draft {{ model.Name }}</p>", IsActive = false }
            });
        repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateVersion { VersionNumber = 2, Body = "<p>active {{ model.Name }}</p>", IsActive = true });

        var engine = CreateEngine(repo);
        var html = await engine.RenderAsync(7, new Dictionary<string, object?> { ["Name"] = "X" });

        html.Should().Contain("active X");
        html.Should().NotContain("draft");
    }

    [Fact]
    public async Task RenderByNameAsync_throws_TemplateInactiveException_for_inactive_template()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByNameAsync("Off", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template { Id = 9, IsActive = false, CurrentVersion = new TemplateVersion { Body = "x" } });

        var engine = CreateEngine(repo);
        var act = () => engine.RenderByNameAsync("Off", new { });

        await act.Should().ThrowAsync<TemplateInactiveException>();
    }

    [Fact]
    public async Task RenderByNameAsync_renders_last_active_version_when_latest_is_draft()
    {
        var repo = new Mock<ITemplateRepository>();
        repo.Setup(r => r.GetByNameAsync("Inv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template
            {
                Id = 5, IsActive = true,
                CurrentVersion = new TemplateVersion { VersionNumber = 2, Body = "draft body", IsActive = false }
            });
        repo.Setup(r => r.GetLastActiveVersionAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateVersion { VersionNumber = 1, Body = "<p>v1 {{ model.Name }}</p>", IsActive = true });

        var engine = CreateEngine(repo);
        var html = await engine.RenderByNameAsync("Inv", new Dictionary<string, object?> { ["Name"] = "Y" });

        html.Should().Contain("v1 Y");
    }
}

public class TemplateBuilderOptionsTests
{
    [Fact]
    public void Defaults_are_sane()
    {
        var o = new TemplateBuilderOptions();

        o.ViewDiscoveryCacheDuration.Should().Be(TimeSpan.FromMinutes(10));
        o.SqlCommandTimeoutSeconds.Should().Be(30);
        o.DefaultCulture.Should().Be("en-US");
    }
}