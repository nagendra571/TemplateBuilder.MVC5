using FluentAssertions;
using TemplateBuilder.Application.Services;

namespace TemplateBuilder.Application.Tests;

public class HtmlSanitizerServiceTests
{
    private readonly HtmlSanitizerService _service = new();

    [Fact]
    public void Sanitize_removes_script_tags()
    {
        var result = _service.Sanitize("<p>Hello</p><script>alert('xss')</script>");
        result.Should().NotContain("script").And.Contain("Hello");
    }

    [Fact]
    public void Sanitize_removes_event_handler_attributes()
    {
        var result = _service.Sanitize("<a href=\"https://example.com\" onclick=\"steal()\">link</a>");
        result.Should().NotContain("onclick").And.Contain("href=\"https://example.com\"");
    }

    [Fact]
    public void Sanitize_removes_javascript_urls()
    {
        var result = _service.Sanitize("<a href=\"javascript:alert(1)\">bad</a>");
        result.Should().NotContain("javascript:");
    }

    [Fact]
    public void Sanitize_keeps_safe_markup()
    {
        var html = "<h1>Title</h1><p>Body with <strong>bold</strong> and <em>italic</em>.</p>";
        var result = _service.Sanitize(html);
        result.Should().Contain("<h1>Title</h1>");
        result.Should().Contain("<strong>bold</strong>");
        result.Should().Contain("<em>italic</em>");
    }

    [Fact]
    public void Sanitize_returns_empty_for_empty_input()
    {
        _service.Sanitize(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_keeps_well_formed_links()
    {
        var result = _service.Sanitize("<a href=\"https://example.com/page?x=1&amp;y=2\" target=\"_blank\">go</a>");
        result.Should().Contain("https://example.com/page");
        result.Should().NotContain("onerror");
    }
}