using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;
using Xunit;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class AuditRepositoryTests
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
    public async Task Add_then_query_round_trips()
    {
        using var ctx = CreateContext();
        var repo = new AuditRepository(ctx);
        await repo.AddAsync(new AuditLog
        {
            EntityType = "Template", EntityId = 3, Action = AuditActions.Published,
            Actor = "bob", OccurredAt = DateTime.UtcNow, AfterState = "{}"
        });

        var rows = await repo.QueryAsync(new AuditQuery { EntityType = "Template", EntityId = 3 });
        rows.Should().ContainSingle(a => a.Action == AuditActions.Published && a.Actor == "bob");
    }

    [Fact]
    public async Task Query_filters_by_action_and_actor()
    {
        using var ctx = CreateContext();
        var repo = new AuditRepository(ctx);
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Created, Actor = "bob", OccurredAt = DateTime.UtcNow });
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Approved, Actor = "alice", OccurredAt = DateTime.UtcNow });

        var rows = await repo.QueryAsync(new AuditQuery { EntityId = 1, Action = AuditActions.Created });
        rows.Should().ContainSingle(a => a.Actor == "bob");

        (await repo.CountAsync(new AuditQuery { EntityId = 1 })).Should().Be(2);
    }

    [Fact]
    public async Task GetLastOccurrence_returns_most_recent()
    {
        using var ctx = CreateContext();
        var repo = new AuditRepository(ctx);
        var old = DateTime.UtcNow.AddHours(-2);
        var recent = DateTime.UtcNow.AddMinutes(-1);
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.DraftSaved, Actor = "bob", OccurredAt = old });
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.DraftSaved, Actor = "bob", OccurredAt = recent });

        var last = await repo.GetLastOccurrenceAsync("Template", 1, AuditActions.DraftSaved);
        last.Should().BeCloseTo(recent, TimeSpan.FromSeconds(2));
    }
}