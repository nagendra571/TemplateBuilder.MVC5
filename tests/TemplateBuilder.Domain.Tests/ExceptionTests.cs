using FluentAssertions;
using TemplateBuilder.Domain.Exceptions;

namespace TemplateBuilder.Domain.Tests;

public class ExceptionTests
{
    [Fact]
    public void SchemaVersionMismatchException_carries_message()
    {
        var ex = new SchemaVersionMismatchException("schema outdated");
        ex.Message.Should().Be("schema outdated");
    }

    [Fact]
    public void SchemaVersionMismatchException_wraps_inner_exception()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new SchemaVersionMismatchException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void TemplateNotFoundException_carries_message()
    {
        var ex = new TemplateNotFoundException("missing");
        ex.Message.Should().Be("missing");
    }

    [Fact]
    public void TemplateNotFoundException_wraps_inner_exception()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new TemplateNotFoundException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void TemplateRenderException_carries_message()
    {
        var ex = new TemplateRenderException("render failed");
        ex.Message.Should().Be("render failed");
    }

    [Fact]
    public void TemplateRenderException_wraps_inner_exception()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new TemplateRenderException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}