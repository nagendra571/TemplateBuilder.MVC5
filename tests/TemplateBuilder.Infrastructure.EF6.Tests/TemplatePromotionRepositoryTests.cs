using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;
using Xunit;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class TemplatePromotionRepositoryTests
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
    public async Task Add_with_versions_preserves_original_version_numbers()
    {
        using var ctx = CreateContext();
        var repo = new TemplatePromotionRepository(ctx);
        var t = await repo.AddWithVersionsAsync(
            new Template { Name = "P", TemplateType = "Email", ExternalKey = Guid.NewGuid() },
            new List<TemplateVersion>
            {
                new TemplateVersion { VersionNumber = 1, Body = "<p>one</p>" },
                new TemplateVersion { VersionNumber = 2, Body = "<p>two</p>" }
            });
        var history = await ctx.TemplateVersions.Where(v => v.TemplateId == t.Id).OrderBy(v => v.VersionNumber).ToListAsync();
        history.Select(v => v.VersionNumber).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Append_versions_continue_from_max_plus_one()
    {
        using var ctx = CreateContext();
        var repo = new TemplatePromotionRepository(ctx);
        var t = await repo.AddWithVersionsAsync(
            new Template { Name = "P", TemplateType = "Email", ExternalKey = Guid.NewGuid() },
            new List<TemplateVersion> { new TemplateVersion { VersionNumber = 1, Body = "one" } });
        var assigned = await repo.AppendVersionsAsync(t.Id, new List<TemplateVersion>
        {
            new TemplateVersion { VersionNumber = 1, Body = "imported a" },
            new TemplateVersion { VersionNumber = 2, Body = "imported b" }
        });
        assigned.Should().Equal(2, 3);
        (await repo.GetMaxVersionNumberAsync(t.Id)).Should().Be(3);
    }

    [Fact]
    public async Task GetByExternalKey_round_trips()
    {
        using var ctx = CreateContext();
        var repo = new TemplatePromotionRepository(ctx);
        var key = Guid.NewGuid();
        await repo.AddWithVersionsAsync(new Template { Name = "P", TemplateType = "Email", ExternalKey = key }, new List<TemplateVersion>());
        (await repo.GetByExternalKeyAsync(key)).Should().NotBeNull();
        (await repo.GetByExternalKeyAsync(Guid.NewGuid())).Should().BeNull();
    }
}
