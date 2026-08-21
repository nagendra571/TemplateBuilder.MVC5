using System;
using FluentAssertions;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

// Regression tests for the client-reported issue: a consumer who configures only
// options.ConnectionString (e.g. name "TemplateDb") in RegisterTemplateBuilderEditor
// got "No connection string named 'TemplateBuilderDbContext' could be found in the
// application config file." Root cause: EF6's DbMigrator discovers the design-time
// TemplateBuilderDbContextFactory by convention ({ContextName}Factory) at RUNTIME too,
// and its Create() resolved a NAMED connection string. The fix: RegisterTemplateBuilderEditor
// sets TemplateBuilderDbContextFactory.ConnectionStringProvider to the consumer's explicit
// connection string, so runtime migrations never consult the app config by name.
[Collection("Database")]
public class ConnectionStringResolutionTests
{
    private const string ExplicitCs =
        "Server=localhost,1433;Database=TemplateBuilderMvc5Repro;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;";

    [Fact]
    public void DesignTimeFactory_still_resolves_the_named_connection_string()
    {
        // Design-time behavior (Package Manager Console) is unchanged: without the runtime
        // provider, the factory requires a config entry named "TemplateBuilderDbContext".
        TemplateBuilderDbContextFactory.ConnectionStringProvider = null;
        var factory = new TemplateBuilderDbContextFactory();
        var act = () =>
        {
            using var ctx = factory.Create();
            _ = ctx.Database.Connection; // forces the named-string resolution
        };
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TemplateBuilderDbContext*");
    }

    [Fact]
    public void Factory_uses_the_explicit_connection_string_when_provider_is_set()
    {
        TemplateBuilderDbContextFactory.ConnectionStringProvider = () => ExplicitCs;
        try
        {
            using var ctx = new TemplateBuilderDbContextFactory().Create();
            ctx.Database.Connection.ConnectionString.Should().Be(ExplicitCs);
        }
        finally
        {
            TemplateBuilderDbContextFactory.ConnectionStringProvider = null;
        }
    }

    [Fact]
    public void Runtime_migrations_use_the_explicit_connection_string_when_provider_is_set()
    {
        TemplateBuilderDbContextFactory.ConnectionStringProvider = () => ExplicitCs;
        try
        {
            using var ctx = new TemplateBuilderDbContext(ExplicitCs);
            ctx.Database.Initialize(force: true);
            ctx.Database.Exists().Should().BeTrue();
        }
        finally
        {
            TemplateBuilderDbContextFactory.ConnectionStringProvider = null;
            using var cleanup = new TemplateBuilderDbContext(ExplicitCs);
            cleanup.Database.Delete();
        }
    }
}
