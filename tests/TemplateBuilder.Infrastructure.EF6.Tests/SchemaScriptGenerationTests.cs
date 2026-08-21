using System;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Migrations;
using System.Data.Entity.Migrations.Infrastructure;
using System.IO;
using FluentAssertions;
using TemplateBuilder.Infrastructure.EF6.Migrations;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

// Golden-file gate for the DBA-provisioned-schema script.
//
// The committed script (src/TemplateBuilder.Editor.Mvc5/Scripts/TemplateBuilder.schema.<version>.sql)
// is GENERATED from the EF6 migration chain via DbMigrator.ScriptUpdate(null, null), so there is a
// single source of truth: the migrations. This test regenerates the script in memory and compares it
// byte-for-byte with the committed file — it fails whenever the migration chain drifts from the
// shipped script (the class of stale-artifact bug that has bitten this repo before).
//
// Regeneration: run with TB_REGEN_SCHEMA=1 (e.g. `TB_REGEN_SCHEMA=1 dotnet test ...`) — the test then
// rewrites the committed file instead of comparing. Commit the regenerated file, then run the test
// normally as the gate. IMPORTANT: when the package version bumps, update the filename below and the
// csproj pack entry to match.
public class SchemaScriptGenerationTests
{
    private const string ScratchCs =
        "Server=localhost,1433;Database=TemplateBuilderMvc5ScriptGen;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;";

    private static readonly string ScriptPath = Path.Combine(
        FindRepoRoot(),
        "src", "TemplateBuilder.Editor.Mvc5", "Scripts", "TemplateBuilder.schema.1.3.2.sql");

    private static string Generate()
    {
        var configuration = new Configuration
        {
            TargetDatabase = new DbConnectionInfo(ScratchCs, "System.Data.SqlClient")
        };
        return new MigratorScriptingDecorator(new DbMigrator(configuration)).ScriptUpdate(null, null);
    }

    [Fact]
    public void SchemaScript_matches_the_committed_file()
    {
        TemplateBuilderDbContextFactory.ConnectionStringProvider = () => ScratchCs;
        try
        {
            if (Environment.GetEnvironmentVariable("TB_REGEN_SCHEMA") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
                File.WriteAllText(ScriptPath, Generate());
                return;
            }

            var committed = File.ReadAllText(ScriptPath);
            Generate().Should().Be(committed);
        }
        finally
        {
            TemplateBuilderDbContextFactory.ConnectionStringProvider = null;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TemplateBuilder.Mvc5.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repo root from " + AppContext.BaseDirectory);
    }
}
