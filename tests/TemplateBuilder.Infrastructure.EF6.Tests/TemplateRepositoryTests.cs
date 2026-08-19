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

    [Fact]
    public async Task PublishVersionAsync_assigns_incrementing_version_numbers()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "Publish Race", TemplateType = "Email" });

        var v1 = await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = "a" });
        var v2 = await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = "b" });

        v1.VersionNumber.Should().Be(1);
        v2.VersionNumber.Should().Be(2);
        (await repo.GetByIdAsync(t.Id))!.CurrentVersionId.Should().Be(v2.Id);
    }

    [Fact]
    public async Task PublishVersionAsync_applies_template_callback_after_insert()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "Callback Publish", TemplateType = "Email", Status = TemplateStatus.Approved });

        await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = "body" },
            tb => { tb.Status = TemplateStatus.Published; tb.DraftBody = null; });

        var fetched = await repo.GetByIdAsync(t.Id);
        fetched!.Status.Should().Be(TemplateStatus.Published);
        fetched.DraftBody.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_publishes_never_duplicate_a_version_number()
    {
        using (var seed = CreateContext()) { /* drop+create schema once, before any parallel work */ }
        var t = await CreateTemplate("Concurrent Race");

        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            using var ctx = CreateContextNoRecreate();   // EF6 DbContext is not thread-safe — one context per task
            var repo = new TemplateRepository(ctx);
            return await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = $"body {i}" });
        });
        var versions = await Task.WhenAll(tasks);

        versions.Select(v => v.VersionNumber).Distinct().Count().Should().Be(5);
    }

    private static TemplateBuilderDbContext CreateContextNoRecreate()
    {
        var ctx = new TemplateBuilderDbContext(
            "Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;");
        ctx.Database.Initialize(force: false);   // schema already exists — must NOT re-run DropCreateDatabaseAlways mid-test
        return ctx;
    }

    [Fact]
    public async Task Template_status_and_draft_body_persist()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "Status Persist", TemplateType = "Email" });
        t.Status = TemplateStatus.Review;
        t.DraftBody = "draft";
        t.ReviewComment = "nope";
        await repo.UpdateTemplateAsync(t);

        var fetched = await repo.GetByIdAsync(t.Id);
        fetched!.Status.Should().Be(TemplateStatus.Review);
        fetched.DraftBody.Should().Be("draft");
        fetched.ReviewComment.Should().Be("nope");
    }

    private static async Task<Template> CreateTemplate(string name)
    {
        using var ctx = CreateContext();
        return await new TemplateRepository(ctx).CreateAsync(new Template { Name = name, TemplateType = "Email" });
    }
}