using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class TemplateLifecycleColumnsTests
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
    public async Task Create_assigns_nonempty_external_key()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
        t.ExternalKey.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task External_keys_are_unique_per_row()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var a = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
        var b = await repo.CreateAsync(new Template { Name = "B", TemplateType = "Email" });
        a.ExternalKey.Should().NotBe(b.ExternalKey);
    }

    [Fact]
    public async Task Duplicate_external_key_insert_throws()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var a = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
        var dup = new Template { Name = "C", TemplateType = "Email", ExternalKey = a.ExternalKey };
        Func<Task> act = async () => await repo.CreateAsync(dup);
        await act.Should().ThrowAsync<Exception>(); // DbUpdateException under EF6
    }

    [Fact]
    public async Task Delete_removes_template_and_versions()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var t = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
        await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "<p>v1</p>" });
        (await repo.DeleteAsync(t.Id)).Should().BeTrue();
        (await repo.GetByIdAsync(t.Id)).Should().BeNull();
        (await repo.GetVersionHistoryAsync(t.Id)).Should().BeEmpty();
        (await repo.DeleteAsync(t.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task SimplifyTemplateStatus_migration_dropped_workflow_columns()
    {
        using var ctx = CreateContext();
        var sql = await ctx.Database.SqlQuery<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Templates' AND COLUMN_NAME IN ('Status','DraftBody','ReviewComment')")
            .ToListAsync();
        sql.Should().BeEmpty();
    }
}
