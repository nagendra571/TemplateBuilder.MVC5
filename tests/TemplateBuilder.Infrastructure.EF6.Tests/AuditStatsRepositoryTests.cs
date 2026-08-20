using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;
using Xunit;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class AuditStatsRepositoryTests
{
    private static TemplateBuilderDbContext CreateContext()
    {
        var ctx = new TemplateBuilderDbContext(
            "Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;");
        Database.SetInitializer(new DropCreateDatabaseAlways<TemplateBuilderDbContext>());
        ctx.Database.Initialize(force: true);
        return ctx;
    }

    private static async Task SeedAsync(TemplateBuilderDbContext ctx, params AuditLog[] rows)
    {
        foreach (var row in rows) ctx.AuditLogs.Add(row);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Stats_totals_by_entity_type_and_unique_actors()
    {
        using var ctx = CreateContext();
        await SeedAsync(ctx,
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Created, Actor = "bob", OccurredAt = DateTime.UtcNow },
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Published, Actor = "alice", OccurredAt = DateTime.UtcNow },
            new AuditLog { EntityType = "Snippet", EntityId = 1, Action = AuditActions.SnippetCreated, Actor = "bob", OccurredAt = DateTime.UtcNow });

        var stats = await new AuditStatsRepository(ctx).GetStatsAsync(new AuditQuery());

        stats.Total.Should().Be(3);
        stats.TemplateCount.Should().Be(2);
        stats.SnippetCount.Should().Be(1);
        stats.UniqueActors.Should().Be(2);
    }

    [Fact]
    public async Task Stats_daily_buckets_default_to_trailing_30_days_zero_filled()
    {
        using var ctx = CreateContext();
        var twoDaysAgo = DateTime.UtcNow.Date.AddDays(-2);
        await SeedAsync(ctx,
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Created, Actor = "bob", OccurredAt = twoDaysAgo.AddHours(10) },
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Published, Actor = "bob", OccurredAt = DateTime.UtcNow });

        var stats = await new AuditStatsRepository(ctx).GetStatsAsync(new AuditQuery());

        stats.DailyBuckets.Should().HaveCount(30);
        stats.DailyBuckets.First().Date.Should().Be(DateTime.UtcNow.Date.AddDays(-29));
        stats.DailyBuckets.Last().Date.Should().Be(DateTime.UtcNow.Date);
        stats.DailyBuckets.Sum(b => b.Count).Should().Be(2);
        stats.DailyBuckets.Should().ContainSingle(b => b.Date == twoDaysAgo && b.Count == 1);
        stats.DailyBuckets.Should().ContainSingle(b => b.Date == DateTime.UtcNow.Date && b.Count == 1);
        stats.DailyBuckets.Count(b => b.Count == 0).Should().Be(28);
    }

    [Fact]
    public async Task Stats_daily_buckets_respect_from_to_window()
    {
        using var ctx = CreateContext();
        var from = DateTime.UtcNow.Date.AddDays(-6);
        var to = DateTime.UtcNow.Date.AddDays(-4);
        await SeedAsync(ctx,
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Created, Actor = "bob", OccurredAt = from.AddHours(9) },
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Published, Actor = "bob", OccurredAt = to.AddHours(23).AddMinutes(59).AddSeconds(59) },
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Edited, Actor = "bob", OccurredAt = DateTime.UtcNow });

        var stats = await new AuditStatsRepository(ctx).GetStatsAsync(new AuditQuery { From = from, To = to });

        stats.Total.Should().Be(2);
        stats.DailyBuckets.Should().HaveCount(3);
        stats.DailyBuckets.First().Date.Should().Be(from);
        stats.DailyBuckets.Last().Date.Should().Be(to);
        stats.DailyBuckets.Sum(b => b.Count).Should().Be(2);
        stats.DailyBuckets.Should().ContainSingle(b => b.Date == from && b.Count == 1);
        stats.DailyBuckets.Should().ContainSingle(b => b.Date == to && b.Count == 1);
    }

    [Fact]
    public async Task Stats_respects_entity_type_and_search_filters()
    {
        using var ctx = CreateContext();
        await SeedAsync(ctx,
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Created, Actor = "bob", OccurredAt = DateTime.UtcNow },
            new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Published, Actor = "alice", OccurredAt = DateTime.UtcNow },
            new AuditLog { EntityType = "Snippet", EntityId = 2, Action = AuditActions.SnippetCreated, Actor = "bob", OccurredAt = DateTime.UtcNow });

        var repo = new AuditStatsRepository(ctx);

        var snippets = await repo.GetStatsAsync(new AuditQuery { EntityType = "Snippet" });
        snippets.Total.Should().Be(1);
        snippets.SnippetCount.Should().Be(1);
        snippets.TemplateCount.Should().Be(0);

        var search = await repo.GetStatsAsync(new AuditQuery { Search = "alice" });
        search.Total.Should().Be(1);
        search.UniqueActors.Should().Be(1);
    }

    [Fact]
    public async Task Stats_empty_database_returns_zero_totals_with_window_buckets()
    {
        using var ctx = CreateContext();
        var stats = await new AuditStatsRepository(ctx).GetStatsAsync(new AuditQuery());

        stats.Total.Should().Be(0);
        stats.TemplateCount.Should().Be(0);
        stats.SnippetCount.Should().Be(0);
        stats.UniqueActors.Should().Be(0);
        stats.FirstOccurrence.Should().BeNull();
        stats.LastOccurrence.Should().BeNull();
        stats.DailyBuckets.Should().HaveCount(30);
        stats.DailyBuckets.Sum(b => b.Count).Should().Be(0);
    }
}
