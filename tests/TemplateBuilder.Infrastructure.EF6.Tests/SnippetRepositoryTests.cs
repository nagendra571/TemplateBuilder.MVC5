using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class SnippetRepositoryTests
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
    public async Task CreateAsync_then_DeleteAsync_removes_the_snippet()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);

        var created = await repo.CreateAsync(new Snippet { Name = "Footer", Body = "<p>Thanks</p>" });
        await repo.DeleteAsync(created.Id);

        var fetched = await repo.GetByIdAsync(created.Id);
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_returns_snippets_ordered_by_name()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);
        await repo.CreateAsync(new Snippet { Name = "Zebra", Body = "z" });
        await repo.CreateAsync(new Snippet { Name = "Apple", Body = "a" });

        var all = await repo.GetAllAsync();

        all.Select(s => s.Name).Should().Equal("Apple", "Zebra");
    }

    [Fact]
    public async Task CreateAsync_sets_timestamps()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);

        var created = await repo.CreateAsync(new Snippet { Name = "Header", Body = "<h1>x</h1>" });

        created.Id.Should().BeGreaterThan(0);
        created.CreatedAt.Should().NotBe(default);
        created.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task DeleteAsync_is_idempotent()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);

        var act = () => repo.DeleteAsync(999999);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateAsync_throws_on_duplicate_name()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);
        await repo.CreateAsync(new Snippet { Name = "Solo", Body = "x" });

        var act = () => repo.CreateAsync(new Snippet { Name = "Solo", Body = "y" });

        await act.Should().ThrowAsync<System.Data.Entity.Infrastructure.DbUpdateException>();
    }

    [Fact]
    public async Task UpdateWithVersionAsync_creates_version_when_body_changes()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);
        var s = await repo.CreateAsync(new Snippet { Name = "V Snippet", Description = "d", Body = "v1" });

        s.Body = "v2";
        await repo.UpdateWithVersionAsync(s, "v1", "second version", "bob");

        var versions = await repo.GetVersionHistoryAsync(s.Id);
        versions.Should().HaveCount(2);
        versions[0].VersionNumber.Should().Be(1);
        versions[1].VersionNumber.Should().Be(2);
        versions[1].Body.Should().Be("v2");
        versions[1].CreatedBy.Should().Be("bob");
        (await repo.GetByIdAsync(s.Id))!.Body.Should().Be("v2");
    }

    [Fact]
    public async Task UpdateWithVersionAsync_skips_version_when_body_unchanged()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);
        var s = await repo.CreateAsync(new Snippet { Name = "No Change Snippet", Description = "d", Body = "same" });

        await repo.UpdateWithVersionAsync(s, "same", "no change", "bob");

        (await repo.GetVersionHistoryAsync(s.Id)).Should().HaveCount(0);
    }

    [Fact]
    public async Task RestoreVersionAsync_creates_new_version_with_restored_body()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);
        var s = await repo.CreateAsync(new Snippet { Name = "Restore Snippet", Description = "d", Body = "v1" });
        s.Body = "v2";
        await repo.UpdateWithVersionAsync(s, "v1", "second", "bob");
        var versions = await repo.GetVersionHistoryAsync(s.Id);
        var v1 = versions[0];

        var restored = await repo.RestoreVersionAsync(s.Id, v1.Id, "bob");

        restored.Body.Should().Be("v1");
        var after = await repo.GetVersionHistoryAsync(s.Id);
        after.Should().HaveCount(3);
        after[2].VersionNumber.Should().Be(3);
        after[2].ChangeComment.Should().Be("Restored from v1");
    }

    [Fact]
    public async Task RecordUsageAsync_and_GetUsageStatsAsync_report_inserts()
    {
        using var ctx = CreateContext();
        var repo = new SnippetRepository(ctx);
        var s = await repo.CreateAsync(new Snippet { Name = "Used Snippet", Description = "d", Body = "b" });

        await repo.RecordUsageAsync(s.Id, 11, "bob");
        await repo.RecordUsageAsync(s.Id, 11, "bob");
        await repo.RecordUsageAsync(s.Id, 12, "alice");

        var stats = await repo.GetUsageStatsAsync();
        var stat = stats.Single(x => x.SnippetId == s.Id);
        stat.UsageCount.Should().Be(3);
        stat.TemplateCount.Should().Be(2);
        stat.LastUsedAt.Should().NotBeNull();
    }
}