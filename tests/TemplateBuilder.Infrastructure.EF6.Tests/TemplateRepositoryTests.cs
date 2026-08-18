using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class TemplateRepositoryTests
{
    private static TemplateBuilderDbContext CreateContext()
    {
        var ctx = new TemplateBuilderDbContext(
            "Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;");
        Database.SetInitializer(new DropCreateDatabaseAlways<TemplateBuilderDbContext>());
        ctx.Database.Initialize(force: true);
        return ctx;
    }

    [Fact]
    public async Task CreateAsync_then_GetByIdAsync_returns_the_created_template()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);

        var created = await repo.CreateAsync(new Template
        {
            Name = "Welcome Email",
            TemplateType = "Email"
        });

        var fetched = await repo.GetByIdAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Welcome Email");
        fetched.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task GetAllAsync_returns_templates_ordered_by_name()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        await repo.CreateAsync(new Template { Name = "Zeta", TemplateType = "Email" });
        await repo.CreateAsync(new Template { Name = "Alpha", TemplateType = "Report" });

        var all = await repo.GetAllAsync();

        all.Select(t => t.Name).Should().Equal("Alpha", "Zeta");
    }

    [Fact]
    public async Task GetByNameAsync_returns_null_when_not_found()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);

        var fetched = await repo.GetByNameAsync("Missing");

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task PublishVersionAsync_sets_current_version_and_increments()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var template = await repo.CreateAsync(new Template { Name = "Notice", TemplateType = "Notice" });

        var v1 = await repo.PublishVersionAsync(template.Id, new TemplateVersion
        {
            TemplateId = template.Id,
            VersionNumber = 1,
            Body = "<p>one</p>"
        });
        var v2 = await repo.PublishVersionAsync(template.Id, new TemplateVersion
        {
            TemplateId = template.Id,
            VersionNumber = 2,
            Body = "<p>two</p>"
        });

        var fetched = await repo.GetByIdAsync(template.Id);
        fetched!.CurrentVersionId.Should().Be(v2.Id);
        fetched.CurrentVersion!.Body.Should().Be("<p>two</p>");

        var history = await repo.GetVersionHistoryAsync(template.Id);
        history.Select(v => v.VersionNumber).Should().Equal(2, 1);

        (await repo.GetNextVersionNumberAsync(template.Id)).Should().Be(3);
        (await repo.GetVersionBodyAsync(v1.Id)).Should().Be("<p>one</p>");
        (await repo.GetCurrentVersionIdAsync(template.Id)).Should().Be(v2.Id);
    }

    [Fact]
    public async Task UpdateTemplateAsync_persists_changes()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var template = await repo.CreateAsync(new Template { Name = "Old Name", TemplateType = "Email" });

        template.Name = "New Name";
        await repo.UpdateTemplateAsync(template);

        var fetched = await repo.GetByIdAsync(template.Id);
        fetched!.Name.Should().Be("New Name");
        fetched.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task CreateAsync_throws_on_duplicate_name()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        await repo.CreateAsync(new Template { Name = "Unique", TemplateType = "Email" });

        var act = () => repo.CreateAsync(new Template { Name = "Unique", TemplateType = "Email" });

        await act.Should().ThrowAsync<System.Data.Entity.Infrastructure.DbUpdateException>();
    }

    [Fact]
    public async Task UpdateTemplateAsync_persists_sample_data()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var template = await repo.CreateAsync(new Template { Name = "SampleDataTest", TemplateType = "Email" });

        template.SampleData = "{\"RecipientName\":\"Jane Doe\"}";
        await repo.UpdateTemplateAsync(template);

        var fetched = await repo.GetByIdAsync(template.Id);
        fetched!.SampleData.Should().Be("{\"RecipientName\":\"Jane Doe\"}");
    }

    [Fact]
    public async Task UpdateTemplateAsync_clears_sample_data_with_null()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var template = await repo.CreateAsync(new Template { Name = "SampleDataClear", TemplateType = "Email" });
        template.SampleData = "{\"a\":1}";
        await repo.UpdateTemplateAsync(template);

        template.SampleData = null;
        await repo.UpdateTemplateAsync(template);

        var fetched = await repo.GetByIdAsync(template.Id);
        fetched!.SampleData.Should().BeNull();
    }
}