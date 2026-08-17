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
}