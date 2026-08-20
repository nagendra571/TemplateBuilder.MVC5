# Lifecycle & Ops (export/import, health check, bulk ops) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add dev→prod promotion (versioned JSON export/import with stable identity), template health checking (field drift vs live SQL view schema), and bulk operations (activate/deactivate/export/delete) to the TemplateBuilder.Mvc5 editor.

**Architecture:** New Application services (`ITemplatePromotionService`, `ITemplateHealthService`) orchestrate over a new Domain interface (`ITemplatePromotionRepository`, EF6 implementation) and the existing repositories. Three new columns on `Template` (`ExternalKey` unique Guid identity, `SourceView` binding, `SourceViewSnapshot` schema expectations) drive identity and drift detection. Controllers expose JSON endpoints (Newtonsoft, existing antiforgery pattern); views get a bulk toolbar, health badges, a Health page, an Import modal, and editor health integration. All UI stays inside `#tb-editor-host` design tokens (both themes).

**Tech Stack:** .NET Framework 4.8 / C# latest, ASP.NET MVC 5.3, EF6 6.5.1 (System.Data.SqlClient), Scriban 7.2.6 (AST), Newtonsoft.Json 13, xunit + FluentAssertions + NSubstitute, RazorGenerator-precompiled views, Docker SQL Server for EF6 tests, xsp4 sample-host smoke + agent-browser verification.

**Spec:** `docs/superpowers/specs/2026-08-20-lifecycle-ops-design.md`

## Global Constraints

- net48 + `<LangVersion>latest</LangVersion>` everywhere; nullable enabled.
- Domain/Application changes are deliberate fork deviations — mention "fork deviation (lifecycle-ops spec)" in the commit message body for any file under `src/TemplateBuilder.Domain` or `src/TemplateBuilder.Application`.
- JSON responses: `Content(JsonConvert.SerializeObject(obj), "application/json")` (camelCase object shapes); POST endpoints take the `[ValidateJsonAntiForgeryToken]` filter; the editor JS sends the `RequestVerificationToken` header (`_csrf`).
- Views are RazorGenerator-precompiled — never ship `.cshtml`; `dotnet build` regenerates `obj/CodeGen` (BLOCKERS #10). New views must be `<None Include>`ed with `<Generator>RazorGenerator</Generator>` (automatic via the existing `Views/**/*.cshtml` glob).
- EF6 tests run against Docker SQL Server: `Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;` in `[Collection("Database")]` classes. **Stop the xsp4 sample host before running the EF6 suite** (shared DB — "Cannot drop database because it is currently in use").
- Sample-host verification cycle (MEMORY.md): `dotnet pack -c Release -o /tmp/opencode/nupkg-test` → delete + reinstall `TemplateBuilder.Editor.Mvc5.1.1.0` in `samples/TemplateBuilder.SampleMvc5Host/packages` (`mono /tmp/opencode/nuget.exe install ... -Source /tmp/opencode/nupkg-test -OutputDirectory samples/.../packages`) → `xbuild samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj /p:Configuration=Debug` → restart xsp4: kill by PID from `pgrep -f "WebServer.XSP"`, then `XSP_BIN=/tmp/opencode/xsp/src/Mono.WebServer.XSP/bin/Debug; MONO_PATH=$XSP_BIN setsid mono $XSP_BIN/Mono.WebServer.XSP.exe --applications /:/workspaces/TemplateBuilder.Mvc5/samples/TemplateBuilder.SampleMvc5Host --port 8081 --nonstop > /tmp/opencode/xsp4.log 2>&1 < /dev/null &` (first request after boot can 500 once — EF init race; retry).
- Commit steps: repo rule is commit only when the user explicitly asks. If the user has approved committing, use conventional style; otherwise skip commit steps and note uncommitted files at the end.
- Action names in camelCase: export file JSON uses camelCase property names via Newtonsoft `CamelCasePropertyNamesContractResolver`.

---

### Task 1: Domain foundation — ExternalKey, SourceView, SourceViewSnapshot, Imported action, DeleteAsync

**Files:**
- Modify: `src/TemplateBuilder.Domain/Entities/Template.cs`
- Modify: `src/TemplateBuilder.Domain/Entities/AuditActions.cs`
- Modify: `src/TemplateBuilder.Domain/Interfaces/ITemplateRepository.cs`
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Data/TemplateBuilderDbContext.cs` (OnModelCreating)
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Repositories/TemplateRepository.cs`
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Migrations/Configuration.cs` (make public — headless scaffolder needs it)
- Create: `src/TemplateBuilder.Infrastructure.EF6/Migrations/AddLifecycleOps.cs` + `.Designer.cs` + `.resx` (scaffolded, then hand-add the NEWID() backfill)
- Test: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateLifecycleColumnsTests.cs`

**Interfaces:**
- Produces:
  - `Template.ExternalKey` — `Guid`, non-nullable, unique; `Template.SourceView` — `string?`; `Template.SourceViewSnapshot` — `string?`.
  - `AuditActions.Imported = "imported"`.
  - `ITemplateRepository.DeleteAsync(int id, CancellationToken ct = default)` → `Task<bool>` (true = deleted, false = not found).
  - Migration `AddLifecycleOps` (id will differ; grep the designer's `Id` field).

- [ ] **Step 1: Domain entity changes**

`Template.cs` — add three properties next to `SampleData`:

```csharp
public Guid ExternalKey { get; set; } = Guid.NewGuid();
public string? SourceView { get; set; }
public string? SourceViewSnapshot { get; set; }
```

`AuditActions.cs` — add after `SnippetRestored`:

```csharp
public const string Imported = "imported";
```

`ITemplateRepository.cs` — add method:

```csharp
Task<bool> DeleteAsync(int id, CancellationToken ct = default);
```

- [ ] **Step 2: DbContext mappings**

In `OnModelCreating` after the `ReviewComment` mapping line:

```csharp
template.Property(t => t.ExternalKey).IsRequired().HasColumnAnnotation(
    IndexAnnotation.AnnotationName,
    new IndexAnnotation(new IndexAttribute("IX_Templates_ExternalKey") { IsUnique = true }));
template.Property(t => t.SourceView).HasMaxLength(200);
template.Property(t => t.SourceViewSnapshot).IsMaxLength();
```

- [ ] **Step 3: Write the failing EF6 tests**

`TemplateLifecycleColumnsTests.cs` (new file, `[Collection("Database")]`, copy the `CreateContext()` helper pattern from `AuditRepositoryTests.cs`):

```csharp
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
```

- [ ] **Step 4: Run tests — verify they fail (compile errors: `ExternalKey`/`DeleteAsync` missing)**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TemplateLifecycleColumnsTests"`
Expected: FAIL with CS1061/CS0117 (missing members) and `CS0103` etc.

- [ ] **Step 5: Implement CreateAsync/DeleteAsync**

In `TemplateRepository.CreateAsync`, before `_db.Templates.Add(template)`:

```csharp
if (template.ExternalKey == Guid.Empty)
    template.ExternalKey = Guid.NewGuid();
```

Add to `TemplateRepository`:

```csharp
public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
{
    var template = await _db.Templates.FindAsync(ct, id);
    if (template is null) return false;
    var versions = await _db.TemplateVersions.Where(v => v.TemplateId == id).ToListAsync(ct);
    _db.TemplateVersions.RemoveRange(versions);
    _db.Templates.Remove(template);
    await _db.SaveChangesAsync(ct);
    return true;
}
```

Note: `WillCascadeOnDelete(false)` on the versions relationship means EF6 deletes them via the explicit RemoveRange (or orphaned row failure) — the above is the explicit, safe path.

- [ ] **Step 6: Run tests — verify green**

Run the same filtered command. Expected: PASS (4/4). Note: the delete test exercises real SQL; migration must exist for the schema — see Step 7; if the table lacks columns, expect failure here → proceed to the migration.

- [ ] **Step 7: Scaffold the migration (BLOCKERS #8 recipe)**

First make `Migrations/Configuration.cs` public (headless scaffolder requires it):

```csharp
public sealed class Configuration : DbMigrationsConfiguration<Data.TemplateBuilderDbContext>
```

Then scaffold — create `/tmp/opencode/migprobe` console project (net48) referencing the fork's `TemplateBuilder.Infrastructure.EF6.csproj` (copy the fork's `Migrations/` folder contents into the probe so history matches), with this Program.cs:

```csharp
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Migrations.Design;
using System.IO;
using TemplateBuilder.Infrastructure.EF6.Migrations;

var config = new Configuration();
config.TargetDatabase = new DbConnectionInfo(
    "Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;",
    "System.Data.SqlClient");
var scaffolder = new MigrationScaffolder(config);
var result = scaffolder.Scaffold("AddLifecycleOps");
File.WriteAllText("AddLifecycleOps.cs", result.UserCode);
File.WriteAllText("AddLifecycleOps.Designer.cs", result.DesignerCode);
File.WriteAllBytes("AddLifecycleOps.resx", result.Resources);
```

Build with the same package set as the EF6 project (EntityFramework 6.5.1), run under mono. Copy the three files into `src/TemplateBuilder.Infrastructure.EF6/Migrations/`.

- [ ] **Step 8: Add the NEWID() backfill to the generated Up()**

In `AddLifecycleOps.Up()`, after the `AddColumn("dbo.Templates", "SourceViewSnapshot", ...)` calls, insert:

```csharp
Sql("UPDATE dbo.Templates SET ExternalKey = NEWID() WHERE ExternalKey = '00000000-0000-0000-0000-000000000000'");
```

(Guarding on the empty-Guid keeps the migration idempotent for rows created after the column exists but before the migration ran — belt and braces.)

- [ ] **Step 9: Verify migration validity end-to-end**

With xsp4 stopped, run the full EF6 suite:
Run: `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --nologo -v q`
Expected: all pass (existing 30 + new 4). Then verify columns via sqlcmd (docker container `mssql-tb`):
Run: `docker exec mssql-tb /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'TemplateBuilder!2026' -Q "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Templates' AND COLUMN_NAME IN ('ExternalKey','SourceView','SourceViewSnapshot')"`
Expected: 3 rows.

- [ ] **Step 10: Commit (only if the user has approved committing)**

```bash
git add src/TemplateBuilder.Domain src/TemplateBuilder.Infrastructure.EF6 tests/TemplateBuilder.Infrastructure.EF6.Tests
git commit -m "feat: lifecycle-ops data foundation (ExternalKey, SourceView, snapshot, delete)

Fork deviation (lifecycle-ops spec): Template gains ExternalKey (unique),
SourceView and SourceViewSnapshot; ITemplateRepository.DeleteAsync; new
AuditActions.Imported. Migration AddLifecycleOps with NEWID() backfill."
```

---

### Task 2: `ITemplatePromotionRepository` + EF6 implementation

**Files:**
- Create: `src/TemplateBuilder.Domain/Interfaces/ITemplatePromotionRepository.cs`
- Create: `src/TemplateBuilder.Infrastructure.EF6/Repositories/TemplatePromotionRepository.cs`
- Test: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplatePromotionRepositoryTests.cs`

**Interfaces:**
- Produces (exact signatures; later tasks consume these):

```csharp
public interface ITemplatePromotionRepository
{
    Task<Template?> GetByExternalKeyAsync(Guid externalKey, CancellationToken ct = default);
    Task<Template> AddWithVersionsAsync(Template template, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default);
    Task<int[]> AppendVersionsAsync(int templateId, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default); // returns assigned version numbers
    Task<int> GetMaxVersionNumberAsync(int templateId, CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Add_with_versions_preserves_original_version_numbers()
{
    using var ctx = CreateContext();
    var repo = new TemplatePromotionRepository(ctx);
    var t = await repo.AddWithVersionsAsync(
        new Template { Name = "P", TemplateType = "Email", ExternalKey = Guid.NewGuid(), Status = TemplateStatus.Published },
        new List<TemplateVersion>
        {
            new TemplateVersion { VersionNumber = 1, Body = "<p>one</p>" },
            new TemplateVersion { VersionNumber = 2, Body = "<p>two</p>" }
        });
    var history = await repo.GetVersionHistoryAsync(t.Id); // exposed via a small internal ctx query in the test
    history.Select(v => v.VersionNumber).Should().Equal(1, 2);
}

[Fact]
public async Task Append_versions_continue_from_max_plus_one()
{
    using var ctx = CreateContext();
    var repo = new TemplatePromotionRepository(ctx);
    var t = await repo.AddWithVersionsAsync(
        new Template { Name = "P", TemplateType = "Email", ExternalKey = Guid.NewGuid(), Status = TemplateStatus.Published },
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
```

(The test reads history via `ctx.TemplateVersions` directly or through a `TemplateRepository(ctx)` helper instance — both fine.)

- [ ] **Step 2: Run tests — verify fail (type missing)**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TemplatePromotionRepositoryTests"`
Expected: FAIL — CS0246 `TemplatePromotionRepository` not found.

- [ ] **Step 3: Implement**

```csharp
using System.Data.Entity;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class TemplatePromotionRepository : ITemplatePromotionRepository
{
    private readonly TemplateBuilderDbContext _db;
    public TemplatePromotionRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task<Template?> GetByExternalKeyAsync(Guid externalKey, CancellationToken ct = default)
        => await _db.Templates.FirstOrDefaultAsync(t => t.ExternalKey == externalKey, ct);

    public async Task<Template> AddWithVersionsAsync(Template template, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default)
    {
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        if (template.ExternalKey == Guid.Empty) template.ExternalKey = Guid.NewGuid();
        _db.Templates.Add(template);
        foreach (var v in versions)
        {
            v.Template = template;
            v.CreatedAt = v.CreatedAt == default ? DateTime.UtcNow : v.CreatedAt;
            _db.TemplateVersions.Add(v);
        }
        await _db.SaveChangesAsync(ct);
        template.CurrentVersionId = versions.LastOrDefault()?.Id;
        template.CurrentVersion = versions.LastOrDefault();
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task<int[]> AppendVersionsAsync(int templateId, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default)
    {
        var next = await GetMaxVersionNumberAsync(templateId, ct) + 1;
        var assigned = new int[versions.Count];
        for (var i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            v.TemplateId = templateId;
            v.VersionNumber = next + i;
            v.CreatedAt = v.CreatedAt == default ? DateTime.UtcNow : v.CreatedAt;
            _db.TemplateVersions.Add(v);
            assigned[i] = next + i;
        }
        await _db.SaveChangesAsync(ct);
        return assigned;
    }

    public async Task<int> GetMaxVersionNumberAsync(int templateId, CancellationToken ct = default)
        => await _db.TemplateVersions.Where(v => v.TemplateId == templateId).Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0;
}
```

- [ ] **Step 4: Run tests — verify green** (same command; 3/3 expected)

- [ ] **Step 5: Commit (conditional on user approval)**

```bash
git add src/TemplateBuilder.Domain/Interfaces/ITemplatePromotionRepository.cs src/TemplateBuilder.Infrastructure.EF6/Repositories/TemplatePromotionRepository.cs tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplatePromotionRepositoryTests.cs
git commit -m "feat: ITemplatePromotionRepository with EF6 implementation

Fork deviation (lifecycle-ops spec): new Domain interface for promotion
write paths (add-with-versions, append-versions, external-key lookup)."
```

---

### Task 3: Export document builder (Application)

**Files:**
- Create: `src/TemplateBuilder.Application/Services/ITemplatePromotionService.cs` (interface + DTOs in one file)
- Create: `src/TemplateBuilder.Application/Services/TemplatePromotionService.cs`
- Test: `tests/TemplateBuilder.Application.Tests/TemplatePromotionServiceTests.cs`

**Interfaces:**
- Consumes: `ITemplateRepository` (Domain, existing: `GetByIdAsync`, `GetVersionHistoryAsync`), `IAuditService` (existing), `ITemplatePromotionRepository` (Task 2).
- Produces:

```csharp
public class ExporterInfo { public string Name { get; set; } = "TemplateBuilder.Editor.Mvc5"; public string Version { get; set; } = "1.1.0"; }
public class TemplateExportVersion { public int VersionNumber { get; set; } public string Body { get; set; } = ""; public string? ChangeComment { get; set; } public DateTime CreatedAt { get; set; } public string? CreatedBy { get; set; } }
public class TemplateExportTemplate { public Guid ExternalKey { get; set; } public string Name { get; set; } = ""; public string TemplateType { get; set; } = ""; public string? Description { get; set; } public string? SampleData { get; set; } public bool IsActive { get; set; } public string Status { get; set; } = "Draft"; public List<TemplateExportVersion> Versions { get; set; } = new(); }
public class TemplateExportDocument { public int SchemaVersion { get; set; } = 1; public ExporterInfo Exporter { get; set; } = new(); public DateTime ExportedAt { get; set; } public TemplateExportTemplate Template { get; set; } = new(); }
public class TemplateImportEntry { public string? Name { get; set; } public string? Reason { get; set; } public Guid ExternalKey { get; set; } public int VersionsAppended { get; set; } }
public class TemplateImportResult { public List<TemplateImportEntry> Created { get; set; } = new(); public List<TemplateImportEntry> Updated { get; set; } = new(); public List<TemplateImportEntry> Skipped { get; set; } = new(); public List<TemplateImportEntry> Errors { get; set; } = new(); }
public interface ITemplatePromotionService
{
    Task<TemplateExportDocument?> BuildExportAsync(int templateId, CancellationToken ct = default);
    string SerializeExport(TemplateExportDocument document);
    string SanitizeFileName(string name);
    Task<TemplateImportResult> ImportAsync(byte[] fileBytes, string actor, CancellationToken ct = default);
    Task<byte[]> BuildBulkZipAsync(IReadOnlyList<int> templateIds, CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing tests** (NSubstitute pattern from `TemplateWorkflowServiceTests`)

```csharp
[Fact]
public async Task BuildExport_shapes_document_with_ordered_versions()
{
    var repo = Substitute.For<ITemplateRepository>();
    var promo = Substitute.For<ITemplatePromotionRepository>();
    var audit = Substitute.For<IAuditService>();
    var svc = new TemplatePromotionService(repo, promo, audit);
    repo.GetByIdAsync(7).Returns(new Template { Id = 7, ExternalKey = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Invoice", TemplateType = "Email", Status = TemplateStatus.Published, IsActive = true });
    repo.GetVersionHistoryAsync(7).Returns(new List<TemplateVersion>
    {
        new TemplateVersion { VersionNumber = 2, Body = "<p>two</p>", ChangeComment = "c2" },
        new TemplateVersion { VersionNumber = 1, Body = "<p>one</p>" }
    });

    var doc = await svc.BuildExportAsync(7);

    doc.Should().NotBeNull();
    doc!.SchemaVersion.Should().Be(1);
    doc.Exporter.Name.Should().NotBeEmpty();
    doc.Template.Name.Should().Be("Invoice");
    doc.Template.Status.Should().Be("Published");
    doc.Template.Versions.Select(v => v.VersionNumber).Should().Equal(1, 2);
}

[Theory]
[InlineData("Invoice v3", "Invoice_v3")]
[InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
public void SanitizeFileName_strips_invalid_chars(string input, string expected)
{
    var svc = new TemplatePromotionService(Substitute.For<ITemplateRepository>(), Substitute.For<ITemplatePromotionRepository>(), Substitute.For<IAuditService>());
    svc.SanitizeFileName(input).Should().Be(expected);
}

[Fact]
public void SerializeExport_uses_camel_case_json()
{
    var svc = new TemplatePromotionService(Substitute.For<ITemplateRepository>(), Substitute.For<ITemplatePromotionRepository>(), Substitute.For<IAuditService>());
    var json = svc.SerializeExport(new TemplateExportDocument { Template = new TemplateExportTemplate { Name = "X", TemplateType = "Email" } });
    json.Should().Contain("\"schemaVersion\"");
    json.Should().Contain("\"externalKey\"");
}
```

- [ ] **Step 2: Run tests — verify fail**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TemplatePromotionServiceTests"`
Expected: FAIL — `TemplatePromotionService` not found.

- [ ] **Step 3: Implement `BuildExportAsync` + `SerializeExport` + `SanitizeFileName`**

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class TemplatePromotionService : ITemplatePromotionService
{
    private static readonly JsonSerializerSettings CamelJson = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented
    };

    private readonly ITemplateRepository _repository;
    private readonly ITemplatePromotionRepository _promotion;
    private readonly IAuditService _audit;

    public TemplatePromotionService(ITemplateRepository repository, ITemplatePromotionRepository promotion, IAuditService audit)
    {
        _repository = repository;
        _promotion = promotion;
        _audit = audit;
    }

    public async Task<TemplateExportDocument?> BuildExportAsync(int templateId, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return null;
        var history = await _repository.GetVersionHistoryAsync(templateId, ct);
        return new TemplateExportDocument
        {
            SchemaVersion = 1,
            Exporter = new ExporterInfo(),
            ExportedAt = DateTime.UtcNow,
            Template = new TemplateExportTemplate
            {
                ExternalKey = template.ExternalKey,
                Name = template.Name,
                TemplateType = template.TemplateType,
                Description = template.Description,
                SampleData = template.SampleData,
                IsActive = template.IsActive,
                Status = template.Status.ToString(),
                Versions = history.OrderBy(v => v.VersionNumber).Select(v => new TemplateExportVersion
                {
                    VersionNumber = v.VersionNumber,
                    Body = v.Body,
                    ChangeComment = v.ChangeComment,
                    CreatedAt = v.CreatedAt,
                    CreatedBy = v.CreatedBy
                }).ToList()
            }
        };
    }

    public string SerializeExport(TemplateExportDocument document)
        => JsonConvert.SerializeObject(document, CamelJson);

    public string SanitizeFileName(string name)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"[^\w\-\.]", "_").Trim();
        if (cleaned.Length > 80) cleaned = cleaned.Substring(0, 80);
        return string.IsNullOrEmpty(cleaned) ? "template" : cleaned;
    }
}
```

(Import/BulkZip methods are Task 4/5 — leave them `throw new NotImplementedException()` for now, or omit and add in the later tasks; the interface is declared once here.)

- [ ] **Step 4: Run tests — verify green** (same command)

- [ ] **Step 5: Commit (conditional)**

```bash
git add src/TemplateBuilder.Application/Services/TemplatePromotionService.cs src/TemplateBuilder.Application/Services/ITemplatePromotionService.cs tests/TemplateBuilder.Application.Tests/TemplatePromotionServiceTests.cs
git commit -m "feat: export document builder with camelCase JSON serialization

Fork deviation (lifecycle-ops spec): ITemplatePromotionService + DTOs in Application."
```

---

### Task 4: Import orchestration + validation (Application)

**Files:**
- Modify: `src/TemplateBuilder.Application/Services/TemplatePromotionService.cs`
- Test: `tests/TemplateBuilder.Application.Tests/TemplatePromotionImportTests.cs`

**Interfaces:**
- Consumes: `Scriban.Template.Parse` (exact API verified: `Template.Page.Children` is `IEnumerable<ScriptNode>`; `ScriptMemberExpression.Target`/`.Member`; `ScriptVariable.Name`), `ITemplatePromotionRepository` (Task 2), `IAuditService.RecordAsync(...)` (existing signature: `RecordAsync(string entityType, int entityId, string action, string actor, string? beforeState = null, string? afterState = null, string? comment = null, CancellationToken ct = default)` — verify against `src/TemplateBuilder.Application/Services/IAuditService.cs` before coding).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Import_rejects_unknown_schema_version()
{
    var svc = Create(); // helper returning (svc, repo, promo, audit) with NSubstitute
    var json = "{ \"schemaVersion\": 99, \"template\": { \"name\": \"X\" } }";
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(json), "bob");
    result.Errors.Should().ContainSingle(e => e.Reason.Contains("schemaVersion"));
    result.Created.Should().BeEmpty();
}

[Fact]
public async Task Import_rejects_scriban_invalid_body()
{
    var svc = Create();
    var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = Guid.NewGuid(), Name = "X", TemplateType = "Email", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "{{ model.Name" } } } };
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
    result.Errors.Should().ContainSingle(e => e.Reason.Contains("version 1"));
}

[Theory]
[InlineData("Draft", "Draft")]
[InlineData("Published", "Published")]
[InlineData("Review", "Draft")]
[InlineData("Approved", "Draft")]
public void CollapseStatus_maps_correctly(string exported, string expected)
{
    TemplatePromotionService.CollapseStatus(exported).Should().Be(expected);
}

[Fact]
public async Task Import_skips_locked_target()
{
    var (svc, repo, promo, audit) = Create();
    var key = Guid.NewGuid();
    promo.GetByExternalKeyAsync(key).Returns(new Template { Id = 3, Name = "Old", Status = TemplateStatus.Review });
    var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", Status = "Published", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>ok</p>" } } };
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
    result.Skipped.Should().ContainSingle(s => s.Reason.Contains("Review"));
    await promo.DidNotReceive().AppendVersionsAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Import_creates_new_template_and_audits()
{
    var (svc, repo, promo, audit) = Create();
    var key = Guid.NewGuid();
    promo.GetByExternalKeyAsync(key).Returns((Template?)null);
    Template captured = null!;
    await promo.AddWithVersionsAsync(Arg.Do<Template>(t => captured = t), Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>());
    var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", Status = "Published", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>ok</p>" } } };
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
    result.Created.Should().ContainSingle(c => c.Name == "X");
    captured.Status.Should().Be(TemplateStatus.Published);
    captured.ExternalKey.Should().Be(key);
}
```

- [ ] **Step 2: Run tests — verify fail** (`ImportAsync` not implemented / `CollapseStatus` missing)

- [ ] **Step 3: Implement**

```csharp
public static string CollapseStatus(string exported)
    => exported == "Published" ? "Published" : "Draft"; // Review/Approved collapse to Draft; Draft stays Draft

public async Task<TemplateImportResult> ImportAsync(byte[] fileBytes, string actor, CancellationToken ct = default)
{
    var result = new TemplateImportResult();
    TemplateExportDocument? doc;
    try
    {
        doc = JsonConvert.DeserializeObject<TemplateExportDocument>(Encoding.UTF8.GetString(fileBytes), CamelJson);
    }
    catch (Exception)
    {
        result.Errors.Add(new TemplateImportEntry { Reason = "Not a valid template export file (JSON parse failed)." });
        return result;
    }
    if (doc is null || doc.SchemaVersion > 1)
    {
        result.Errors.Add(new TemplateImportEntry { Reason = $"schemaVersion {doc?.SchemaVersion} not supported." });
        return result;
    }
    if (doc.Template is null || string.IsNullOrWhiteSpace(doc.Template.Name) || string.IsNullOrWhiteSpace(doc.Template.TemplateType) || doc.Template.Versions.Count == 0)
    {
        result.Errors.Add(new TemplateImportEntry { Name = doc?.Template?.Name, Reason = "File is missing template name/type or has no versions." });
        return result;
    }
    for (var i = 0; i < doc.Template.Versions.Count; i++)
    {
        var parsed = Scriban.Template.Parse(doc.Template.Versions[i].Body ?? string.Empty);
        if (parsed.HasErrors)
        {
            result.Errors.Add(new TemplateImportEntry { Name = doc.Template.Name, Reason = $"Version {doc.Template.Versions[i].VersionNumber} does not parse." });
            return result;
        }
    }

    var key = doc.Template.ExternalKey;
    var existing = key != Guid.Empty ? await _promotion.GetByExternalKeyAsync(key, ct) : null;

    if (existing is null)
    {
        var template = new Template
        {
            ExternalKey = key == Guid.Empty ? Guid.NewGuid() : key,
            Name = doc.Template.Name.Trim(),
            TemplateType = doc.Template.TemplateType,
            Description = doc.Template.Description,
            SampleData = doc.Template.SampleData,
            IsActive = doc.Template.IsActive,
            Status = Enum.TryParse<TemplateStatus>(CollapseStatus(doc.Template.Status), out var st) ? st : TemplateStatus.Draft
        };
        var versions = doc.Template.Versions.Select(v => new TemplateVersion
        {
            VersionNumber = v.VersionNumber,
            Body = v.Body,
            ChangeComment = v.ChangeComment,
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy
        }).ToList();
        var created = await _promotion.AddWithVersionsAsync(template, versions, ct);
        await _audit.RecordAsync("Template", created.Id, AuditActions.Imported, actor,
            afterState: JsonConvert.SerializeObject(new { file = doc.Template.Name, externalKey = created.ExternalKey, versionsImported = versions.Count }), ct: ct);
        result.Created.Add(new TemplateImportEntry { Name = created.Name, ExternalKey = created.ExternalKey });
        return result;
    }

    if (existing.Status == TemplateStatus.Review || existing.Status == TemplateStatus.Approved)
    {
        result.Skipped.Add(new TemplateImportEntry { Name = existing.Name, Reason = $"Target is {existing.Status} (locked)" });
        return result;
    }

    existing.Name = doc.Template.Name.Trim();
    existing.TemplateType = doc.Template.TemplateType;
    existing.Description = doc.Template.Description;
    existing.SampleData = doc.Template.SampleData;
    existing.IsActive = doc.Template.IsActive;
    existing.Status = Enum.TryParse<TemplateStatus>(CollapseStatus(doc.Template.Status), out var st2) ? st2 : TemplateStatus.Draft;
    existing.UpdatedAt = DateTime.UtcNow;

    var importedVersions = doc.Template.Versions.Select(v => new TemplateVersion
    {
        Body = v.Body,
        ChangeComment = v.ChangeComment is null ? $"Imported from {doc.Exporter.Name} ({doc.ExportedAt:u})" : $"{v.ChangeComment} — imported {doc.ExportedAt:u}",
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy
    }).ToList();

    // NOTE: EF6 single-context rules — the repository's AppendVersionsAsync also persists the
    // metadata change above within the same SaveChanges; see implementation note in Step 3b.
    var assigned = await _promotion.AppendVersionsAsync(existing.Id, importedVersions, ct);
    await _audit.RecordAsync("Template", existing.Id, AuditActions.Imported, actor,
        afterState: JsonConvert.SerializeObject(new { file = doc.Template.Name, externalKey = existing.ExternalKey, versionsImported = assigned.Length }), ct: ct);
    result.Updated.Add(new TemplateImportEntry { Name = existing.Name, ExternalKey = existing.ExternalKey, VersionsAppended = assigned.Length });
    return result;
}
```

**Step 3b note:** the metadata update (`existing.Name = ...`) happens outside the repository. To persist it, extend `ITemplatePromotionRepository.AppendVersionsAsync` — rename it `UpdateFromImportAsync` and move the metadata mutation inside the repository so a single `SaveChangesAsync` commits metadata + versions atomically:

```csharp
Task<int[]> UpdateFromImportAsync(Template template, IReadOnlyList<TemplateVersion> versions, CancellationToken ct = default);
```

Keep the signature used in Task 2 (`AppendVersionsAsync`) and add this wrapper inside the repository that sets `UpdatedAt` and saves the graph. The service calls `UpdateFromImportAsync`; Task 2's tests for `AppendVersionsAsync` remain valid. (Adjust the interface in Task 2's file — both methods exist.)

- [ ] **Step 4: Run tests — verify green** (ImportAsync + collapse + skip + create paths)

- [ ] **Step 5: Commit (conditional)**

```bash
git add src/TemplateBuilder.Application/Services/TemplatePromotionService.cs src/TemplateBuilder.Application/Services/ITemplatePromotionService.cs src/TemplateBuilder.Domain/Interfaces/ITemplatePromotionRepository.cs src/TemplateBuilder.Infrastructure.EF6/Repositories/TemplatePromotionRepository.cs tests/TemplateBuilder.Application.Tests/TemplatePromotionImportTests.cs
git commit -m "feat: import orchestration (validation, status collapse, locked-skip, audit)"
```

---

### Task 5: Bulk ZIP packaging (Application)

**Files:**
- Modify: `src/TemplateBuilder.Application/Services/TemplatePromotionService.cs`
- Test: `tests/TemplateBuilder.Application.Tests/TemplatePromotionBulkZipTests.cs`

**Interfaces:**
- Consumes: `BuildExportAsync` + `SerializeExport` (Tasks 3–4), `System.IO.Compression.ZipArchive` (net48 built-in).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Bulk_zip_contains_per_template_files_and_summary_manifest()
{
    var repo = Substitute.For<ITemplateRepository>();
    var promo = Substitute.For<ITemplatePromotionRepository>();
    var audit = Substitute.For<IAuditService>();
    var svc = new TemplatePromotionService(repo, promo, audit);

    repo.GetByIdAsync(1).Returns(new Template { Id = 1, ExternalKey = Guid.NewGuid(), Name = "Invoice v3", TemplateType = "Email", Status = TemplateStatus.Published });
    repo.GetVersionHistoryAsync(1).Returns(new List<TemplateVersion> { new TemplateVersion { VersionNumber = 1, Body = "<p>one</p>" } });
    repo.GetByIdAsync(2).Returns((Template?)null);

    var zip = await svc.BuildBulkZipAsync(new[] { 1, 2 });
    zip.Should().NotBeEmpty();

    using var ms = new MemoryStream(zip);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    archive.Entries.Select(e => e.Name).Should().Contain("Invoice_v3.template.json");
    archive.Entries.Select(e => e.Name).Should().Contain("_summary.json");
    var summaryEntry = archive.GetEntry("_summary.json");
    using var sr = new StreamReader(summaryEntry!.Open());
    var summary = sr.ReadToEnd();
    summary.Should().Contain("\"Invoice v3\"");
    summary.Should().Contain("not found");
}
```

- [ ] **Step 2: Run — verify fail** (NotImplementedException)

- [ ] **Step 3: Implement**

```csharp
public async Task<byte[]> BuildBulkZipAsync(IReadOnlyList<int> templateIds, CancellationToken ct = default)
{
    using var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        var summary = new List<object>();
        foreach (var id in templateIds)
        {
            var doc = await BuildExportAsync(id, ct);
            if (doc is null)
            {
                summary.Add(new { id, name = (string?)null, status = "not found" });
                continue;
            }
            var entry = archive.CreateEntry($"{SanitizeFileName(doc.Template.Name)}.template.json");
            using (var writer = new StreamWriter(entry.Open()))
                await writer.WriteAsync(SerializeExport(doc));
            summary.Add(new { id, name = doc.Template.Name, status = "exported" });
        }
        var manifest = archive.CreateEntry("_summary.json");
        using (var writer = new StreamWriter(manifest.Open()))
            await writer.WriteAsync(JsonConvert.SerializeObject(new { schemaVersion = 1, exportedAt = DateTime.UtcNow, files = summary }, CamelJson));
    }
    return ms.ToArray();
}
```

- [ ] **Step 4: Run — verify green**

- [ ] **Step 5: Commit (conditional)**

```bash
git add src/TemplateBuilder.Application/Services/TemplatePromotionService.cs tests/TemplateBuilder.Application.Tests/TemplatePromotionBulkZipTests.cs
git commit -m "feat: bulk export ZIP packaging with summary manifest"
```

---

### Task 6: Health check engine (Application)

**Files:**
- Create: `src/TemplateBuilder.Application/Services/ITemplateHealthService.cs` (interface + DTOs)
- Create: `src/TemplateBuilder.Application/Services/TemplateHealthService.cs`
- Test: `tests/TemplateBuilder.Application.Tests/TemplateHealthServiceTests.cs`

**Interfaces:**
- Consumes: `ITemplateRepository` (existing), `ISqlViewDiscoveryService.GetViewColumnsAsync` (existing, returns `IReadOnlyList<SqlColumnInfo>` with `Name/DataType/MaxLength/IsNullable`), Scriban AST (exact member names verified: `Template.Page.Children`; `ScriptNode.Children`; `ScriptMemberExpression.Target/.Member`; `ScriptVariable.Name`).
- Produces:

```csharp
public enum HealthSeverity { Info = 0, Warning = 1, Critical = 2 }
public class HealthFinding { public HealthSeverity Severity { get; set; } public string Code { get; set; } = ""; public string Message { get; set; } = ""; }
public class TemplateHealthReport
{
    public int TemplateId { get; set; }
    public string? SourceView { get; set; }
    public bool ViewMissing { get; set; }
    public List<string> Tokens { get; set; } = new();
    public List<HealthFinding> Findings { get; set; } = new();
    public DateTime? SnapshotTakenAt { get; set; }
    public HealthSeverity Worst => Findings.Count == 0 ? HealthSeverity.Info : Findings.Max(f => f.Severity);
}
public interface ITemplateHealthService
{
    Task<TemplateHealthReport> CheckAsync(int templateId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ExtractModelPathsAsync(string body, CancellationToken ct = default); // static-able; exposed for tests
    Task<string> BuildSnapshotJsonAsync(string viewName, CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing tests**

Token extraction fixture tests (the critical correctness cases):

```csharp
[Fact]
public async Task Extract_handles_nested_loops_conditionals_and_ignores_literals()
{
    var svc = new TemplateHealthService(Substitute.For<ITemplateRepository>(), Substitute.For<ISqlViewDiscoveryService>());
    const string body = @"
<p>{{ model.FirstName }} {{ model.User.Name }}</p>
{% for item in model.Items %}{{ item.Qty }}{% endfor %}
{% if model.HasDiscount %}yes{% endif %}
{{ ""literal model.Nope"" }} {{ 'model.Single' }}";
    var paths = await svc.ExtractModelPathsAsync(body);
    paths.Should().BeEquivalentTo("FirstName", "User.Name", "Items", "HasDiscount");
}

[Fact]
public async Task Check_reports_missing_view_and_missing_column()
{
    var repo = Substitute.For<ITemplateRepository>();
    var discovery = Substitute.For<ISqlViewDiscoveryService>();
    repo.GetByIdAsync(1).Returns(new Template
    {
        Id = 1, Name = "T", SourceView = "v_Gone", SourceViewSnapshot = JsonConvert.SerializeObject(new List<SqlColumnInfo> { new SqlColumnInfo { Name = "FirstName", DataType = "nvarchar", MaxLength = 100, IsNullable = false } }),
        CurrentVersion = new TemplateVersion { Body = "<p>{{ model.FirstName }}</p><p>{{ model.Nope }}</p>" }
    });
    discovery.GetViewColumnsAsync("v_Gone").Returns(new List<SqlColumnInfo>());

    var svc = new TemplateHealthService(repo, discovery);
    var report = await svc.CheckAsync(1);

    report.ViewMissing.Should().BeTrue();
    report.Findings.Should().Contain(f => f.Code == "view_missing" && f.Severity == HealthSeverity.Critical);
}

[Fact]
public async Task Check_reports_type_and_length_drift_from_snapshot()
{
    // SourceView = "v_Cust"; snapshot says nvarchar(100) nullable; live says nvarchar(500) NOT NULL
    var repo = Substitute.For<ITemplateRepository>();
    var discovery = Substitute.For<ISqlViewDiscoveryService>();
    repo.GetByIdAsync(1).Returns(new Template
    {
        Id = 1, Name = "T", SourceView = "v_Cust",
        SourceViewSnapshot = JsonConvert.SerializeObject(new List<SqlColumnInfo> { new SqlColumnInfo { Name = "CustomerName", DataType = "nvarchar", MaxLength = 100, IsNullable = true } }),
        CurrentVersion = new TemplateVersion { Body = "<p>{{ model.CustomerName }}</p>" }
    });
    discovery.GetViewColumnsAsync("v_Cust").Returns(new List<SqlColumnInfo> { new SqlColumnInfo { Name = "CustomerName", DataType = "nvarchar", MaxLength = 500, IsNullable = false } });

    var svc = new TemplateHealthService(repo, discovery);
    var report = await svc.CheckAsync(1);

    report.Findings.Should().Contain(f => f.Code == "column_length_changed" && f.Severity == HealthSeverity.Warning);
    report.Findings.Should().Contain(f => f.Code == "column_nullability_changed" && f.Severity == HealthSeverity.Warning);
}

[Fact]
public async Task Check_reports_type_change_without_redundant_length_finding()
{
    // snapshot: nvarchar(100); live: int → type changed; length finding suppressed
    var repo = Substitute.For<ITemplateRepository>();
    var discovery = Substitute.For<ISqlViewDiscoveryService>();
    repo.GetByIdAsync(1).Returns(new Template
    {
        Id = 1, Name = "T", SourceView = "v_C",
        SourceViewSnapshot = JsonConvert.SerializeObject(new List<SqlColumnInfo> { new SqlColumnInfo { Name = "Amount", DataType = "nvarchar", MaxLength = 100, IsNullable = true } }),
        CurrentVersion = new TemplateVersion { Body = "<p>{{ model.Amount }}</p>" }
    });
    discovery.GetViewColumnsAsync("v_C").Returns(new List<SqlColumnInfo> { new SqlColumnInfo { Name = "Amount", DataType = "int", MaxLength = null, IsNullable = false } });

    var report = await new TemplateHealthService(repo, discovery).CheckAsync(1);

    report.Findings.Should().Contain(f => f.Code == "column_type_changed");
    report.Findings.Should().NotContain(f => f.Code == "column_length_changed");
}

[Fact]
public async Task Check_unbound_template_with_tokens_reports_warning()
{
    var repo = Substitute.For<ITemplateRepository>();
    var discovery = Substitute.For<ISqlViewDiscoveryService>();
    repo.GetByIdAsync(1).Returns(new Template { Id = 1, Name = "T", SourceView = null, CurrentVersion = new TemplateVersion { Body = "<p>{{ model.FirstName }}</p>" } });
    var report = await new TemplateHealthService(repo, discovery).CheckAsync(1);
    report.Findings.Should().Contain(f => f.Code == "unbound_tokens" && f.Severity == HealthSeverity.Warning);
}
```

- [ ] **Step 2: Run — verify fail** (`TemplateHealthService` missing)

- [ ] **Step 3: Implement the AST walker** (exact Scriban API per the verified probe)

```csharp
using Scriban;
using Scriban.Syntax;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class TemplateHealthService : ITemplateHealthService
{
    private readonly ITemplateRepository _repository;
    private readonly ISqlViewDiscoveryService _discovery;

    public TemplateHealthService(ITemplateRepository repository, ISqlViewDiscoveryService discovery)
    {
        _repository = repository;
        _discovery = discovery;
    }

    public Task<IReadOnlyList<string>> ExtractModelPathsAsync(string body, CancellationToken ct = default)
    {
        var parsed = Template.Parse(body);
        if (parsed.HasErrors) return Task.FromResult<IReadOnlyList<string>>(new List<string>());
        var members = new List<ScriptMemberExpression>();
        Collect(parsed.Page.Children, members);
        var leaves = members.Where(m => !members.Any(other => other != m && IsInTargetChain(other.Target, m))).ToList();
        var paths = leaves.Select(ToPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult<IReadOnlyList<string>>(paths);
    }

    private static void Collect(IEnumerable<ScriptNode> nodes, List<ScriptMemberExpression> acc)
    {
        foreach (var node in nodes)
        {
            if (node is ScriptMemberExpression m && IsRootedAtModel(m)) acc.Add(m);
            if (node.ChildrenCount > 0) Collect(node.Children, acc);
        }
    }

    private static bool IsRootedAtModel(ScriptMemberExpression m)
        => m.Target is ScriptVariable sv && sv.Name == "model"
           || (m.Target is ScriptMemberExpression inner && IsRootedAtModel(inner));

    private static bool IsInTargetChain(ScriptExpression target, ScriptMemberExpression needle)
        => ReferenceEquals(target, needle) || (target is ScriptMemberExpression inner && IsInTargetChain(inner.Target, needle));

    private static string ToPath(ScriptMemberExpression m)
        => m.Target is ScriptVariable v ? m.Member.Name : ToPath((ScriptMemberExpression)m.Target) + "." + m.Member.Name;

    public async Task<string> BuildSnapshotJsonAsync(string viewName, CancellationToken ct = default)
    {
        var columns = await _discovery.GetViewColumnsAsync(viewName, ct);
        return JsonConvert.SerializeObject(new { takenAt = DateTime.UtcNow, columns });
    }

    public async Task<TemplateHealthReport> CheckAsync(int templateId, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        var report = new TemplateHealthReport { TemplateId = templateId };
        if (template is null)
        {
            report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Critical, Code = "template_missing", Message = $"Template {templateId} does not exist." });
            return report;
        }
        report.SourceView = template.SourceView;
        var body = template.CurrentVersion?.Body ?? string.Empty;
        report.Tokens = (await ExtractModelPathsAsync(body, ct)).ToList();

        SnapshotData? snapshot = null;
        if (!string.IsNullOrWhiteSpace(template.SourceViewSnapshot))
            try { snapshot = JsonConvert.DeserializeObject<SnapshotData>(template.SourceViewSnapshot); }
            catch { snapshot = null; }
        report.SnapshotTakenAt = snapshot?.TakenAt;

        if (string.IsNullOrWhiteSpace(template.SourceView))
        {
            if (report.Tokens.Count > 0)
                report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "unbound_tokens", Message = "Template references model fields but is not bound to a SQL view." });
            else
                report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Info, Code = "unbound_no_tokens", Message = "Template is not bound to a SQL view (not schema-checkable)." });
            return report;
        }

        IReadOnlyList<SqlColumnInfo> live;
        try { live = await _discovery.GetViewColumnsAsync(template.SourceView, ct); }
        catch
        {
            report.ViewMissing = true;
            report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Critical, Code = "view_missing", Message = $"View '{template.SourceView}' no longer exists." });
            return report;
        }

        var liveByName = live.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var token in report.Tokens)
        {
            if (!liveByName.ContainsKey(token))
                report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Critical, Code = "column_missing", Message = $"Column '{token}' is missing from view '{template.SourceView}'." });
        }

        if (snapshot is { Columns: not null })
        {
            foreach (var expected in snapshot.Columns)
            {
                if (!liveByName.TryGetValue(expected.Name, out var actual)) continue; // covered by column_missing when referenced
                var typeChanged = !string.Equals(expected.DataType, actual.DataType, StringComparison.OrdinalIgnoreCase);
                var lengthChanged = expected.MaxLength != actual.MaxLength;
                var nullabilityChanged = expected.IsNullable != actual.IsNullable;
                if (typeChanged)
                    report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "column_type_changed", Message = $"Column '{expected.Name}' type changed {expected.DataType} → {actual.DataType}." });
                else if (lengthChanged)
                    report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "column_length_changed", Message = $"Column '{expected.Name}' length changed {expected.MaxLength?.ToString() ?? "—"} → {actual.MaxLength?.ToString() ?? "—"}." });
                if (nullabilityChanged)
                    report.Findings.Add(new HealthFinding { Severity = HealthSeverity.Warning, Code = "column_nullability_changed", Message = $"Column '{expected.Name}' nullability changed." });
            }
        }
        return report;
    }

    private class SnapshotData
    {
        public DateTime TakenAt { get; set; }
        public List<SqlColumnInfo>? Columns { get; set; }
    }
}
```

(Note: `ISqlViewDiscoveryService` lives in `TemplateBuilder.Application.Services`; the service can also call `GetViewNamesAsync` when it needs existence checks — the missing-view path above uses the column call's failure, which is the live-signal path; wrap `GetViewNamesAsync` usage in the controller/page layer if a cheaper existence check is wanted.)

- [ ] **Step 4: Run — verify green** (all 5 tests)

- [ ] **Step 5: Commit (conditional)**

```bash
git add src/TemplateBuilder.Application/Services/ITemplateHealthService.cs src/TemplateBuilder.Application/Services/TemplateHealthService.cs tests/TemplateBuilder.Application.Tests/TemplateHealthServiceTests.cs
git commit -m "feat: template health check engine (Scriban AST token extraction + snapshot drift)"
```

---

### Task 7: Controllers, endpoints, Unity registration (Editor.Mvc5)

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Controllers/HealthController.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/HealthIndexViewModel.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs`

**Interfaces:**
- Consumes: `ITemplatePromotionService` (Tasks 3–5), `ITemplateHealthService` (Task 6), `ITemplateRepository`, `IAuditService` (existing), `TemplateBuilderControllerBase` (existing base — provides `CurrentActor`, JSON antiforgery exclusion).
- Produces routes (see steps):

| Route | Method | Returns |
|---|---|---|
| `Templates/Export/{id:int}` | GET | file attachment |
| `Templates/Import` | POST (multipart) | `TemplateImportResult` JSON |
| `Templates/BulkActivate` / `BulkDeactivate` / `BulkDelete` | POST (JSON) | `{ succeeded, failed }` JSON |
| `Templates/BulkExport` | POST (JSON) | ZIP attachment |
| `Templates/{id:int}/Health` | GET | `TemplateHealthReport` JSON (camelCase) |
| `Health` | GET | view |
| `Health/Summaries` | GET | `[{ templateId, severity, findingCount }]` JSON |

- [ ] **Step 1: Controller endpoints**

In `TemplatesController`, add (constructor already receives `ITemplateRepository repository, ... IAuditService audit, ...` — extend the constructor with `ITemplatePromotionService promotion, ITemplateHealthService health`; mirror the existing `[ValidateJsonAntiForgeryToken]` usage on JSON POSTs):

```csharp
[Route("Templates/Export/{id:int}")]
[HttpGet]
public async Task<ActionResult> ExportTemplate(int id)
{
    var doc = await _promotion.BuildExportAsync(id);
    if (doc is null) return HttpNotFound();
    var bytes = Encoding.UTF8.GetBytes(_promotion.SerializeExport(doc));
    Response.AddHeader("Content-Disposition", $"attachment; filename={_promotion.SanitizeFileName(doc.Template.Name)}.template.json");
    return File(bytes, "application/json");
}

[Route("Templates/Import")]
[HttpPost]
public async Task<ActionResult> Import(HttpPostedFileBase file)
{
    if (file is null || file.ContentLength == 0)
        return Content(JsonConvert.SerializeObject(new { errors = new[] { new { reason = "No file selected." } } }), "application/json");
    using var ms = new MemoryStream();
    await file.InputStream.CopyToAsync(ms);
    var result = await _promotion.ImportAsync(ms.ToArray(), CurrentActor);
    return Content(JsonConvert.SerializeObject(result, new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    }), "application/json");
}

[Route("Templates/BulkActivate")]
[HttpPost]
[ValidateJsonAntiForgeryToken]
public async Task<ActionResult> BulkActivate(BulkIdsRequest request)
    => await BulkToggle(request.Ids, active: true);

[Route("Templates/BulkDeactivate")]
[HttpPost]
[ValidateJsonAntiForgeryToken]
public async Task<ActionResult> BulkDeactivate(BulkIdsRequest request)
    => await BulkToggle(request.Ids, active: false);

private async Task<ActionResult> BulkToggle(IReadOnlyList<int> ids, bool active)
{
    var succeeded = new List<int>();
    var failed = new List<object>();
    foreach (var id in ids)
    {
        try
        {
            var t = await _repository.GetByIdAsync(id);
            if (t is null) { failed.Add(new { id, reason = "NOT_FOUND" }); continue; }
            if (t.IsActive == active) { succeeded.Add(id); continue; }
            t.IsActive = active;
            await _repository.UpdateTemplateAsync(t);
            await _audit.RecordAsync("Template", id, AuditActions.ToggledActive, CurrentActor, afterState: JsonConvert.SerializeObject(new { isActive = active }));
            succeeded.Add(id);
        }
        catch (Exception) { failed.Add(new { id, reason = "ERROR" }); }
    }
    return Content(JsonConvert.SerializeObject(new { succeeded, failed }), "application/json");
}

[Route("Templates/BulkExport")]
[HttpPost]
[ValidateJsonAntiForgeryToken]
public async Task<ActionResult> BulkExport(BulkIdsRequest request)
{
    var zip = await _promotion.BuildBulkZipAsync(request.Ids);
    Response.AddHeader("Content-Disposition", "attachment; filename=template-builder-export.zip");
    return File(zip, "application/zip");
}

[Route("Templates/BulkDelete")]
[HttpPost]
[ValidateJsonAntiForgeryToken]
public async Task<ActionResult> BulkDelete(BulkIdsRequest request)
{
    var succeeded = new List<int>();
    var failed = new List<object>();
    foreach (var id in request.Ids)
    {
        try
        {
            var t = await _repository.GetByIdAsync(id);
            if (t is null) { failed.Add(new { id, reason = "NOT_FOUND" }); continue; }
            await _audit.RecordAsync("Template", id, AuditActions.Deleted, CurrentActor, beforeState: JsonConvert.SerializeObject(new { name = t.Name }));
            if (await _repository.DeleteAsync(id)) succeeded.Add(id); else failed.Add(new { id, reason = "NOT_FOUND" });
        }
        catch (Exception) { failed.Add(new { id, reason = "ERROR" }); }
    }
    return Content(JsonConvert.SerializeObject(new { succeeded, failed }), "application/json");
}

[Route("Templates/{id:int}/Health")]
[HttpGet]
public async Task<ActionResult> GetHealth(int id)
{
    var report = await _health.CheckAsync(id);
    return Content(JsonConvert.SerializeObject(report, new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    }), "application/json");
}
```

Add the request DTO (in the controller file):

```csharp
public class BulkIdsRequest
{
    public int[] Ids { get; set; } = Array.Empty<int>();
}
```

Also: `AuditActions.Deleted` — verify it exists in `AuditActions.cs` (governance spec listed `deleted`); if absent, add `public const string Deleted = "deleted";` (it is expected to exist — check first; if missing it's a one-line fork fix).

- [ ] **Step 2: HealthController + view model**

```csharp
public class HealthIndexViewModel
{
    public IReadOnlyList<TemplateHealthReport> Reports { get; set; } = new List<TemplateHealthReport>();
}

public class HealthController : TemplateBuilderControllerBase
{
    private readonly ITemplateRepository _repository;
    private readonly ITemplateHealthService _health;
    public HealthController(ITemplateRepository repository, ITemplateHealthService health)
    { _repository = repository; _health = health; }

    [Route("Health")]
    [HttpGet]
    public async Task<ActionResult> Index()
    {
        var templates = await _repository.GetAllAsync();
        var reports = new List<TemplateHealthReport>();
        foreach (var t in templates)
            reports.Add(await _health.CheckAsync(t.Id));
        return View(new HealthIndexViewModel { Reports = reports });
    }

    [Route("Health/Summaries")]
    [HttpGet]
    public async Task<ActionResult> Summaries(string? ids)
    {
        var list = new List<object>();
        foreach (var raw in (ids ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(raw, out var id)) continue;
            var report = await _health.CheckAsync(id);
            list.Add(new { templateId = id, severity = SeverityName(report.Worst), findingCount = report.Findings.Count(f => f.Severity != HealthSeverity.Info) });
        }
        return Content(JsonConvert.SerializeObject(list), "application/json");
    }

    private static string SeverityName(HealthSeverity s) => s switch
    {
        HealthSeverity.Critical => "critical",
        HealthSeverity.Warning => "warning",
        _ => "healthy"
    };
}
```

- [ ] **Step 3: Unity registration** (in `RegisterTemplateBuilderEditor`, next to the audit registrations)

```csharp
container.RegisterType<ITemplatePromotionRepository, TemplatePromotionRepository>(new HierarchicalLifetimeManager());
container.RegisterType<ITemplatePromotionService, TemplatePromotionService>(new HierarchicalLifetimeManager());
container.RegisterType<ITemplateHealthService, TemplateHealthService>(new HierarchicalLifetimeManager());
```

- [ ] **Step 4: Build**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
Expected: 0 errors (pre-existing nullable warnings unchanged — compare the warning list; no new files in it).

- [ ] **Step 5: Commit (conditional)**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Controllers src/TemplateBuilder.Editor.Mvc5/Models/HealthIndexViewModel.cs src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs
git commit -m "feat: promotion/health/bulk endpoints and Unity registration"
```

---

### Task 8: Views + CSS (Index bulk bar, Health page, Import modal, editor health)

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Index.cshtml`
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Health/Index.cshtml`
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css` (append "Section 36 — Lifecycle & Ops")

**Interfaces:**
- Consumes: routes from Task 7; design tokens (`--surface`, `--border`, `--accent`, `--success/--warning/--danger`, `--radius-*`, `--transition`); `template-editor.js` guarded modules (Task 9).
- Produces: element ids the JS binds to (exact list in Task 9's step 1).

- [ ] **Step 1: Index.cshtml additions**

Inside the existing `<div class="tb-page-header">` actions area, add the Import button:

```html
<button type="button" id="btn-import-open" class="btn btn-secondary">Import</button>
```

Add the bulk toolbar directly under the page header (hidden until selection):

```html
<div id="tb-bulk-bar" class="tb-bulk-bar" hidden>
    <span id="tb-bulk-count" class="tb-bulk-count">0 selected</span>
    <button type="button" id="btn-bulk-activate" class="btn btn-sm btn-secondary">Activate</button>
    <button type="button" id="btn-bulk-deactivate" class="btn btn-sm btn-secondary">Deactivate</button>
    <button type="button" id="btn-bulk-export" class="btn btn-sm btn-secondary">Export ZIP</button>
    <button type="button" id="btn-bulk-delete" class="btn btn-sm btn-danger">Delete</button>
    <button type="button" id="btn-bulk-clear" class="btn btn-sm btn-ghost">Clear</button>
</div>
```

In the table header row, prepend a checkbox cell; in each body row, prepend:

```html
<td class="tb-check-col"><input type="checkbox" class="tb-row-check" value="@t.Id" aria-label="Select @t.Name"></td>
<td class="tb-health-col"><span class="tb-health-badge" data-template-id="@t.Id">—</span></td>
```

And add an Export row action in the existing actions cell: `<a class="tb-action-link" href="@Url.Action("ExportTemplate", "Templates", new { id = t.Id })">Export</a>`.

Add the import modal before the closing `</div>` of the host:

```html
<div class="modal-overlay" id="import-modal" hidden role="dialog" aria-modal="true" aria-labelledby="import-modal-title">
    <div class="modal modal--narrow">
        <div class="modal-header">
            <span id="import-modal-title" class="modal-title">Import templates</span>
            <button type="button" class="modal-close" id="btn-import-close" aria-label="Close">&#x2715;</button>
        </div>
        <div class="modal-body">
            <div class="tb-form-row">
                <label for="import-file">Export file (.template.json)</label>
                <input type="file" id="import-file" accept=".json">
            </div>
            <p class="tb-import-hint">Matched by external key — the same template in dev is updated, new templates are created. Review/Approved targets are skipped. Status: Review/Approved import as Draft.</p>
            <div id="import-error" class="tb-error-msg" role="alert" style="display:none;"></div>
            <button type="button" id="btn-import-submit" class="btn btn-primary">Import</button>
            <div id="import-result" class="tb-import-result"></div>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Health page view**

`Views/Health/Index.cshtml`:

```html
@using TemplateBuilder.Editor.Mvc5.Models
@using TemplateBuilder.Application.Services
@model HealthIndexViewModel
@{
    ViewBag.Title = "Template Health";
    var critical = Model.Reports.Count(r => r.Findings.Any(f => f.Severity == HealthSeverity.Critical));
    var warnings = Model.Reports.Count(r => r.Findings.Any(f => f.Severity == HealthSeverity.Warning) && !r.Findings.Any(f => f.Severity == HealthSeverity.Critical));
    var healthy = Model.Reports.Count(r => !r.Findings.Any(f => f.Severity != HealthSeverity.Info));
    var unbound = Model.Reports.Count(r => string.IsNullOrWhiteSpace(r.SourceView));
}
<div id="tb-editor-host" class="tb-audit-page">
    <div class="tb-page-content">
        <div class="tb-page-header">
            <div>
                <h1 class="tb-page-title">Template Health</h1>
                <p class="tb-audit-subtitle">Field drift between template bodies and the live SQL view schema.</p>
            </div>
            <div class="tb-audit-header-actions">
                <a class="btn btn-secondary" href="@Url.Action("Index", "Health")">Re-check all</a>
            </div>
        </div>
        <div class="tb-audit-stats">
            <div class="tb-audit-stat"><div class="tb-audit-stat-value">@healthy</div><div class="tb-audit-stat-label">Healthy</div></div>
            <div class="tb-audit-stat"><div class="tb-audit-stat-value">@warnings</div><div class="tb-audit-stat-label">Warnings</div></div>
            <div class="tb-audit-stat"><div class="tb-audit-stat-value">@critical</div><div class="tb-audit-stat-label">Critical</div></div>
            <div class="tb-audit-stat"><div class="tb-audit-stat-value">@unbound</div><div class="tb-audit-stat-label">Unbound</div></div>
        </div>
        <div class="tb-audit-card tb-audit-table-card">
            <div class="tb-audit-table-wrap">
                <table class="tb-audit-table">
                    <thead>
                        <tr><th>Template</th><th>Source view</th><th>Status</th><th>Findings</th><th></th></tr>
                    </thead>
                    <tbody>
                        @foreach (var r in Model.Reports)
                        {
                            var worst = r.Worst;
                            var badge = worst == HealthSeverity.Critical ? "tb-action-badge--snippet_deleted" : worst == HealthSeverity.Warning ? "tb-action-badge--toggled_active" : "tb-action-badge--published";
                            <tr>
                                <td><a class="tb-audit-target" href="@Url.Action("Edit", "Templates", new { id = r.TemplateId })"><b>@r.TemplateId</b></a></td>
                                <td>@(r.SourceView ?? "—")</td>
                                <td><span class="tb-action-badge @badge">@(worst == HealthSeverity.Critical ? "Critical" : worst == HealthSeverity.Warning ? "Warning" : "Healthy")</span></td>
                                <td>
                                    @foreach (var f in r.Findings.Where(f => f.Severity != HealthSeverity.Info).Take(3))
                                    {
                                        <div class="tb-health-finding tb-health-finding--@(f.Severity == HealthSeverity.Critical ? "critical" : "warning")">@f.Message</div>
                                    }
                                    @if (r.Findings.Count(f => f.Severity != HealthSeverity.Info) > 3)
                                    {
                                        <div class="tb-health-more">+@(r.Findings.Count(f => f.Severity != HealthSeverity.Info) - 3) more</div>
                                    }
                                </td>
                                <td class="tb-col-actions"><a class="tb-action-link" href="@Url.Action("Index", "Health")">Re-check</a></td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </div>
    </div>
</div>
```

(Adjust the first cell to show the template name — fetch names via `ITemplateRepository.GetAllAsync` in the controller and map by id; add `Name` to the view model row shape: `public string? Name { get; set; }` on a small `HealthRowViewModel` or reuse `TemplateHealthReport` + a parallel dictionary passed via `ViewBag` — pick the view-model-row approach: `HealthIndexViewModel.Rows` of `(Template Template, TemplateHealthReport Report)` tuples serialized into a row class.)

- [ ] **Step 3: Edit.cshtml additions**

In the Properties panel, after the Type select block:

```html
<div>
    <label for="prop-source-view">Source SQL View</label>
    <select id="prop-source-view" name="SourceView">
        <option value="">— unbound —</option>
        @foreach (var view in Model.AvailableViews)
        {
            <option value="@view" @(Model.SourceView == view ? "selected" : "")>@view</option>
        }
    </select>
</div>
```

And next to the Preview button:

```html
<button type="button" id="btn-health" class="btn btn-secondary">Health</button>
<div id="health-panel" class="tb-health-panel" hidden>
    <div id="health-findings"></div>
    <div id="health-meta" class="tb-health-meta"></div>
</div>
```

(`TemplateEditorViewModel` gains `public string? SourceView { get; set; }` — set it in the Edit GET action; the POST path adds `sourceView` handling to SaveVersion metadata: include `SourceView` in the saved metadata object and, when it changed, refresh `SourceViewSnapshot` via `_health.BuildSnapshotJsonAsync(view)`.)

- [ ] **Step 4: CSS (append Section 36)**

Append to `template-editor.css` (all selectors prefixed `#tb-editor-host`, variables only — works in both themes):

```css
/* ── 36. Lifecycle & Ops ── */
#tb-editor-host .tb-bulk-bar { display:flex; align-items:center; gap:8px; background:var(--surface); border:1px solid var(--border); border-radius:var(--radius-md); padding:8px 12px; margin-bottom:14px; box-shadow:var(--shadow-sm); }
#tb-editor-host .tb-bulk-bar[hidden] { display:none; }
#tb-editor-host .tb-bulk-count { font-size:12px; font-weight:600; color:var(--accent-hover); margin-right:4px; }
#tb-editor-host .tb-check-col { width:36px; }
#tb-editor-host .tb-check-col input[type=checkbox], #tb-editor-host .tb-row-check { cursor:pointer; }
#tb-editor-host .tb-health-badge { display:inline-block; border-radius:20px; padding:2px 9px; font-size:11px; font-weight:600; border:1px solid var(--border); color:var(--text-muted); background:var(--surface2); }
#tb-editor-host .tb-health-badge[data-severity="healthy"] { background:var(--success-bg); color:var(--success); border-color:var(--success-border); }
#tb-editor-host .tb-health-badge[data-severity="warning"] { background:var(--warning-bg); color:var(--warning); border-color:var(--warning-border); }
#tb-editor-host .tb-health-badge[data-severity="critical"] { background:var(--danger-bg); color:var(--danger); border-color:var(--danger-border); }
#tb-editor-host .tb-import-hint { font-size:12px; color:var(--text-muted); margin:0 0 10px; }
#tb-editor-host .tb-import-result { margin-top:12px; display:flex; flex-direction:column; gap:6px; }
#tb-editor-host .tb-import-entry { border-left:3px solid var(--border); padding:6px 10px; font-size:12px; background:var(--bg); border-radius:0 6px 6px 0; }
#tb-editor-host .tb-import-entry--created { border-color:var(--success); }
#tb-editor-host .tb-import-entry--updated { border-color:var(--accent); }
#tb-editor-host .tb-import-entry--skipped { border-color:var(--warning); }
#tb-editor-host .tb-import-entry--error { border-color:var(--danger); }
#tb-editor-host .tb-health-panel { border:1px solid var(--border); border-radius:var(--radius-md); padding:10px 12px; margin-top:10px; background:var(--bg); }
#tb-editor-host .tb-health-panel[hidden] { display:none; }
#tb-editor-host .tb-health-finding { border-left:3px solid var(--border); padding:4px 10px; font-size:12px; margin-bottom:6px; background:var(--surface2); border-radius:0 6px 6px 0; }
#tb-editor-host .tb-health-finding--critical { border-color:var(--danger); }
#tb-editor-host .tb-health-finding--warning { border-color:var(--warning); }
#tb-editor-host .tb-health-meta { font-size:11px; color:var(--text-muted); margin-top:6px; }
#tb-editor-host .tb-health-more { font-size:11px; color:var(--text-muted); }
```

- [ ] **Step 5: Build + codegen verification**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
Expected: 0 errors. Then:
Run: `grep -rl "tb-bulk-bar" src/TemplateBuilder.Editor.Mvc5/obj/CodeGen/ && grep -rl "tb-health-panel" src/TemplateBuilder.Editor.Mvc5/obj/CodeGen/ && ls src/TemplateBuilder.Editor.Mvc5/obj/CodeGen/Views/Health/`
Expected: `Views/Templates/Index.cshtml.cs`, `Views/Templates/Edit.cshtml.cs`, and `Views/Health/Index.cshtml.cs` present (RazorGenerator regenerated all three).

- [ ] **Step 6: Commit (conditional)**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Views src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css
git commit -m "feat: bulk toolbar, health page, import modal, editor health UI"
```

---

### Task 9: Editor JS modules

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (append guarded modules at the end)

**Interfaces:**
- Consumes: existing `_csrf` const, `showToast(msg)`, `fetch` JSON pattern from the existing save/audit modules; endpoints from Task 7.
- Produces: behavior only (no exports to other tasks).

- [ ] **Step 1: Bulk selection + toolbar module** (append; guard on `#tb-bulk-bar` presence)

```javascript
(function initBulkOps() {
    const bar = document.getElementById('tb-bulk-bar');
    if (!bar) return;
    const checks = () => [...document.querySelectorAll('.tb-row-check')];
    const countEl = document.getElementById('tb-bulk-count');
    const selectedIds = () => checks().filter(c => c.checked).map(c => parseInt(c.value, 10));

    function refresh() {
        const n = selectedIds().length;
        bar.hidden = n === 0;
        if (countEl) countEl.textContent = `${n} selected`;
    }
    checks().forEach(c => c.addEventListener('change', refresh));
    const selectAll = document.getElementById('tb-check-all');
    if (selectAll) selectAll.addEventListener('change', () => { checks().forEach(c => { c.checked = selectAll.checked; }); refresh(); });

    async function bulkPost(url, extra) {
        const ids = selectedIds();
        if (!ids.length) return null;
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _csrf },
            body: JSON.stringify({ ids, ...(extra || {}) })
        });
        return res.ok ? res.json() : null;
    }

    document.getElementById('btn-bulk-activate')?.addEventListener('click', async () => {
        const r = await bulkPost('/Templates/BulkActivate');
        if (r) showToast(`Activated ${r.succeeded.length} template(s).`);
        location.reload();
    });
    document.getElementById('btn-bulk-deactivate')?.addEventListener('click', async () => {
        const r = await bulkPost('/Templates/BulkDeactivate');
        if (r) showToast(`Deactivated ${r.succeeded.length} template(s).`);
        location.reload();
    });
    document.getElementById('btn-bulk-delete')?.addEventListener('click', async () => {
        const n = selectedIds().length;
        if (!confirm(`Delete ${n} template(s)? Version history is removed; audit records remain.`)) return;
        const r = await bulkPost('/Templates/BulkDelete');
        if (r) showToast(`Deleted ${r.succeeded.length} template(s).`);
        location.reload();
    });
    document.getElementById('btn-bulk-export')?.addEventListener('click', async () => {
        const ids = selectedIds();
        const res = await fetch('/Templates/BulkExport', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _csrf },
            body: JSON.stringify({ ids })
        });
        if (res.ok) {
            const blob = await res.blob();
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = 'template-builder-export.zip';
            a.click();
            URL.revokeObjectURL(a.href);
        } else showToast('Bulk export failed.');
    });
    document.getElementById('btn-bulk-clear')?.addEventListener('click', () => { checks().forEach(c => { c.checked = false; }); refresh(); });
})();
```

- [ ] **Step 2: Health badges module**

```javascript
(function initHealthBadges() {
    const badges = [...document.querySelectorAll('.tb-health-badge')];
    if (!badges.length) return;
    const ids = badges.map(b => b.getAttribute('data-template-id')).join(',');
    fetch(`/Health/Summaries?ids=${ids}`, { headers: { 'Accept': 'application/json' } })
        .then(r => r.json())
        .then(list => {
            const byId = new Map(list.map(x => [String(x.templateId), x]));
            badges.forEach(b => {
                const s = byId.get(b.getAttribute('data-template-id'));
                if (!s) return;
                b.setAttribute('data-severity', s.severity);
                b.textContent = s.severity === 'healthy' ? 'Healthy'
                    : s.severity === 'critical' ? `${s.findingCount} issue${s.findingCount === 1 ? '' : 's'}`
                    : `${s.findingCount} warning${s.findingCount === 1 ? '' : 's'}`;
            });
        })
        .catch(() => { /* badges stay as '—' */ });
})();
```

- [ ] **Step 3: Import modal module**

```javascript
(function initImportModal() {
    const modal = document.getElementById('import-modal');
    if (!modal) return;
    const open = document.getElementById('btn-import-open');
    const close = document.getElementById('btn-import-close');
    const submit = document.getElementById('btn-import-submit');
    const fileInput = document.getElementById('import-file');
    const result = document.getElementById('import-result');
    const errEl = document.getElementById('import-error');

    open?.addEventListener('click', () => { modal.hidden = false; if (result) result.innerHTML = ''; if (errEl) errEl.style.display = 'none'; });
    close?.addEventListener('click', () => { modal.hidden = true; });

    function renderEntry(entry, kind) {
        const text = entry.name
            ? `<b>${escapeHtml(entry.name)}</b>${entry.reason ? ` — ${escapeHtml(entry.reason)}` : ''}${entry.versionsAppended ? ` · ${entry.versionsAppended} versions appended` : ''}`
            : escapeHtml(entry.reason || 'Unknown file');
        return `<div class="tb-import-entry tb-import-entry--${kind}">${text}</div>`;
    }

    submit?.addEventListener('click', async () => {
        if (!fileInput || !fileInput.files || !fileInput.files.length) {
            if (errEl) { errEl.textContent = 'Choose a .template.json file first.'; errEl.style.display = 'block'; }
            return;
        }
        const fd = new FormData();
        fd.append('file', fileInput.files[0]);
        const res = await fetch('/Templates/Import', { method: 'POST', headers: { 'RequestVerificationToken': _csrf }, body: fd });
        if (!res.ok) { if (errEl) { errEl.textContent = 'Import failed.'; errEl.style.display = 'block'; } return; }
        const r = await res.json();
        if (result) {
            result.innerHTML = [
                ...(r.created || []).map(e => renderEntry(e, 'created')),
                ...(r.updated || []).map(e => renderEntry(e, 'updated')),
                ...(r.skipped || []).map(e => renderEntry(e, 'skipped')),
                ...(r.errors || []).map(e => renderEntry(e, 'error'))
            ].join('');
        }
        showToast('Import complete.');
        setTimeout(() => location.reload(), 2500);
    });
})();
```

- [ ] **Step 4: Editor health button module**

```javascript
(function initEditorHealth() {
    const btn = document.getElementById('btn-health');
    const panel = document.getElementById('health-panel');
    if (!btn || !panel || window.tbIsCreate === 'true' || (window.tbTemplateId || 0) <= 0) return;
    const findings = document.getElementById('health-findings');
    const meta = document.getElementById('health-meta');
    btn.addEventListener('click', async () => {
        const res = await fetch(`/Templates/${window.tbTemplateId}/Health`, { headers: { 'Accept': 'application/json' } });
        if (!res.ok) return;
        const r = await res.json();
        panel.hidden = false;
        if (findings) {
            const items = r.findings.filter(f => f.severity !== 'info' || f.severity !== 0);
            findings.innerHTML = items.length
                ? items.map(f => `<div class="tb-health-finding tb-health-finding--${f.severity === 2 ? 'critical' : 'warning'}">${escapeHtml(f.message)}</div>`).join('')
                : '<div class="tb-health-finding">No issues — template matches the schema.</div>';
        }
        if (meta) meta.textContent = r.sourceView ? `Bound to ${r.sourceView} · checked just now` : 'Template is not bound to a SQL view.';
    });
})();
```

- [ ] **Step 5: Syntax check**

Run: `node --check src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js`
Expected: no output, exit 0.

- [ ] **Step 6: Commit (conditional)**

```bash
git add src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js
git commit -m "feat: bulk selection, import modal, health badge/editor JS modules"
```

---

### Task 10: End-to-end verification (pack → sample host → xsp4 → agent-browser)

**Files:** none (verification only; fixes land in whatever task owns the broken file)

- [ ] **Step 1: Full solution build + tests**

Run (xsp4 stopped first): `dotnet build TemplateBuilder.Mvc5.sln --nologo` then `dotnet test tests/TemplateBuilder.Domain.Tests/...` , `tests/TemplateBuilder.Application.Tests/...`, `tests/TemplateBuilder.Infrastructure.EF6.Tests/...`
Expected: build 0 errors; Domain green; Application green (existing + new promotion/health suites); EF6 green (existing + new).

- [ ] **Step 2: Pack + reinstall + xbuild + restart** (MEMORY.md recipe, exact commands in Global Constraints)

- [ ] **Step 3: Smoke — endpoints via curl** (reuse the token/cookie pattern from PROGRESS.md: fetch a page for `__RequestVerificationToken` + cookie jar, send header `RequestVerificationToken`)

Check: `GET /Templates/Export/1` → 200 + `Content-Disposition` attachment; `GET /Templates/1/Health` → JSON report; `GET /Health` → 200 + chips; `GET /Health/Summaries?ids=1` → JSON; `POST /Templates/BulkActivate {"ids":[1]}` → `{"succeeded":[1],...}`; `POST /Templates/BulkExport {"ids":[1]}` → ZIP (save, `unzip -l` shows `*.template.json` + `_summary.json`); `POST /Templates/BulkDeactivate`, re-activate; `POST /Templates/Import` (multipart via `curl -F "file=@export.json"`) → created/updated JSON. **BulkDelete last** (removes the test template).

- [ ] **Step 4: agent-browser flows** (named session; recipes in MEMORY.md)

1. Create a template (form flow), note id; export it (click the Export row action; verify download), re-import it via the modal (result report shows "Created"), re-import again (shows "Updated · N versions appended").
2. Submit→approve a second template; import over it → "Skipped — Target is Review/Approved (locked)".
3. Health page: bind a template to a view, generate sample data (auto-binds), then `docker exec mssql-tb sqlcmd ...` to drop a column / alter a type in a scratch view, Re-check → critical/warning findings render; index badges show matching severity.
4. Bulk: check 2 rows → toolbar appears; Deactivate (toast + rows gray); Export ZIP (file downloads, `unzip -l` verify); Delete (confirm dialog → rows gone; `/Audit` still shows the `deleted`/`toggled_active` rows).
5. Editor: Source View select visible in Properties; Health button renders findings inline.
6. Screenshots to `/tmp/opencode/lifecycle-*.png` for the user (this model cannot view images — verify via DOM assertions + screenshots for the user).

- [ ] **Step 5: Fix forward**

Any failures: return the fix to the owning task (TDD: add a failing test first), re-run Steps 1–4. Record evidence in PROGRESS.md.

---

### Task 11: Docs + memory

**Files:**
- Modify: `README.md` (add Lifecycle & Ops feature bullets mirroring the governance section)
- Modify: `PROGRESS.md` (append this phase's gate evidence table)
- Modify: `MEMORY.md` (durable facts: promotion format schemaVersion 1, ExternalKey never regenerated/duplicated, SourceViewSnapshot is environment-local and NOT exported, import never clobbers locked templates, health findings severity precedence, the migration probe recipe for AddLifecycleOps)

- [ ] **Step 1: README bullets** — under the feature list add: export/import (JSON + version history, external-key identity), health check (view binding + drift findings), bulk ops (activate/deactivate/export ZIP/delete).
- [ ] **Step 2: PROGRESS.md gate table** — rows for Tasks 1–10 with the actual command outputs (paste them).
- [ ] **Step 3: MEMORY.md entries** — the durable facts above (2–4 bullets).
- [ ] **Step 4: Commit (conditional)**

```bash
git add README.md PROGRESS.md MEMORY.md
git commit -m "docs: lifecycle & ops phase — README, progress gates, memory"
```

---

## Self-review notes (fixes applied while writing)

- Spec module coverage: Module 1 → Tasks 1–5, 7 (endpoints), 9 (import UI); Module 2 → Tasks 1, 6, 7, 8, 9; Module 3 → Tasks 1 (DeleteAsync), 7, 9; Module 4 → Task 1 migration; Module 5 → Tasks 8–9; Module 6 → Tasks 10–11. All spec requirements mapped.
- `UpdateFromImportAsync` signature discrepancy between Task 2 (AppendVersionsAsync) and Task 4 (metadata update) — resolved in Task 4 Step 3b by keeping both methods on the repository.
- Scriban API member names verified against the actual Scriban 7.2.6 assembly via a reflection probe (see Task 6 interfaces note).
- `AuditActions.Deleted` may already exist from the governance phase — Task 7 Step 1 instructs verifying before adding.
