using System;
using FluentAssertions;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

// Regression tests for options.ApplyMigrations=false (DBA-managed database): the app login
// has DML-only rights, the schema is provisioned by the shipped script, and EF6 must NEVER
// attempt DDL — not at Initialize, and not lazily on first query (the constructor must not
// re-install the MigrateDatabaseToLatestVersion initializer).
[Collection("Database")]
public class ApplyMigrationsTests
{
    private const string ScratchCs =
        "Server=localhost,1433;Database=TemplateBuilderMvc5NoMigrate;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;";

    [Fact]
    public void Disabled_migrations_install_no_initializer_and_create_nothing()
    {
        TemplateBuilderDbContext.MigrationsEnabled = false;
        try
        {
            using var ctx = new TemplateBuilderDbContext(ScratchCs);
            ctx.Database.Initialize(force: true);
            ctx.Database.Exists().Should().BeFalse("no initializer may run, so no database may be created");
        }
        finally
        {
            TemplateBuilderDbContext.MigrationsEnabled = true;
            using var cleanup = new TemplateBuilderDbContext(ScratchCs);
            cleanup.Database.Delete();
        }
    }

    [Fact]
    public void Enabled_migrations_install_the_initializer_by_default()
    {
        TemplateBuilderDbContext.MigrationsEnabled = true;
        TemplateBuilderDbContextFactory.ConnectionStringProvider = () => ScratchCs;
        try
        {
            using var ctx = new TemplateBuilderDbContext(ScratchCs);
            ctx.Database.Initialize(force: true);
            ctx.Database.Exists().Should().BeTrue();
        }
        finally
        {
            TemplateBuilderDbContextFactory.ConnectionStringProvider = null;
            using var cleanup = new TemplateBuilderDbContext(ScratchCs);
            cleanup.Database.Delete();
        }
    }
}
