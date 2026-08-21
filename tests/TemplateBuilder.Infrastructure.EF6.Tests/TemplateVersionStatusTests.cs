using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;
using Xunit;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class TemplateVersionStatusTests
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
    public async Task PublishVersion_defaults_IsActive_true()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
        var v = await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "<p>x</p>" });
        v.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetLastActiveVersion_skips_drafts_and_returns_latest_active()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
        await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "active-1", IsActive = true });
        await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "draft-2", IsActive = false });
        var active3 = await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "active-3", IsActive = true });
        await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "draft-4", IsActive = false });

        var result = await repo.GetLastActiveVersionAsync(t.Id);

        result.Should().NotBeNull();
        result!.VersionNumber.Should().Be(active3.VersionNumber);
        result.Body.Should().Be("active-3");
    }

    [Fact]
    public async Task GetLastActiveVersion_returns_null_when_all_versions_are_drafts()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
        await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "draft", IsActive = false });
        (await repo.GetLastActiveVersionAsync(t.Id)).Should().BeNull();
    }
}
