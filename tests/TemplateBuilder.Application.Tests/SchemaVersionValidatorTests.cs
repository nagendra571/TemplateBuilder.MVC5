using FluentAssertions;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Exceptions;

namespace TemplateBuilder.Application.Tests;

public class SchemaVersionValidatorTests
{
    private readonly SchemaVersionValidator _validator = new();

    [Fact]
    public void ValidateSchemaVersion_accepts_current_version()
    {
        var act = () => _validator.ValidateSchemaVersion(SchemaVersionValidator.CurrentSchemaVersion);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSchemaVersion_accepts_newer_versions()
    {
        var act = () => _validator.ValidateSchemaVersion(SchemaVersionValidator.CurrentSchemaVersion + 1);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSchemaVersion_throws_when_version_missing()
    {
        var act = () => _validator.ValidateSchemaVersion(null);
        act.Should().Throw<SchemaVersionMismatchException>()
            .WithMessage($"*{SchemaVersionValidator.SchemaVersionViewName}*");
    }

    [Fact]
    public void ValidateSchemaVersion_throws_when_version_older()
    {
        var act = () => _validator.ValidateSchemaVersion(SchemaVersionValidator.CurrentSchemaVersion - 1);
        act.Should().Throw<SchemaVersionMismatchException>()
            .WithMessage("*older*");
    }
}