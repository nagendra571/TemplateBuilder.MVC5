using System.Reflection;
using FluentAssertions;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Domain.Tests;

public class InterfaceContractTests
{
    private static IEnumerable<string> MethodNames(Type iface)
        => iface.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n);

    [Fact]
    public void ITemplateRepository_has_exactly_the_plan_surface()
    {
        var expected = new[]
        {
            "GetByIdAsync",
            "GetByNameAsync",
            "GetCurrentVersionIdAsync",
            "GetLastActiveVersionAsync",
            "GetVersionBodyAsync",
            "GetAllAsync",
            "GetVersionHistoryAsync",
            "GetNextVersionNumberAsync",
            "CreateAsync",
            "DeleteAsync",
            "UpdateTemplateAsync",
            "PublishVersionAsync"
        };

        MethodNames(typeof(ITemplateRepository)).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void ISnippetRepository_has_exactly_the_plan_surface()
    {
        var expected = new[]
        {
            "GetAllAsync",
            "GetByIdAsync",
            "CreateAsync",
            "DeleteAsync",
            "UpdateWithVersionAsync",
            "GetVersionHistoryAsync",
            "GetVersionAsync",
            "RestoreVersionAsync",
            "RecordUsageAsync",
            "GetUsageStatsAsync"
        };

        MethodNames(typeof(ISnippetRepository)).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void ITemplateEngine_has_exactly_the_plan_surface()
    {
        var expected = new[] { "RenderAsync", "RenderByNameAsync", "RenderBodyAsync" };

        MethodNames(typeof(ITemplateEngine)).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Repository_methods_accept_cancellation_token_with_default()
    {
        var tokenParam = typeof(ITemplateRepository)
            .GetMethod("GetByIdAsync")!
            .GetParameters()
            .Single(p => p.Name == "ct");

        tokenParam.ParameterType.Should().Be(typeof(CancellationToken));
        tokenParam.HasDefaultValue.Should().BeTrue();
    }
}