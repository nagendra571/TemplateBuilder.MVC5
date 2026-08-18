using FluentAssertions;
using Moq;
using TemplateBuilder.Application.Options;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Tests;

public class ScribanReferenceCatalogTests
{
    public static IEnumerable<object[]> AllEntries()
        => ScribanReferenceCatalog.Entries.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(AllEntries))]
    public async Task Entries_render_without_error_and_match_documented_output(ScribanReferenceEntry entry)
    {
        var engine = new TemplateEngine(new Mock<ITemplateRepository>().Object, new TemplateBuilderOptions());
        var model = new Dictionary<string, object?>
        {
            ["DueDate"] = new DateTime(2026, 8, 18),
            ["UpdatedAt"] = new DateTime(2026, 8, 18, 10, 30, 0),
            ["Amount"] = 1250.00m,
            ["Name"] = "jane doe",
            ["Status"] = "Active",
            ["RichHtml"] = "<b>x</b>",
            ["Items"] = new object[]
            {
                new Dictionary<string, object?> { ["Name"] = "A" },
                new Dictionary<string, object?> { ["Name"] = "B" }
            }
        };

        var result = await engine.RenderBodyAsync(entry.Code, model);

        result.Should().NotContain("error", because: "the entry renders cleanly: " + entry.Label);
        if (entry.Expected is not null)
            result.Should().Be(entry.Expected, because: "the documented output must match the engine for: " + entry.Label);
    }

    [Fact]
    public void Catalog_has_no_duplicate_codes()
    {
        ScribanReferenceCatalog.Entries.Select(e => e.Code)
            .Should().OnlyHaveUniqueItems();
    }
}