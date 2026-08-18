# v1.1 Authoring Superpowers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** v1.1.0 of TemplateBuilder.Editor.Mvc5 that beats the origin product with server-generated, type-aware sample data (schema-first, token fallback, loop-aware arrays), per-template persisted preview data, an enriched field palette (type badges, used markers, search), and a tested Scriban reference panel.

**Architecture:** Additive release on the v1.0.0 baseline. One nullable column (`Templates.SampleData`) via a new EF6 migration; a new `SampleDataGenerator` service in `Application` (Scriban AST walking via `ScriptNode.Children`, no new dependencies); two new JSON endpoints on `TemplatesController`; UI work in `Edit.cshtml` + `template-editor.js`/`.css`. Nothing existing changes shape — all v1.0.0 routes, payloads, views, and tests stay green.

**Tech Stack:** net48 / MVC5 / EF6 6.5.1 / Scriban 7.2.6 / Newtonsoft.Json 13.0.3 / RazorGenerator precompiled views / Unity DI / xunit + FluentAssertions + Moq / xsp4 (mono) sample host.

**Spec:** `docs/superpowers/specs/2026-08-18-authoring-superpowers-design.md` — this plan argues from that spec; read both.

## Global Constraints

- **No breakage contract:** every v1.0.0 behavior, route, payload, view, and test stays green. All changes additive.
- **NuGet safety:** the published `1.0.0` nupkg is never modified, re-packed, or re-pushed. This plan packs **only** `1.1.0` (`<Version>1.1.0</Version>` in the editor csproj). **Do NOT push anything to nuget.org** — that requires explicit user confirmation.
- **Fork decisions (documented in the spec §9):** `Template.SampleData` property and the `SampleDataGenerator` service are deliberate deviations from the "verbatim Domain/Application" rule. Commit messages for those tasks must state the rationale.
- **Test commands:** `dotnet test` works for net48 on Linux with Test.Sdk 18.9.0 (verified Tasks 2/3/5 of the original plan). EF6 tests require the Docker SQL container `mssql-tb` (`Server=localhost,1433;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True`) — verify it is running before Task 1.
- **JS gate:** `node --check` on `template-editor.js` before every JS commit.
- **Views:** RazorGenerator precompiles views; on Linux the build regenerates `obj/CodeGen` via `eng/RazorGenDriver.cs` automatically (BLOCKERS #10) — no manual codegen step.
- **xsp4 smoke:** follow the recipe in `BLOCKERS.md` #11 and the flow in `DASHBOARD.md` ("How to run / verify"). JSON endpoints need the `RequestVerificationToken` header + cookie (BLOCKERS #13). Mono quirk: never send *nested* JSON objects in curl bodies (HttpAntiForgeryException) — the request bodies in this plan are flat strings only.
- **Commit style:** conventional commits (`feat:`/`fix:`/`chore:`/`docs:`), one per task, with fork-decision rationale where required.
- **Scaffold probe convention:** headless EF6 `MigrationScaffolder` recipe from `BLOCKERS.md` #8, probe project at `/tmp/opencode/ef-scaffold`.

---

### Task 1: `Template.SampleData` entity + EF6 round-trip + headless migration

**Files:**
- Modify: `src/TemplateBuilder.Domain/Entities/Template.cs`
- Modify: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateRepositoryTests.cs`
- Create: `src/TemplateBuilder.Infrastructure.EF6/Migrations/AddSampleDataToTemplates.cs` (+ `.Designer.cs`, `.resx`)
- Create (throwaway, outside repo): `/tmp/opencode/ef-scaffold/ScaffoldSampleData.cs`, `/tmp/opencode/ef-scaffold/MigrationCheck.cs`

**Interfaces:**
- Produces: `Template.SampleData` (`string?` — nullable, no fluent config needed; EF6 convention maps to `nvarchar(max)` nullable); migration `AddSampleDataToTemplates` (AddColumn only).

- [ ] **Step 1: Verify the SQL container is up**

Run: `docker ps --format "{{.Names}} {{.Status}}"`
Expected: `mssql-tb Up ...` (if not, start it per the repo's docker recipe before continuing).

- [ ] **Step 2: Add the entity property (fork decision #1)**

In `src/TemplateBuilder.Domain/Entities/Template.cs`, after `public string? Description { get; set; }`:

```csharp
    public string? SampleData { get; set; }
```

Commit message rationale (at commit time): fork decision #1 — origin has no persisted preview data; additive nullable column.

- [ ] **Step 3: Write the failing tests**

Append to `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task UpdateTemplateAsync_persists_sample_data()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var template = await repo.CreateAsync(new Template { Name = "SampleDataTest", TemplateType = "Email" });

        template.SampleData = "{\"RecipientName\":\"Jane Doe\"}";
        await repo.UpdateTemplateAsync(template);

        var fetched = await repo.GetByIdAsync(template.Id);
        fetched!.SampleData.Should().Be("{\"RecipientName\":\"Jane Doe\"}");
    }

    [Fact]
    public async Task UpdateTemplateAsync_clears_sample_data_with_null()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);
        var template = await repo.CreateAsync(new Template { Name = "SampleDataClear", TemplateType = "Email" });
        template.SampleData = "{\"a\":1}";
        await repo.UpdateTemplateAsync(template);

        template.SampleData = null;
        await repo.UpdateTemplateAsync(template);

        var fetched = await repo.GetByIdAsync(template.Id);
        fetched!.SampleData.Should().BeNull();
    }
```

Note: these tests use `DropCreateDatabaseAlways` (model-driven schema), so they validate repository behavior; the migration itself is validated in Step 7.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/`
Expected: PASS — both new tests and all 11 existing ones (the model already includes `SampleData`, so the create-from-model path works).

- [ ] **Step 5: Build Infrastructure.EF6 for the scaffolder**

Run: `dotnet build src/TemplateBuilder.Infrastructure.EF6/TemplateBuilder.Infrastructure.EF6.csproj -c Debug --nologo`
Expected: `0 Error(s)` — the new-migration files do not exist yet; the assembly now contains the model with `SampleData`.

- [ ] **Step 6: Scaffold the migration headlessly**

Create `/tmp/opencode/ef-scaffold/ScaffoldSampleData.cs` (BLOCKERS #8 recipe, adapted to dump all three outputs and build the `.resx`):

```csharp
using System;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Text;

class P
{
    static void Main()
    {
        using (var master = new SqlConnection("Server=localhost,1433;Database=master;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;"))
        {
            master.Open();
            new SqlCommand("IF DB_ID('TemplateBuilderMvc5Scaffold') IS NOT NULL BEGIN ALTER DATABASE TemplateBuilderMvc5Scaffold SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE TemplateBuilderMvc5Scaffold; END", master).ExecuteNonQuery();
        }

        var infraAsm = Assembly.LoadFrom("/workspaces/TemplateBuilder.Mvc5/src/TemplateBuilder.Infrastructure.EF6/bin/Debug/net48/TemplateBuilder.Infrastructure.EF6.dll");
        var cfgType = infraAsm.GetType("TemplateBuilder.Infrastructure.EF6.Migrations.Configuration");
        var cfg = Activator.CreateInstance(cfgType);

        var ef = typeof(System.Data.Entity.DbContext).Assembly;
        var scaffolderType = ef.GetType("System.Data.Entity.Migrations.Design.MigrationScaffolder");
        var scaffolder = Activator.CreateInstance(scaffolderType, cfg);
        var scaffold = scaffolderType.GetMethod("Scaffold", new[] { typeof(string), typeof(bool) });
        var migration = scaffold.Invoke(scaffolder, new object[] { "AddSampleDataToTemplates", false });

        var outDir = "/tmp/opencode/ef-scaffold";
        File.WriteAllText($"{outDir}/AddSampleDataToTemplates.cs",
            (string)migration.GetType().GetProperty("UserCode").GetValue(migration));
        File.WriteAllText($"{outDir}/AddSampleDataToTemplates.Designer.cs",
            (string)migration.GetType().GetProperty("DesignerCode").GetValue(migration));

        var resources = (System.Collections.IDictionary)migration.GetType().GetProperty("Resources").GetValue(migration);
        var resx = new StringBuilder();
        resx.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        resx.AppendLine("<root>");
        resx.AppendLine("  <resheader name=\"resmimetype\"><value>text/microsoft-resx</value></resheader>");
        resx.AppendLine("  <resheader name=\"version\"><value>2.0</value></resheader>");
        resx.AppendLine("  <resheader name=\"reader\"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>");
        resx.AppendLine("  <resheader name=\"writer\"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>");
        foreach (var key in resources.Keys)
        {
            resx.AppendLine($"  <data name=\"{key}\" type=\"System.String\">");
            resx.AppendLine($"    <value>{resources[key]}</value>");
            resx.AppendLine("  </data>");
        }
        resx.AppendLine("</root>");
        File.WriteAllText($"{outDir}/AddSampleDataToTemplates.resx", resx.ToString());
        Console.WriteLine("DUMP OK");
    }
}
```

Run:
```bash
cd /tmp/opencode/ef-scaffold && mcs -out:scaffold.exe ScaffoldSampleData.cs \
  -r:/workspaces/TemplateBuilder.Mvc5/src/TemplateBuilder.Infrastructure.EF6/bin/Debug/net48/EntityFramework.dll \
  -r:/workspaces/TemplateBuilder.Mvc5/src/TemplateBuilder.Infrastructure.EF6/bin/Debug/net48/TemplateBuilder.Infrastructure.EF6.dll \
  -r:System.Data.dll && mono scaffold.exe
```
Expected: `DUMP OK`. Verify `AddSampleDataToTemplates.cs` contains only an `AddColumn` for `SampleData` (and the namespace is `TemplateBuilder.Infrastructure.EF6.Migrations` — fix if the scaffolder used a different namespace).

- [ ] **Step 7: Copy + verify the migration**

Copy the three files into `src/TemplateBuilder.Infrastructure.EF6/Migrations/` (same names). Then verify the migration actually runs and adds the column — create `/tmp/opencode/ef-scaffold/MigrationCheck.cs`:

```csharp
using System;
using System.Data.Entity;
using TemplateBuilder.Infrastructure.EF6.Data;

class P
{
    static void Main()
    {
        var ctx = new TemplateBuilderDbContext(
            "Server=localhost,1433;Database=TemplateBuilderMvc5Scaffold;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;");
        ctx.Database.Initialize(force: true);
        Console.WriteLine("MIGRATED");
    }
}
```

Run (the constructor already sets `MigrateDatabaseToLatestVersion`, which runs the new migration):
```bash
cd /tmp/opencode/ef-scaffold && mcs -out:check.exe MigrationCheck.cs \
  -r:/workspaces/TemplateBuilder.Mvc5/src/TemplateBuilder.Infrastructure.EF6/bin/Debug/net48/EntityFramework.dll \
  -r:/workspaces/TemplateBuilder.Mvc5/src/TemplateBuilder.Infrastructure.EF6/bin/Debug/net48/TemplateBuilder.Infrastructure.EF6.dll \
  -r:System.Data.dll && mono check.exe
```
Expected: `MIGRATED`. Then verify the schema (grep the migration ID first from `__MigrationHistory`, but the column check is the gate):

```bash
sqlcmd -S localhost,1433 -U sa -P 'TemplateBuilder!2026' -d TemplateBuilderMvc5Scaffold -Q "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Templates' AND COLUMN_NAME='SampleData'"
```
Expected: one row — `SampleData`, `nvarchar`, `YES`. (If sqlcmd is unavailable, use the EF6 test context's `DropCreateDatabaseAlways` + a `DbContext.Database.SqlQuery<string>` probe — the sqlcmd recipe is the established one from Task 4.)

- [ ] **Step 8: Rebuild + full EF6 test suite + commit**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/` then `dotnet build TemplateBuilder.Mvc5.sln --nologo`
Expected: all EF6 tests PASS (13 now) and the solution builds with 0 errors.

```bash
git add src/TemplateBuilder.Domain/Entities/Template.cs \
  src/TemplateBuilder.Infrastructure.EF6/Migrations/AddSampleDataToTemplates.cs \
  src/TemplateBuilder.Infrastructure.EF6/Migrations/AddSampleDataToTemplates.Designer.cs \
  src/TemplateBuilder.Infrastructure.EF6/Migrations/AddSampleDataToTemplates.resx \
  tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateRepositoryTests.cs
git commit -m "feat: add Template.SampleData column and AddSampleDataToTemplates migration

Fork decision #1 (documented in the v1.1 spec): origin has no persisted
preview data; additive nullable nvarchar(max) column so the client's DB
upgrades in place via MigrateDatabaseToLatestVersion. Migration generated
with EF6's MigrationScaffolder (headless Add-Migration, BLOCKERS #8)."
```

---

### Task 2: `SampleDataGenerator` + `ScribanReferenceCatalog` (Application, TDD)

**Files:**
- Create: `src/TemplateBuilder.Application/Services/SampleDataGenerator.cs` (contains `ISampleDataGenerator`, `SampleDataGenerator`)
- Create: `src/TemplateBuilder.Application/Services/ScribanReferenceCatalog.cs` (contains `ScribanReferenceEntry`, `ScribanReferenceCatalog`)
- Create: `tests/TemplateBuilder.Application.Tests/SampleDataGeneratorTests.cs`
- Create: `tests/TemplateBuilder.Application.Tests/ScribanReferenceCatalogTests.cs`

**Interfaces:**
- Produces:
  - `ISampleDataGenerator.GenerateAsync(string? viewName, string? templateBody, CancellationToken ct = default) → Task<Dictionary<string, object?>>` (dictionary, NOT JSON — the controller serializes)
  - `ScribanReferenceCatalog.Entries` → `IReadOnlyList<ScribanReferenceEntry>` where `ScribanReferenceEntry { string Group; string Label; string Code; string? Expected; }` (init-only properties)
- Consumes: `ISqlViewDiscoveryService.GetViewColumnsAsync(viewName, ct)` → `IReadOnlyList<SqlColumnInfo>` (`Name`, `DataType`, `MaxLength`, `IsNullable`); `Scriban` 7.2.6 AST (`Template.Parse`, `ScriptNode.Children`, `ScriptForStatement`, `ScriptMemberExpression`, `ScriptVariable`).

- [ ] **Step 1: Write the failing tests for `SampleDataGenerator`**

Create `tests/TemplateBuilder.Application.Tests/SampleDataGeneratorTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using TemplateBuilder.Application.DTOs;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Tests;

public class SampleDataGeneratorTests
{
    private static Mock<ISqlViewDiscoveryService> ViewWith(params SqlColumnInfo[] columns)
    {
        var mock = new Mock<ISqlViewDiscoveryService>();
        mock.Setup(v => v.GetViewColumnsAsync("v_Test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(columns.ToList());
        return mock;
    }

    private static SampleDataGenerator Create(Mock<ISqlViewDiscoveryService>? views = null)
        => new(views?.Object ?? new Mock<ISqlViewDiscoveryService>().Object);

    [Fact]
    public async Task GenerateAsync_from_view_maps_column_types()
    {
        var gen = Create(ViewWith(
            new SqlColumnInfo { Name = "RecipientName", DataType = "nvarchar", MaxLength = 200 },
            new SqlColumnInfo { Name = "Qty", DataType = "int" },
            new SqlColumnInfo { Name = "Amount", DataType = "decimal" },
            new SqlColumnInfo { Name = "DueDate", DataType = "datetime" },
            new SqlColumnInfo { Name = "IsActive", DataType = "bit" },
            new SqlColumnInfo { Name = "EmailAddress", DataType = "nvarchar", MaxLength = 100 },
            new SqlColumnInfo { Name = "Id", DataType = "uniqueidentifier" }));

        var result = await gen.GenerateAsync("v_Test", null);

        result["RecipientName"].Should().Be("Jane Doe");
        result["Qty"].Should().Be(4);
        result["Amount"].Should().Be(1250.00m);
        result["DueDate"].Should().Be(DateTime.Today);
        result["IsActive"].Should().Be(true);
        result["EmailAddress"].Should().Be("jane.doe@agency.gov");
        result["Id"].Should().Be(Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301"));
    }

    [Fact]
    public async Task GenerateAsync_respects_max_length()
    {
        var gen = Create(ViewWith(new SqlColumnInfo { Name = "FirstName", DataType = "nvarchar", MaxLength = 6 }));

        var result = await gen.GenerateAsync("v_Test", null);

        result["FirstName"].Should().Be("Jane D");
    }

    [Fact]
    public async Task GenerateAsync_from_tokens_without_view()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "Dear {{model.RecipientName}}, total {{model.Amount}}");

        result["RecipientName"].Should().Be("Jane Doe");
        result["Amount"].Should().Be(1250.00m);
    }

    [Fact]
    public async Task GenerateAsync_detects_loops_with_item_fields()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "{{ for item in model.Items }}{{ item.Name }}: {{ item.Price }}{{ end }}");

        var items = result["Items"].Should().BeAssignableTo<IReadOnlyList<Dictionary<string, object?>>>().Subject;
        items.Should().HaveCount(3);
        items.Should().OnlyContain(i => i.ContainsKey("Name") && i.ContainsKey("Price"));
        items[0]["Name"].Should().Be("Jane Doe");
    }

    [Fact]
    public async Task GenerateAsync_bare_loop_falls_back_to_label_rows()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "{{ for i in model.Rows }}x{{ end }}");

        var rows = result["Rows"].Should().BeAssignableTo<IReadOnlyList<Dictionary<string, object?>>>().Subject;
        rows.Should().HaveCount(3);
        rows[0]["label"].Should().Be("Row 1");
        rows[2]["label"].Should().Be("Row 3");
    }

    [Fact]
    public async Task GenerateAsync_view_wins_over_tokens_for_same_key()
    {
        var gen = Create(ViewWith(new SqlColumnInfo { Name = "Qty", DataType = "int" }));
        var result = await gen.GenerateAsync("v_Test", "{{ model.Qty }} and {{ model.Notes }}");

        result["Qty"].Should().Be(4);
        result["Notes"].Should().Be("Sample Notes");
    }

    [Fact]
    public async Task GenerateAsync_loop_array_overrides_same_key_scalar()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "{{ model.Items }}{{ for item in model.Items }}{{ item.Name }}{{ end }}");

        result["Items"].Should().BeAssignableTo<IReadOnlyList<Dictionary<string, object?>>>();
    }

    [Fact]
    public async Task GenerateAsync_empty_inputs_returns_empty_dictionary()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_invalid_template_body_does_not_throw()
    {
        var gen = Create(ViewWith(new SqlColumnInfo { Name = "Qty", DataType = "int" }));
        var result = await gen.GenerateAsync("v_Test", "{{ 1 + }} broken");

        result["Qty"].Should().Be(4);
    }
}
```

Note the exact shape expectations: `NameAwareString` matches substrings `email`/`phone`/`name`/`address`/`city`/`state`/`zip`/`url` (e.g. `RecipientName` → "Jane Doe", `EmailAddress` → "jane.doe@agency.gov"); ints `qty`/`quantity`/`count` → 4 else 42; decimals `price`/`amount`/`total`/`cost` → 1250.00m, `rate`/`tax` → 0.06m else 99.99m; dates `dob`/`birth` → 1985-03-14 else `DateTime.Today`; loop collections render as `List<Dictionary<string, object?>>` with 3 items.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/ --filter "FullyQualifiedName~SampleDataGeneratorTests"`
Expected: FAIL to compile (`ISampleDataGenerator`/`SampleDataGenerator` don't exist).

- [ ] **Step 3: Write the failing tests for the reference catalog**

Create `tests/TemplateBuilder.Application.Tests/ScribanReferenceCatalogTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using TemplateBuilder.Application.Options;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Tests;

public class ScribanReferenceCatalogTests
{
    public static IEnumerable<object[]> AllEntries()
        => ScribanReferenceCatalog.Entries.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(AllEntries))]
    public async Task Entries_render_without_error_and_match_documented_output(ScribanReferenceEntry entry)
    {
        var engine = new TemplateEngine(new Mock<ITemplateRepository>().Object, new TemplateBuilderOptions());
        var model = new Dictionary<string, object?>
        {
            ["DueDate"] = new DateTime(2026, 8, 18),
            ["UpdatedAt"] = new DateTime(2026, 8, 18, 10, 30, 0),
            ["Amount"] = 1250.00m,
            ["Name"] = "jane doe",
            ["Status"] = "Active",
            ["RichHtml"] = "<b>x</b>",
            ["Items"] = new object[]
            {
                new Dictionary<string, object?> { ["Name"] = "A" },
                new Dictionary<string, object?> { ["Name"] = "B" }
            }
        };

        var result = await engine.RenderBodyAsync(entry.Code, model);

        result.Should().NotContain("error", because: "the entry renders cleanly: " + entry.Label);
        if (entry.Expected is not null)
            result.Should().Be(entry.Expected, because: "the documented output must match the engine for: " + entry.Label);
    }

    [Fact]
    public void Catalog_has_no_duplicate_codes()
    {
        ScribanReferenceCatalog.Entries.Select(e => e.Code)
            .Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 4: Run to verify they fail**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/ --filter "FullyQualifiedName~ScribanReferenceCatalog"`
Expected: FAIL to compile (catalog types don't exist).

- [ ] **Step 5: Implement `SampleDataGenerator`**

Create `src/TemplateBuilder.Application/Services/SampleDataGenerator.cs` (fork decision #2 — origin has no generator; server-side for testability):

```csharp
using TemplateBuilder.Application.DTOs;
using TemplateBuilder.Domain.Interfaces;
using Scriban;
using Scriban.Syntax;

namespace TemplateBuilder.Application.Services;

public interface ISampleDataGenerator
{
    Task<Dictionary<string, object?>> GenerateAsync(string? viewName, string? templateBody, CancellationToken ct = default);
}

public class SampleDataGenerator : ISampleDataGenerator
{
    private const int MaxColumns = 50;
    private const int MaxScalarKeys = 50;
    private const int LoopItems = 3;

    private readonly ISqlViewDiscoveryService _viewDiscovery;

    public SampleDataGenerator(ISqlViewDiscoveryService viewDiscovery)
        => _viewDiscovery = viewDiscovery;

    public async Task<Dictionary<string, object?>> GenerateAsync(string? viewName, string? templateBody, CancellationToken ct = default)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(viewName))
        {
            var columns = await _viewDiscovery.GetViewColumnsAsync(viewName!, ct);
            foreach (var column in columns.Take(MaxColumns))
                result[column.Name] = ValueForColumn(column);
        }

        if (!string.IsNullOrWhiteSpace(templateBody))
            ApplyTemplateTokens(result, templateBody!);

        return result;
    }

    private static void ApplyTemplateTokens(Dictionary<string, object?> result, string body)
    {
        var parsed = Template.Parse(body);
        if (parsed.HasErrors) return;

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var loopFields = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var scalars = new List<(string Key, string Kind)>();

        void Walk(ScriptNode node)
        {
            switch (node)
            {
                case ScriptForStatement forStmt:
                    var alias = (forStmt.Variable as ScriptVariable)?.Name;
                    var collection = MemberKey(forStmt.Iterator);
                    if (alias is not null && collection is not null)
                        aliases[alias] = collection;
                    break;
                case ScriptMemberExpression member:
                    if (member.Target is ScriptVariable target && target.Name == "model" && member.Member.Name.Length > 0)
                        scalars.Add((member.Member.Name, InferKind(member.Member.Name)));
                    else if (member.Target is ScriptVariable local
                             && aliases.TryGetValue(local.Name, out var coll)
                             && member.Member.Name.Length > 0)
                    {
                        if (!loopFields.TryGetValue(coll, out var fields))
                            loopFields[coll] = fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        fields.Add(member.Member.Name);
                    }
                    break;
            }
            foreach (var child in node.Children)
                Walk(child);
        }

        Walk(parsed.Page);

        foreach (var (key, _) in scalars)
            if (!result.ContainsKey(key) && result.Count < MaxScalarKeys)
                result[key] = ValueForKind(key);

        foreach (var (collection, fields) in loopFields)
        {
            var items = new List<Dictionary<string, object?>>();
            for (var i = 1; i <= LoopItems; i++)
            {
                if (fields.Count == 0)
                    items.Add(new Dictionary<string, object?> { ["label"] = $"Row {i}" });
                else
                    items.Add(fields.ToDictionary(f => f, ValueForKind, StringComparer.OrdinalIgnoreCase));
            }
            result[collection] = items;
        }
    }

    private static string? MemberKey(ScriptExpression expr)
        => expr is ScriptMemberExpression member
           && member.Target is ScriptVariable target
           && target.Name == "model"
           && member.Member.Name.Length > 0
            ? member.Member.Name
            : null;

    private static object? ValueForColumn(SqlColumnInfo column)
    {
        var type = column.DataType;
        var len = column.MaxLength;
        string Clip(string value) => len.HasValue && value.Length > len.Value ? value[..len.Value] : value;

        if (type.StartsWith("nvarchar") || type.StartsWith("varchar") || type is "char" or "text")
            return Clip(NameAwareString(column.Name));
        if (type.StartsWith("int") || type is "smallint" or "bigint" or "tinyint")
            return NameAwareInt(column.Name);
        if (type.StartsWith("decimal") || type is "numeric" or "money" or "smallmoney")
            return NameAwareDecimal(column.Name);
        if (type.StartsWith("datetime") || type is "date" or "smalldatetime")
            return NameAwareDate(column.Name);
        if (type == "bit") return true;
        if (type == "uniqueidentifier") return Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
        return Clip($"Sample {column.Name}");
    }

    private static string NameAwareString(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("email")) return "jane.doe@agency.gov";
        if (lower.Contains("phone")) return "(860) 555-0142";
        if (lower.Contains("name")) return "Jane Doe";
        if (lower.Contains("address")) return "450 Columbus Blvd, Hartford, CT 06103";
        if (lower.Contains("city")) return "Hartford";
        if (lower.Contains("state")) return "CT";
        if (lower.Contains("zip")) return "06103";
        if (lower.Contains("url") || lower.Contains("website")) return "https://example.gov";
        return $"Sample {key}";
    }

    private static int NameAwareInt(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("qty") || lower.Contains("quantity") || lower.Contains("count")) return 4;
        if (lower.Contains("year")) return 2026;
        return 42;
    }

    private static decimal NameAwareDecimal(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("price") || lower.Contains("amount") || lower.Contains("total") || lower.Contains("cost"))
            return 1250.00m;
        if (lower.Contains("rate") || lower.Contains("tax")) return 0.06m;
        return 99.99m;
    }

    private static object NameAwareDate(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("dob") || lower.Contains("birth")) return new DateTime(1985, 3, 14);
        return DateTime.Today;
    }

    private static string InferKind(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("email")) return "email";
        if (lower.Contains("phone")) return "phone";
        if (lower.EndsWith("date") || lower.EndsWith("time") || lower.EndsWith("day")) return "date";
        if (lower.Contains("amount") || lower.Contains("price") || lower.Contains("total")
            || lower.Contains("rate") || lower.Contains("cost") || lower.Contains("fee")
            || lower.Contains("balance") || lower.Contains("salary") || lower.Contains("tax")) return "decimal";
        if (lower.Contains("id") || lower.Contains("code") || lower.Contains("qty")
            || lower.Contains("quantity") || lower.Contains("count") || lower.Contains("number")
            || lower.Contains("year") || lower.Contains("age")) return "int";
        if (lower.Contains("active") || lower.Contains("enabled") || lower.Contains("approved")
            || lower.Contains("published") || lower.Contains("deleted") || lower.Contains("archived")
            || lower.Contains("is") || lower.Contains("has")) return "bool";
        return "string";
    }

    private static object? ValueForKind(string key)
    {
        var kind = InferKind(key);
        return kind switch
        {
            "email" or "phone" or "string" => NameAwareString(key),
            "date" => NameAwareDate(key),
            "decimal" => NameAwareDecimal(key),
            "int" => NameAwareInt(key),
            "bool" => true,
            _ => $"Sample {key}"
        };
    }
}
```

(ImplicitUsings is enabled in this project — `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading`, `System.Threading.Tasks` are already global.)

- [ ] **Step 6: Implement `ScribanReferenceCatalog`**

Create `src/TemplateBuilder.Application/Services/ScribanReferenceCatalog.cs` — static content shipped in the package, rendered by the Edit view, validated by the Task-2 tests:

```csharp
namespace TemplateBuilder.Application.Services;

public sealed class ScribanReferenceEntry
{
    public required string Group { get; init; }
    public required string Label { get; init; }
    public required string Code { get; init; }
    public string? Expected { get; init; }
}

public static class ScribanReferenceCatalog
{
    public static IReadOnlyList<ScribanReferenceEntry> Entries { get; } = new[]
    {
        new ScribanReferenceEntry { Group = "Dates", Label = "Format a date", Code = "{{ model.DueDate | date \"%m/%d/%Y\" }}", Expected = "08/18/2026" },
        new ScribanReferenceEntry { Group = "Dates", Label = "Date and time", Code = "{{ model.UpdatedAt | date \"%Y-%m-%d %H:%M\" }}", Expected = "2026-08-18 10:30" },
        new ScribanReferenceEntry { Group = "Dates", Label = "Today's date", Code = "{{ \"now\" | date \"%B %d, %Y\" }}" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Uppercase", Code = "{{ model.Name | upcase }}", Expected = "JANE DOE" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Capitalize", Code = "{{ model.Name | capitalize }}", Expected = "Jane doe" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Escape HTML", Code = "{{ model.RichHtml | html_escape }}", Expected = "&lt;b&gt;x&lt;/b&gt;" },
        new ScribanReferenceEntry { Group = "Strings", Label = "Truncate", Code = "{{ model.Name | truncate 6 }}" },
        new ScribanReferenceEntry { Group = "Numbers", Label = "Round", Code = "{{ model.Amount | math.round }}", Expected = "1250" },
        new ScribanReferenceEntry { Group = "Numbers", Label = "Fixed decimals", Code = "{{ model.Amount | math.format \"0.00\" }}", Expected = "1250.00" },
        new ScribanReferenceEntry { Group = "Loops", Label = "Simple loop", Code = "{{ for item in model.Items }}{{ item.Name }}{{ end }}", Expected = "AB" },
        new ScribanReferenceEntry { Group = "Loops", Label = "Loop with separator", Code = "{{ for item in model.Items }}{{ item.Name }}{{ if !for.last }}, {{ end }}{{ end }}", Expected = "A, B" },
        new ScribanReferenceEntry { Group = "Conditionals", Label = "If / else", Code = "{{ if model.Status == \"Active\" }}Yes{{ else }}No{{ end }}", Expected = "Yes" },
        new ScribanReferenceEntry { Group = "Conditionals", Label = "Value exists", Code = "{{ if model.Status }}Present{{ else }}Missing{{ end }}", Expected = "Present" },
        new ScribanReferenceEntry { Group = "Missing values", Label = "Fallback value", Code = "{{ model.Missing ?? \"—\" }}", Expected = "—" },
        new ScribanReferenceEntry { Group = "Whitespace", Label = "Trim space", Code = "X {{- model.Name -}} Y", Expected = "XJANE DOE Y" }
    };
}
```

- [ ] **Step 7: Run the tests, reconcile any Expected drift**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/`
Expected: PASS. If a deterministic entry's `Expected` mismatches the engine's actual output, the engine is the source of truth — correct `Expected` to the actual output and re-run. (This is the designed gate: the panel ships only engine-verified snippets.)

- [ ] **Step 8: Full suite + commit**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/` and `dotnet test tests/TemplateBuilder.Domain.Tests/`
Expected: Application suite PASS (22 existing + new), Domain 16/16 untouched.

```bash
git add src/TemplateBuilder.Application/Services/SampleDataGenerator.cs \
  src/TemplateBuilder.Application/Services/ScribanReferenceCatalog.cs \
  tests/TemplateBuilder.Application.Tests/SampleDataGeneratorTests.cs \
  tests/TemplateBuilder.Application.Tests/ScribanReferenceCatalogTests.cs
git commit -m "feat: add SampleDataGenerator service and Scriban reference catalog

Fork decision #2 (documented in the v1.1 spec): origin has no sample-data
generation. Server-side generator walks the Scriban 7.2.6 AST via
ScriptNode.Children - schema-first (reuses SqlViewDiscoveryService),
token fallback with name-heuristic types, loop-aware 3-item arrays.
Catalog entries are validated against the engine by xunit before they
ship in the reference panel."
```

---

### Task 3: Editor server — request models, two endpoints, view model, Unity registration

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/SampleDataRequests.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs` (ctor + 2 actions)
- Modify: `src/TemplateBuilder.Editor.Mvc5/Models/TemplateEditorViewModel.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs`

**Interfaces:**
- Consumes: `ISampleDataGenerator` (Task 2), `ITemplateRepository.UpdateTemplateAsync` (exists)
- Produces: routes `POST Templates/Api/SampleData/Generate` (body `{ viewName?, templateBody? }` → `{ sampleData: {...} }`, Newtonsoft-serialized) and `PUT Templates/{id:int}/SampleData` (body `{ sampleData }` → `{ saved: true }`); view-model property `TemplateEditorViewModel.SampleData` (`string?`).

- [ ] **Step 1: Create the request models**

Create `src/TemplateBuilder.Editor.Mvc5/Models/SampleDataRequests.cs`:

```csharp
namespace TemplateBuilder.Editor.Mvc5.Models;

public class GenerateSampleDataRequest
{
    public string? ViewName { get; set; }
    public string? TemplateBody { get; set; }
}

public class SaveSampleDataRequest
{
    public string? SampleData { get; set; }
}
```

- [ ] **Step 2: Add the view-model property**

In `src/TemplateBuilder.Editor.Mvc5/Models/TemplateEditorViewModel.cs`, after `Description`:

```csharp
    public string? SampleData { get; set; }
```

- [ ] **Step 3: Wire the service into the controller**

In `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs`:
- Add `using Newtonsoft.Json;` is already present (line 8).
- Add field + ctor param:
  ```csharp
  private readonly ISampleDataGenerator _sampleDataGenerator;
  ```
  ctor signature becomes:
  ```csharp
  public TemplatesController(ITemplateRepository repository, ISqlViewDiscoveryService viewDiscovery, ITemplateEngine engine, IHtmlSanitizerService sanitizer, ISampleDataGenerator sampleDataGenerator)
  ```
  and add `_sampleDataGenerator = sampleDataGenerator;` in the ctor body.
- Add `SampleData = template.SampleData` to the `Edit` action's view model initializer (after `AvailableViews`).
- Add the two actions (JSON endpoints — `ValidateJsonAntiForgeryToken`, the header-based antiforgery from BLOCKERS #13):

```csharp
    [Route("Templates/Api/SampleData/Generate")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> GenerateSampleData()
    {
        var request = await Request.ReadJsonBodyAsync<GenerateSampleDataRequest>();
        var data = await _sampleDataGenerator.GenerateAsync(request?.ViewName, request?.TemplateBody);
        return Content(JsonConvert.SerializeObject(new { sampleData = data }), "application/json");
    }

    [Route("Templates/{id:int}/SampleData")]
    [HttpPut, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> SaveSampleData(int id)
    {
        var request = await Request.ReadJsonBodyAsync<SaveSampleDataRequest>();
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Template {id} not found."));
        template.SampleData = string.IsNullOrWhiteSpace(request?.SampleData) ? null : request.SampleData;
        await _repository.UpdateTemplateAsync(template);
        return JsonOk(new { saved = true });
    }
```

(Newtonsoft is used for the Generate response because MVC5's stock `JsonResult` serializes `DateTime` as the legacy `/Date(...)/` format; the JS must round-trip dates through the textarea and the Preview endpoint's `JObject.Parse`, so ISO/plain JSON is required. `ReadJsonBodyAsync` already parses the body with Newtonsoft — same behavior as every existing JSON endpoint.)

- [ ] **Step 4: Register the service in Unity**

In `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs`, after the `ITemplateEngine` registration (line 38):

```csharp
        container.RegisterType<ISampleDataGenerator, SampleDataGenerator>(new HierarchicalLifetimeManager());
```

- [ ] **Step 5: Build gate**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
Expected: `0 Error(s)` (2-3 pre-existing nullable warnings allowed).

- [ ] **Step 6: Route gate + commit**

Run:
```bash
grep -n 'Route("Templates/Api/SampleData/Generate")\|Route("Templates/{id:int}/SampleData")' src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs
```
Expected: both routes present.

```bash
git add src/TemplateBuilder.Editor.Mvc5/Models/SampleDataRequests.cs \
  src/TemplateBuilder.Editor.Mvc5/Models/TemplateEditorViewModel.cs \
  src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs \
  src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs
git commit -m "feat: add sample-data Generate/Save endpoints, view-model property, DI registration"
```

---

### Task 4: `Edit.cshtml` — palette search, reference panel, preview toolbar

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml`

**Interfaces:**
- Consumes: `ScribanReferenceCatalog.Entries` (Task 2), `Model.SampleData` (Task 3)
- Produces (DOM contract for the JS in Task 5): `#palette-search` input; `.palette-field` rows keep `data-field`; `#btn-ref-open` button; `#ref-panel` floating panel with `#ref-search`, `#ref-groups` and `.tb-ref-item` buttons carrying `data-code`; preview toolbar with `#btn-gen-menu` (dropdown), `#gen-menu` (`.tb-gen-option` buttons with `data-gen="view|tokens|both"`), `#btn-gen-save`; `#gen-cta` inline call-to-action; `const savedSampleData` inline script variable (declared with `let` so the JS can update it).

- [ ] **Step 1: Add `@using` + preview toolbar + palette search + reference panel + script var**

Edit `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml`:

1. Add `@using TemplateBuilder.Application.Services` after the existing `@using TemplateBuilder.Editor.Mvc5.Models` (line 1).
2. Left panel — add a palette search row above `<div id="field-palette">` (after the view-selector row, line 27):

```html
                <div class="tb-palette-search-row">
                    <input type="search" id="palette-search" class="tb-palette-search" placeholder="Search fields&#8230;" autocomplete="off" aria-label="Search fields">
                </div>
```

3. Palette heading (line 17) — add the reference-panel button to the right:

```html
                <div class="tb-panel-heading" id="palette-heading"><span class="tb-heading-icon">&#8854;</span> FIELD PALETTE
                    <button type="button" id="btn-ref-open" class="tb-ref-open-btn" title="Scriban syntax reference">?</button>
                </div>
```

4. Preview modal (lines 202-212) — replace the single Auto-fill button block with the full toolbar:

```html
                <div class="tb-form-row">
                    <div style="display:flex;align-items:baseline;justify-content:space-between;margin-bottom:4px;">
                        <label for="preview-json" style="margin:0;">Sample Data (JSON)</label>
                        <div class="tb-preview-actions">
                            <button type="button" id="btn-gen-save" class="btn btn-ghost btn-sm"
                                    title="Save this sample data to the template" style="display:none;">&#128190; Save to template</button>
                            <div class="tb-dropdown">
                                <button type="button" id="btn-gen-menu" class="btn btn-ghost btn-sm"
                                        aria-haspopup="menu" aria-expanded="false">&#9889; Generate &#9662;</button>
                                <div class="tb-dropdown-menu" id="gen-menu" role="menu" hidden>
                                    <button type="button" class="tb-gen-option" data-gen="view" role="menuitem">From SQL view</button>
                                    <button type="button" class="tb-gen-option" data-gen="tokens" role="menuitem">From template tokens</button>
                                    <button type="button" class="tb-gen-option" data-gen="both" role="menuitem">Both</button>
                                </div>
                            </div>
                            <button type="button" id="btn-gen-sample" class="btn btn-ghost btn-sm"
                                    title="Auto-fill sample values from {{ model.X }} placeholders in the template">
                                &#9889; Auto-fill from template
                            </button>
                        </div>
                    </div>
                    <textarea id="preview-json" class="tb-code-textarea" rows="8"
                              placeholder='{ "FieldName": "value" }'>{}</textarea>
                </div>
```

5. Scriban reference floating panel — insert after the Special Characters panel (`</div>` closing `#special-chars-panel`, line 357), before the closing `</div>` of `#tb-editor-host` (line 359):

```html
    <!-- Scriban Reference floating panel (non-blocking — no overlay) -->
    <div id="ref-panel" class="tb-ref-panel" hidden role="dialog" aria-label="Scriban Reference">
        <div class="tb-ref-header">
            <span class="tb-ref-title">Scriban Reference</span>
            <button type="button" id="btn-ref-close" class="tb-ref-close" aria-label="Close">&#x2715;</button>
        </div>
        <div class="tb-ref-search-row">
            <input type="search" id="ref-search" class="tb-ref-search" placeholder="Search&#8230;" autocomplete="off">
        </div>
        <div id="ref-groups" class="tb-ref-groups">
            @foreach (var group in ScribanReferenceCatalog.Entries.GroupBy(e => e.Group))
            {
                <div class="tb-ref-group" data-group="@group.Key">
                    <div class="tb-ref-group-label">@group.Key</div>
                    @foreach (var entry in group)
                    {
                        <button type="button" class="tb-ref-item" data-code="@entry.Code" data-label="@entry.Label"
                                title="@entry.Label">
                            <span class="tb-ref-item-label">@entry.Label</span>
                            <code class="tb-ref-item-code">@entry.Code</code>
                        </button>
                    }
                </div>
            }
        </div>
    </div>
```

6. Inline script block (line 361-364) — add the saved-data variable (after `currentVersionNumber`):

```html
    let savedSampleData = @Html.Raw(Json.Encode(Model.SampleData));
```

(`Json.Encode(null)` renders `null`; a string renders as a JS string literal — this is the MVC5-sanctioned pattern. Declared with `let` because the JS updates it on save.)

- [ ] **Step 2: Build gate**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
Expected: `0 Error(s)` — RazorGenerator regenerates `obj/CodeGen` automatically (BLOCKERS #10). Note: if the build complains about `GroupBy` on the catalog in the view, confirm `System.Linq` is imported via the views' `Web.config` namespaces (the `Index.cshtml` uses LINQ-free markup; add `@using System.Linq` at the top of the view if needed).

- [ ] **Step 3: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml
git commit -m "feat: Edit view - palette search, Scriban reference panel, sample-data toolbar"
```

---

### Task 5: `template-editor.js` — generation, persistence, palette enrichment, reference panel

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js`

**Interfaces:**
- Consumes: DOM contract from Task 4; `savedSampleData` (inline `let`); routes from Task 3
- Produces: `generateSampleData(mode)` (async, fills `#preview-json`), save-to-template flow, `_tbUsedFields(html)` → `Set<string>`, palette type badges with `MaxLength`, used-marker rendering, palette search filter, `window._openScribanReference`, cheat-sheet insert via `_editor.insertText`, draft payload gains `sampleData`.

- [ ] **Step 1: Read the current file sections you will touch**

Read these ranges to anchor the edits (line numbers from the v1.0.0 file — confirm before editing):
- 1-26 (state: `_currentColumns`, `_isDirty`)
- 437-519 (palette rendering + field insert)
- 713-761 (preview modal open/render)
- 1760-1824 (special-chars panel pattern — mirror it for the reference panel)
- 2013-2097 (draft save/load, autosave interval)

- [ ] **Step 2: Palette type badges + used markers + search**

Edit `loadViewColumns` (currently lines 437-467). After the existing palette render, add used-marking and keep the row template but make the badge MaxLength-aware and the row used-aware. Replace the `palette.innerHTML = columns.map(...)` block with:

```js
        const used = _tbUsedFields(_editor ? _editor.getContents() : '');
        palette.innerHTML = columns.map(c => `
            <div class="palette-field${used.has(c.name) ? ' palette-field--used' : ''}" draggable="true" data-field="${escapeHtml(c.name)}">
                <span class="palette-field-label">${escapeHtml(c.name)}
                    <span class="palette-field-type">${escapeHtml(c.maxLength ? `${c.dataType}(${c.maxLength})` : c.dataType)}</span>
                </span>
                <span class="palette-field-used-mark" aria-hidden="true">${used.has(c.name) ? '&#10003;' : ''}</span>
                <button type="button" class="palette-insert-btn"
                        aria-label="Insert ${escapeHtml(c.name)} field"
                        data-field="${escapeHtml(c.name)}">Insert</button>
            </div>`).join('');
```

Add the used-field scanner next to `_tbGenerateSampleFromHtml` (after line 502):

```js
function _tbUsedFields(html) {
    const used = new Set();
    const scalarPat = /\{\{-?\s*model\.(\w+)\s*-?\}\}/g;
    let m;
    while ((m = scalarPat.exec(html)) !== null) used.add(m[1]);
    const loopPat = /\{\{-?\s*for\s+\w+\s+in\s+model\.(\w+)\s*-?\}\}/g;
    while ((m = loopPat.exec(html)) !== null) used.add(m[1]);
    return used;
}
```

Add palette search + used-mark refresh (after the palette click handler, line 519):

```js
document.getElementById('palette-search')?.addEventListener('input', (e) => {
    const q = e.target.value.trim().toLowerCase();
    document.querySelectorAll('#field-palette .palette-field').forEach(row => {
        const name = row.dataset.field?.toLowerCase() ?? '';
        row.style.display = !q || name.includes(q) ? '' : 'none';
    });
});

let _usedMarkTimer = null;
function refreshUsedMarks() {
    clearTimeout(_usedMarkTimer);
    _usedMarkTimer = setTimeout(() => {
        if (!_currentColumns.length) return;
        const used = _tbUsedFields(_editor ? _editor.getContents() : '');
        document.querySelectorAll('#field-palette .palette-field').forEach(row => {
            const isUsed = used.has(row.dataset.field);
            row.classList.toggle('palette-field--used', isUsed);
            const mark = row.querySelector('.palette-field-used-mark');
            if (mark) mark.innerHTML = isUsed ? '&#10003;' : '';
        });
    }, 1500);
}
```

Hook the refresh into editor content changes — in the "Initialize word count once SunEditor has rendered its content" section at the end of the file (line 2099+), inside the same `on` handler that updates the word count, add `refreshUsedMarks();`.

- [ ] **Step 3: Sample-data generation + save flow**

Replace `openPreview` (lines 713-721) with:

```js
async function openPreview() {
    const modal = document.getElementById('preview-modal');
    modal.classList.add('open');
    const ta = document.getElementById('preview-json');
    if (!ta.value.trim() || ta.value.trim() === '{}') {
        if (savedSampleData) {
            ta.value = savedSampleData;
        } else {
            const cta = document.getElementById('gen-cta');
            if (!cta) await generateSampleData('both');
        }
    }
    updateSampleSaveBtn();
    trapFocus(modal);
}
```

Add the generation + save functions after `_tbGenerateSampleFromTemplate` (line 507):

```js
async function generateSampleData(mode) {
    const ta = document.getElementById('preview-json');
    if (!ta) return;
    const viewName = document.getElementById('view-selector')?.value || null;
    const body = _editor ? _editor.getContents() : null;
    try {
        const res = await fetch('/Templates/Api/SampleData/Generate', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': _csrf
            },
            body: JSON.stringify({
                viewName: mode === 'tokens' ? null : viewName,
                templateBody: body
            })
        });
        if (!res.ok) throw new Error('Generate failed');
        const { sampleData } = await res.json();
        if (!sampleData || !Object.keys(sampleData).length) {
            showToast('Nothing to generate - add {{ model.X }} placeholders first');
            return;
        }
        ta.value = JSON.stringify(sampleData, null, 2);
        updateSampleSaveBtn();
        showToast('Sample data generated');
    } catch {
        showToast('Could not generate sample data - check the template or view');
    }
}

function updateSampleSaveBtn() {
    const btn = document.getElementById('btn-gen-save');
    if (!btn) return;
    const ta = document.getElementById('preview-json');
    const hasData = !!ta && !!ta.value.trim() && ta.value.trim() !== '{}';
    btn.style.display = templateId && hasData ? '' : 'none';
}

async function saveSampleData() {
    if (!templateId) return;
    const ta = document.getElementById('preview-json');
    try {
        const res = await fetch(`/Templates/${templateId}/SampleData`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': _csrf
            },
            body: JSON.stringify({ sampleData: ta.value.trim() })
        });
        if (!res.ok) throw new Error('Save failed');
        savedSampleData = ta.value.trim();
        showToast('Sample data saved to template');
    } catch {
        showToast('Could not save sample data');
    }
}
```

Wire the toolbar (after the `saveVersion` block, near line 560):

```js
document.getElementById('btn-gen-menu')?.addEventListener('click', () => {
    const menu = document.getElementById('gen-menu');
    const btn = document.getElementById('btn-gen-menu');
    const willOpen = menu.hidden;
    menu.hidden = !willOpen;
    btn?.setAttribute('aria-expanded', String(willOpen));
    if (willOpen) menu.querySelector('button')?.focus();
});

document.addEventListener('click', (e) => {
    const menu = document.getElementById('gen-menu');
    if (!menu || menu.hidden) return;
    if (!e.target.closest('.tb-dropdown')) menu.hidden = true;
});

document.querySelectorAll('.tb-gen-option').forEach(btn => {
    btn.addEventListener('click', () => {
        document.getElementById('gen-menu').hidden = true;
        generateSampleData(btn.dataset.gen);
    });
});

document.getElementById('btn-gen-save')?.addEventListener('click', saveSampleData);
```

Keep the existing `btn-gen-sample` Auto-fill handler working (it sets `#preview-json` from `_tbGenerateSampleFromTemplate`) — after it runs, also call `updateSampleSaveBtn()` (add the call inside that existing handler; verify it sets the textarea and does not reference removed elements).

- [ ] **Step 4: Draft snapshot for sample data**

In `saveDraft` (line 2047-2057), add `sampleData` to the payload:

```js
        localStorage.setItem(DRAFT_KEY, JSON.stringify({
            body: _editor.getContents(),
            sampleData: document.getElementById('preview-json')?.value ?? null,
            timestamp: Date.now(),
            versionNumber: currentVersionNumber
        }));
```

In `loadDraft`'s restore handler (line 2074-2081), after `_editor.setContents(draft.body);`:

```js
            const pv = document.getElementById('preview-json');
            if (pv && draft.sampleData) pv.value = draft.sampleData;
```

- [ ] **Step 5: Scriban reference panel**

Add after the special-chars panel IIFE (after line 1824), mirroring its pattern:

```js
(function () {
    const panel    = document.getElementById('ref-panel');
    const searchEl = document.getElementById('ref-search');
    const groupsEl = document.getElementById('ref-groups');
    if (!panel) return;

    function renderGroups(query) {
        const q = query.trim().toLowerCase();
        document.querySelectorAll('#ref-groups .tb-ref-group').forEach(group => {
            const label = group.querySelector('.tb-ref-group-label')?.textContent.toLowerCase() ?? '';
            let visible = 0;
            group.querySelectorAll('.tb-ref-item').forEach(item => {
                const hay = (item.textContent ?? '').toLowerCase();
                const show = !q || hay.includes(q) || label.includes(q);
                item.style.display = show ? '' : 'none';
                if (show) visible++;
            });
            group.style.display = visible ? '' : 'none';
        });
    }

    function openPanel() {
        searchEl.value = '';
        renderGroups('');
        panel.hidden = false;
        searchEl.focus();
    }

    function closePanel() {
        panel.hidden = true;
        document.querySelector('.sun-editor-editable')?.focus();
    }

    searchEl.addEventListener('input', e => renderGroups(e.target.value));

    groupsEl.addEventListener('click', e => {
        const btn = e.target.closest('.tb-ref-item');
        if (!btn || !_editor) return;
        _editor.insertText(btn.dataset.code + ' ');
        markDirty();
        closePanel();
        showToast(`Inserted: ${btn.dataset.label}`);
    });

    document.getElementById('btn-ref-close')?.addEventListener('click', closePanel);
    document.getElementById('btn-ref-open')?.addEventListener('click', openPanel);

    window._openScribanReference = openPanel;
    window._closeScribanReference = closePanel;
    window._isScribanReferenceOpen = () => !panel.hidden;
})();
```

- [ ] **Step 6: JS syntax gate**

Run: `node --check src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js`
Expected: no output (exit 0).

- [ ] **Step 7: Build gate + commit**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
Expected: `0 Error(s)` (the JS is an embedded resource — rebuild re-embeds it).

```bash
git add src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js
git commit -m "feat: editor JS - server sample-data generation, palette badges/used-marks/search, Scriban reference panel, draft sample-data snapshot"
```

---

### Task 6: `template-editor.css` — styles for the new UI

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css`

**Interfaces:**
- Consumes: DOM contract from Task 4 (classes: `tb-palette-search-row`, `tb-palette-search`, `tb-ref-open-btn`, `tb-preview-actions`, `tb-dropdown`, `tb-dropdown-menu`, `tb-gen-option`, `tb-ref-panel`, `tb-ref-header`, `tb-ref-title`, `tb-ref-close`, `tb-ref-search-row`, `tb-ref-search`, `tb-ref-groups`, `tb-ref-group`, `tb-ref-group-label`, `tb-ref-item`, `tb-ref-item-label`, `tb-ref-item-code`, `palette-field--used`, `palette-field-used-mark`)
- All styles MUST be scoped under `#tb-editor-host` (Bootstrap-3 client collision safety, spec risk #3) or use the standalone floating-panel pattern already used by `#find-replace-panel`/`#special-chars-panel`.

- [ ] **Step 1: Read the existing panel + palette styles to mirror**

Read the CSS for `#tb-editor-host .tb-find-replace` (the floating-panel pattern) and `.palette-field` (the row pattern) to copy their tokens (colors, radii, z-index). The reference panel should visually match the find-replace panel; the palette additions should match the existing row styles.

- [ ] **Step 2: Append the new styles**

Append to `template-editor.css` (inside the `#tb-editor-host` scoping block; for the floating panel use the same un-scoped pattern as `#find-replace-panel` so it can float above the host):

```css
/* ── v1.1: palette search, used markers, preview toolbar, Scriban reference ── */
#tb-editor-host .tb-palette-search-row { padding: 0 0 8px; }
#tb-editor-host .tb-palette-search {
    width: 100%; box-sizing: border-box; padding: 6px 10px;
    border: 1px solid var(--tb-border); border-radius: 6px;
    background: var(--tb-bg-input); color: var(--tb-text); font-size: 13px;
}
#tb-editor-host .tb-ref-open-btn {
    margin-left: auto; width: 22px; height: 22px; border-radius: 50%;
    border: 1px solid var(--tb-border); background: transparent;
    color: var(--tb-text-muted); cursor: pointer; font-weight: 700; line-height: 1;
}
#tb-editor-host .tb-ref-open-btn:hover { color: var(--tb-accent); border-color: var(--tb-accent); }
#tb-editor-host .palette-field--used .palette-field-label { color: var(--tb-text-muted); }
#tb-editor-host .palette-field--used .palette-field-label::after { content: " ✓"; color: var(--tb-accent); }
#tb-editor-host .palette-field-used-mark { display: none; }
#tb-editor-host .palette-field-type { font-size: 11px; opacity: .75; }
#tb-editor-host .tb-preview-actions { display: flex; align-items: center; gap: 6px; }
#tb-editor-host .tb-dropdown { position: relative; }
#tb-editor-host .tb-dropdown-menu {
    position: absolute; right: 0; top: calc(100% + 4px); z-index: 30;
    min-width: 180px; background: var(--tb-bg-panel); border: 1px solid var(--tb-border);
    border-radius: 8px; box-shadow: 0 8px 24px rgba(0,0,0,.18); padding: 4px;
}
#tb-editor-host .tb-dropdown-menu button {
    display: block; width: 100%; text-align: left; padding: 8px 10px;
    border: 0; background: transparent; color: var(--tb-text); cursor: pointer;
    border-radius: 6px; font-size: 13px;
}
#tb-editor-host .tb-dropdown-menu button:hover { background: var(--tb-bg-hover); }

.tb-ref-panel {
    position: fixed; top: 50%; left: 50%; transform: translate(-50%, -50%);
    width: min(560px, 92vw); max-height: 70vh; display: flex; flex-direction: column;
    background: var(--tb-bg-panel); border: 1px solid var(--tb-border); border-radius: 12px;
    box-shadow: 0 16px 48px rgba(0,0,0,.25); z-index: 500; overflow: hidden;
    color: var(--tb-text); font-family: inherit;
}
.tb-ref-header { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; border-bottom: 1px solid var(--tb-border); }
.tb-ref-title { font-weight: 700; font-size: 14px; letter-spacing: .02em; }
.tb-ref-close { border: 0; background: transparent; color: var(--tb-text-muted); font-size: 16px; cursor: pointer; }
.tb-ref-close:hover { color: var(--tb-text); }
.tb-ref-search-row { padding: 10px 16px; }
.tb-ref-search {
    width: 100%; box-sizing: border-box; padding: 7px 10px;
    border: 1px solid var(--tb-border); border-radius: 6px;
    background: var(--tb-bg-input); color: var(--tb-text); font-size: 13px;
}
.tb-ref-groups { overflow-y: auto; padding: 0 16px 16px; }
.tb-ref-group { margin-bottom: 14px; }
.tb-ref-group-label { font-size: 11px; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; color: var(--tb-text-muted); margin-bottom: 6px; }
.tb-ref-item {
    display: flex; flex-direction: column; gap: 2px; width: 100%; text-align: left;
    padding: 8px 10px; margin-bottom: 6px; border: 1px solid var(--tb-border);
    border-radius: 8px; background: var(--tb-bg-input); color: var(--tb-text); cursor: pointer;
}
.tb-ref-item:hover { border-color: var(--tb-accent); }
.tb-ref-item-label { font-size: 13px; font-weight: 600; }
.tb-ref-item-code { font-size: 12px; color: var(--tb-accent); overflow-wrap: anywhere; }
```

(If `--tb-*` variables are not used in the existing file and plain colors are used instead, substitute the file's actual token values — read Step 1 first and match the existing conventions exactly.)

- [ ] **Step 3: Build gate + commit**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
Expected: `0 Error(s)`.

```bash
git add src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css
git commit -m "feat: editor CSS - palette search, used-marker, dropdown, Scriban reference panel styles"
```

---

### Task 7: Version 1.1.0 — pack, inspect, sample-host upgrade, xsp4 regression + new-feature smoke

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj` (`<Version>1.0.0</Version>` → `1.1.0`)
- Modify: `samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj` (4 HintPaths `1.0.0` → `1.1.0`)

**Interfaces:**
- Consumes: everything from Tasks 1-6; the published-in-place `nupkg/TemplateBuilder.Editor.Mvc5.1.0.0.nupkg` (never touched)

- [ ] **Step 1: Bump the version**

In `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj` line 77: `<Version>1.1.0</Version>`.

- [ ] **Step 2: Pack**

Run: `dotnet pack src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Release -o ./nupkg --nologo`
Expected: creates `nupkg/TemplateBuilder.Editor.Mvc5.1.1.0.nupkg`. **Verify `1.0.0.nupkg` still exists untouched** (`ls -la nupkg/`).

- [ ] **Step 3: Inspect the nupkg (the CLAUDE.md rule — always extract and inspect)**

```bash
mkdir -p /tmp/opencode/nupkg-110 && cd /tmp/opencode/nupkg-110 && unzip -o /workspaces/TemplateBuilder.Mvc5/nupkg/TemplateBuilder.Editor.Mvc5.1.1.0.nupkg > /dev/null && find . -type f | sort
```
Expected: `lib/net48/` with exactly 4 DLLs (Editor + Domain + Application + Infrastructure.EF6), `tools/install.ps1`, root `README.md` — **no `.cshtml` anywhere**.
Verify the new types made it into the bundled Application.dll:
```bash
monodis --typedef /tmp/opencode/nupkg-110/lib/net48/TemplateBuilder.Application.dll | grep -i "SampleDataGenerator\|ScribanReferenceCatalog"
```
Expected: `SampleDataGenerator`, `ISampleDataGenerator`, `ScribanReferenceCatalog`, `ScribanReferenceEntry` listed.

- [ ] **Step 4: Upgrade the sample host from the package**

```bash
cd samples/TemplateBuilder.SampleMvc5Host
mono /tmp/opencode/nuget.exe install TemplateBuilder.Editor.Mvc5 -Version 1.1.0 -Source /workspaces/TemplateBuilder.Mvc5/nupkg -ConfigFile /tmp/opencode/nuget-cfg.txt
```
(On a machine without `/tmp/opencode/nuget-cfg.txt`, use `-Source /workspaces/TemplateBuilder.Mvc5/nupkg` only — the config file pins the public nuget.org source for the dependency tree.)
Then update the 4 HintPaths in `TemplateBuilder.SampleMvc5Host.csproj`: `packages\TemplateBuilder.Editor.Mvc5.1.0.0\` → `packages\TemplateBuilder.Editor.Mvc5.1.1.0\` (lines 52, 55, 58, 61).

- [ ] **Step 5: Build the sample host**

Run: `xbuild samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj /p:Configuration=Debug`
Expected: 0 errors (mono xbuild; grep `error :`).

- [ ] **Step 6: xsp4 — regression smoke (v1.0.0 behavior untouched)**

Start xsp4 per the BLOCKERS #11 recipe (`xsp4 --port 8081` against the sample host's bin dir, or the repo's start script). The sample host's Web.config points at `TemplateBuilderMvc5Tests` DB — on first hit, `MigrateDatabaseToLatestVersion` applies `AddSampleDataToTemplates` automatically. Verify with the flow from DASHBOARD.md + Task-14 evidence (cookie + `RequestVerificationToken` header for JSON):
- [ ] `/` → 200 (Home renders)
- [ ] `/Templates` → 200, stats sidebar/badges/duplicate modal markers present
- [ ] `/Templates/Create` → 200, `btn-create-submit`, no version UI
- [ ] `/Templates/{id}/Edit` → 200, 3-panel grid, SunEditor assets
- [ ] `/TemplateBuilderEditor/js/template-editor.js` → 200 `application/javascript`
- [ ] `/TemplateBuilderEditor/css/template-editor.css` → 200 `text/css`
- [ ] `/TemplateBuilderEditor/js/suneditor.min.js` → 200
- [ ] SaveVersion JSON POST → `{"versionId":...,"versionNumber":...}` 200
- [ ] Versions partial, VersionBody, Restore, ToggleActive, Validate, Duplicate, Snippets CRUD — all 200/expected
- [ ] `/Templates/_setup` → 3× PASS

- [ ] **Step 7: xsp4 — new-feature smoke**

Grab the antiforgery token from the Edit page HTML (`__RequestVerificationToken` hidden input), then with `-b /tmp/opencode/tb-cookies.txt -H "RequestVerificationToken: $TOKEN"`:
- [ ] `POST /Templates/Api/SampleData/Generate` body `{"viewName":"v_TestContacts"}` → 200; body contains `"sampleData"` with `FirstName`-style keys (the sample DB's `v_TestContacts` view columns; if the exact view is absent, use any view name from `/Templates/_setup`'s view list)
- [ ] `POST /Templates/Api/SampleData/Generate` body `{"templateBody":"Hi {{model.RecipientName}} — {{model.Amount}}"}` → 200; body contains `"RecipientName":"Jane Doe"` and `"Amount":1250.00`
- [ ] `PUT /Templates/{id}/SampleData` body `{"sampleData":"{\"a\":1}"}` → 200 `{"saved":true}`
- [ ] `GET /Templates/{id}/Edit` → 200 and contains `savedSampleData = "{\"a\":1}"` (proves persistence + reload)
- [ ] Edit page HTML contains `palette-search` and `btn-ref-open` and `ref-groups` with `.tb-ref-item` entries
- [ ] `GET /TemplateBuilderEditor/js/template-editor.js` → 200 and contains `SampleData/Generate`
- [ ] `GET /TemplateBuilderEditor/css/template-editor.css` → 200 and contains `.tb-ref-panel`
- [ ] Create-mode Generate (no template id): `POST /Templates/Api/SampleData/Generate` with only a templateBody → 200 (endpoint is id-independent)

Record all evidence in PROGRESS.md (append a v1.1 section mirroring the existing format).

- [ ] **Step 8: Final full-suite run + commit**

Run: `dotnet build TemplateBuilder.Mvc5.sln --nologo`, `dotnet test tests/TemplateBuilder.Domain.Tests/`, `dotnet test tests/TemplateBuilder.Application.Tests/`, `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/`
Expected: build 0 errors; Domain 16/16; Application 22 + new PASS; EF6 13/13.

```bash
git add src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj \
  samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj \
  PROGRESS.md
git commit -m "feat: v1.1.0 - pack, sample-host upgrade, xsp4 regression + sample-data smoke

Version bump 1.0.0 -> 1.1.0 (the published 1.0.0 nupkg is untouched;
nothing is pushed - publish requires explicit confirmation)."
```

---

## Self-Review

**Spec coverage check:**
- §4.1 schema → Task 1. ✓
- §4.2 strategies 1/2/3 → Task 2 (`ValueForColumn` schema mapping, token walk, loop arrays). ✓
- §4.3 endpoints → Task 3. ✓
- §5.1 preview modal (saved pre-fill, generate dropdown, save button, draft snapshot) → Tasks 4-5. ✓
- §5.2 palette badges/used-marks/search → Tasks 4-6. ✓
- §5.3 reference panel (groups, search, click-to-insert, engine-verified) → Tasks 2/4/5/6. ✓
- §6 testing (Application tests incl. cheat-sheet validation, EF6 round-trip, migration check, pack inspect, xsp4 flow, no-breakage regression) → Tasks 1/2/7. ✓
- §7 risks (MaxLength cap, 50-key cap, debounce, additive-only) → Tasks 2/5/7. ✓
- No-breakage + NuGet safety constraints → Task 7 steps 2/6/8. ✓

**Placeholder scan:** no TBD/TODO; every step has concrete commands/code. The one conditional (view names in the smoke) names the fallback explicitly.

**Type consistency:** `GenerateAsync(string?, string?, CancellationToken) → Dictionary<string, object?>` used identically in Tasks 2/3; `ScribanReferenceEntry.{Group,Label,Code,Expected}` used identically in Tasks 2/4; DOM ids (`palette-search`, `btn-ref-open`, `ref-panel`, `ref-groups`, `btn-gen-menu`, `gen-menu`, `tb-gen-option`, `btn-gen-save`, `savedSampleData`) consistent across Tasks 4/5/6. `UpdateSampleSaveBtn` referenced in Task 5 Step 3 and defined in the same step. ✓

**Known executor hazards called out:** Scriban `Expected` drift handled by the engine-as-truth rule (Task 2 Step 7); CSS variable names to be reconciled with the actual file (Task 6 Step 1); RazorGenerator codegen is automatic; `@using System.Linq` fallback for the view.