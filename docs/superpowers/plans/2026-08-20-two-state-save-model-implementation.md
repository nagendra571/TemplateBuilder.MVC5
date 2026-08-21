# Two-State Save Model (Draft/Active versions) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 5-state governance workflow (Draft/Review/Approved/Published + submit/approve/reject/cancel/publish + auto-save + editing locks) with a two-state save model: each version is marked Draft or Active, the editor shows the latest version, the developer render API serves the last Active version (typed exceptions otherwise), and template-level `IsActive` remains the servable switch.

**Architecture:** `TemplateVersion` gains `IsActive`; `Template.Status`/`TemplateStatus`/`DraftBody`/`ReviewComment` are removed from the domain; `TemplateWorkflowService` and all 6 workflow endpoints are deleted; `TemplateEngine.RenderAsync`/`RenderByNameAsync` enforce `IsActive` + last-active-version with typed exceptions; promotion export bumps to schemaVersion 2 (per-version `isActive`); the editor gets always-visible Save Draft / Save Version buttons and per-version badges; EF6 migrations add the version flag and drop the workflow columns.

**Tech Stack:** .NET Framework 4.8 / C# latest, ASP.NET MVC 5.3, EF6 6.5.1 (System.Data.SqlClient), Scriban 7.2.6, Newtonsoft.Json 13, xunit + FluentAssertions + NSubstitute, RazorGenerator-precompiled views, Docker SQL Server for EF6 tests, xsp4 sample-host smoke + agent-browser verification.

**Spec:** `docs/superpowers/specs/2026-08-20-two-state-save-model-design.md` — all decisions (D1–D13) are quoted from there.

## Global Constraints

- net48 + `<LangVersion>latest</LangVersion>` everywhere; nullable enabled.
- Domain/Application changes are deliberate fork deviations — mention "fork deviation (two-state-save spec)" in the commit message body for any file under `src/TemplateBuilder.Domain` or `src/TemplateBuilder.Application`.
- JSON responses: `Content(JsonConvert.SerializeObject(obj), "application/json")` (camelCase shapes); JSON POSTs take `[ValidateJsonAntiForgeryToken]`; the editor JS sends the `RequestVerificationToken` header (`_csrf`).
- Views are RazorGenerator-precompiled — never ship `.cshtml`; `dotnet build` regenerates `obj/CodeGen` (BLOCKERS #10).
- EF6 tests run against Docker SQL Server: `Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;` in `[Collection("Database")]` classes. **Stop the xsp4 sample host before running the EF6 suite** (shared DB — "Cannot drop database because it is currently in use"; kill by PID from `ss -ltnp | grep :8081`, never pkill the name).
- **Mono test-host crashes are transient:** a `dotnet test` run for a net48 project can abort with "Test host process crashed" (mono_crash dumps). Re-run once before treating it as a real failure; never debug a single crashed run.
- Sample-host verification cycle: `dotnet pack -c Release -o /tmp/opencode/nupkg-test` → replace the 4 DLLs in `samples/TemplateBuilder.SampleMvc5Host/packages/TemplateBuilder.Editor.Mvc5.<ver>/lib/net48/` from the extracted nupkg (nuget.exe may be absent after environment resets — DLL copy is equivalent) → bump the 4 `<HintPath>`s in the sample csproj to the new version → `xbuild samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj /p:Configuration=Debug` → restart xsp4: `XSP_BIN=/tmp/opencode/xsp/src/Mono.WebServer.XSP/bin/Debug; MONO_PATH=$XSP_BIN setsid mono $XSP_BIN/Mono.WebServer.XSP.exe --applications /:/workspaces/TemplateBuilder.Mvc5/samples/TemplateBuilder.SampleMvc5Host --port 8081 --nonstop > /tmp/opencode/xsp4.log 2>&1 < /dev/null &` (first request after boot can 500 once — EF init race; retry). If `/tmp/opencode/xsp` was wiped, rebuild xsp per BLOCKERS #11 (clone mono/xsp, checkout `72b24c0`, generate AssemblyInfo.cs from `.in`, `SignAssembly=false`, xbuild `src/Mono.WebServer.XSP/Mono.WebServer.XSP.csproj /p:Configuration=Debug`).
- `grep -cE " error "` exits 1 when the count is 0 — never chain `&&` after it.
- Commit steps: repo rule is commit only when the user explicitly asks. If the user has approved committing, use conventional style; otherwise skip commit steps and note uncommitted files at the end.
- Migration scaffolding (EF6 headless recipe, BLOCKERS #8): create `/tmp/opencode/migprobe` console project (net48) referencing the fork's `TemplateBuilder.Infrastructure.EF6.csproj` (copy the fork's current `Migrations/` folder into the probe so history matches), Program.cs uses `MigrationScaffolder` with `TargetDatabase` pointing at the Docker SQL connection string; build under mono and copy the three generated files into `src/TemplateBuilder.Infrastructure.EF6/Migrations/`.
- sqlcmd in container `mssql-tb` is `/opt/mssql-tools18/bin/sqlcmd` and needs `-C` (trust cert) + `-d <db>` (defaults to master otherwise).

---

### Task 1: Version-level IsActive + render-exception types + repository accessor (additive)

**Files:**
- Modify: `src/TemplateBuilder.Domain/Entities/TemplateVersion.cs`
- Create: `src/TemplateBuilder.Domain/Exceptions/TemplateInactiveException.cs`
- Create: `src/TemplateBuilder.Domain/Exceptions/NoActiveVersionException.cs`
- Modify: `src/TemplateBuilder.Domain/Interfaces/ITemplateRepository.cs`
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Repositories/TemplateRepository.cs`
- Create: `src/TemplateBuilder.Infrastructure.EF6/Migrations/AddVersionIsActive.cs` + `.Designer.cs` + `.resx` (scaffolded, then hand-add the default backfill)
- Modify: `tests/TemplateBuilder.Domain.Tests/InterfaceContractTests.cs`
- Create: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateVersionStatusTests.cs`

**Interfaces:**
- Produces:
  - `TemplateVersion.IsActive` — `bool`, defaults `true` (true = Active save, false = Draft save).
  - `TemplateInactiveException : Exception` with ctor `(int templateId)`, message `"Template {templateId} is inactive and not servable."`
  - `NoActiveVersionException : Exception` with ctor `(int templateId)`, message `"Template {templateId} has no active version to serve."`
  - `ITemplateRepository.GetLastActiveVersionAsync(int templateId, CancellationToken ct = default)` → `Task<TemplateVersion?>` (latest version with `IsActive == true`, `null` if none).

- [ ] **Step 1: Write the failing tests**

`TemplateVersionStatusTests.cs` (new file, `[Collection("Database")]`, copy the `CreateContext()` helper pattern from `AuditRepositoryTests.cs`):

```csharp
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
```

`InterfaceContractTests.cs` — add `"GetLastActiveVersionAsync"` to the expected method list in `ITemplateRepository_has_exactly_the_plan_surface` (sorted: after `GetCurrentVersionIdAsync`).

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj --nologo -v q --filter "FullyQualifiedName~InterfaceContractTests"` then `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TemplateVersionStatusTests"`
Expected: FAIL — `IsActive`/`GetLastActiveVersionAsync` missing (CS1061/CS0117), InterfaceContract fails on the missing method.

- [ ] **Step 3: Implement the domain + repository changes**

`TemplateVersion.cs` — add after `CreatedBy`:

```csharp
public bool IsActive { get; set; } = true;
```

Exceptions (match `TemplateNotFoundException.cs`'s file style):

```csharp
using System;
namespace TemplateBuilder.Domain.Exceptions;

public class TemplateInactiveException : Exception
{
    public TemplateInactiveException(int templateId)
        : base($"Template {templateId} is inactive and not servable.") { }
}
```

```csharp
using System;
namespace TemplateBuilder.Domain.Exceptions;

public class NoActiveVersionException : Exception
{
    public NoActiveVersionException(int templateId)
        : base($"Template {templateId} has no active version to serve.") { }
}
```

`ITemplateRepository.cs` — add after `GetCurrentVersionIdAsync`:

```csharp
Task<TemplateVersion?> GetLastActiveVersionAsync(int templateId, CancellationToken ct = default);
```

`TemplateRepository.cs` — add after `GetVersionBodyAsync`:

```csharp
public async Task<TemplateVersion?> GetLastActiveVersionAsync(int templateId, CancellationToken ct = default)
    => await _db.TemplateVersions
        .Where(v => v.TemplateId == templateId && v.IsActive)
        .OrderByDescending(v => v.VersionNumber)
        .FirstOrDefaultAsync(ct);
```

- [ ] **Step 4: Scaffold the `AddVersionIsActive` migration**

Use the migprobe recipe (Global Constraints) with `scaffolder.Scaffold("AddVersionIsActive")`. The generated `Up()` will contain `AddColumn("dbo.TemplateVersions", "IsActive", c => c.Boolean(nullable: false))` — hand-edit it to backfill legacy rows as Active:

```csharp
AddColumn("dbo.TemplateVersions", "IsActive", c => c.Boolean(nullable: false, defaultValue: true));
```

(No Sql() backfill needed — the DB default covers existing rows.)

- [ ] **Step 5: Run tests — verify green**

Run both commands from Step 2.
Expected: PASS (InterfaceContract, 3/3 version-status tests). If EF6 fails with "Invalid column name 'IsActive'" the migration didn't apply — re-check Step 4's defaultValue edit and re-run.

- [ ] **Step 6: Full suites**

Run: `dotnet test tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj --nologo -v q` then `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --nologo -v q` (xsp4 stopped).
Expected: Domain all green; EF6 all green (37 + 3 new). Re-run once on a mono crash before investigating.

- [ ] **Step 7: Commit (conditional on user approval)**

```bash
git add src/TemplateBuilder.Domain tests/TemplateBuilder.Domain.Tests src/TemplateBuilder.Infrastructure.EF6 tests/TemplateBuilder.Infrastructure.EF6.Tests
git commit -m "feat: version-level IsActive with last-active accessor and render exceptions

Fork deviation (two-state-save spec): TemplateVersion gains IsActive (draft
vs active save); ITemplateRepository.GetLastActiveVersionAsync; new
TemplateInactiveException/NoActiveVersionException; AddVersionIsActive
migration backfills legacy versions as Active."
```

---
### Task 2: TemplateEngine render contract (typed exceptions + last-active selection)

**Files:**
- Modify: `src/TemplateBuilder.Application/Services/TemplateEngine.cs`
- Modify: `tests/TemplateBuilder.Application.Tests/TemplateEngineTests.cs`

**Interfaces:**
- Consumes: `ITemplateRepository.GetByIdAsync`, `GetByNameAsync`, `GetLastActiveVersionAsync` (Task 1); `TemplateNotFoundException` (existing), `TemplateInactiveException`, `NoActiveVersionException` (Task 1).
- Produces (behavior contract): `RenderAsync(int id, object model, CancellationToken)` and `RenderByNameAsync(string name, object model, CancellationToken)` throw `TemplateNotFoundException` when missing, `TemplateInactiveException` when `IsActive == false`, `NoActiveVersionException` when no Active version exists; otherwise render the **last Active** version's body. `RenderBodyAsync` unchanged.

- [ ] **Step 1: Write the failing tests** — modify `TemplateEngineTests.cs`. The file uses **Moq** with helper `private static TemplateEngine CreateEngine(Mock<ITemplateRepository>? repo = null) => new(repo?.Object ?? new Mock<ITemplateRepository>().Object, new TemplateBuilderOptions());` — reuse it verbatim (do not add a duplicate helper).

First, REWRITE the three existing tests that assert the old draft-rendering contract:

```csharp
[Fact]
public async Task RenderAsync_renders_current_version_body()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template
        {
            Id = 7,
            Name = "Welcome",
            IsActive = true,
            CurrentVersion = new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" }
        });
    repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" });

    var engine = CreateEngine(repo);
    var result = await engine.RenderAsync(7, new { user = "bob" });

    result.Should().Be("Welcome bob");
}
```

```csharp
[Fact]
public async Task RenderAsync_throws_NoActiveVersionException_when_template_has_no_versions()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template { Id = 1, Name = "Empty", IsActive = true, CurrentVersion = null });
    repo.Setup(r => r.GetLastActiveVersionAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync((TemplateVersion?)null);

    var engine = CreateEngine(repo);
    var act = () => engine.RenderAsync(1, new { });

    await act.Should().ThrowAsync<NoActiveVersionException>();
}
```

```csharp
[Fact]
public async Task RenderByNameAsync_renders_current_version_body()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByNameAsync("Welcome", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template
        {
            Id = 7,
            Name = "Welcome",
            IsActive = true,
            CurrentVersion = new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" }
        });
    repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateVersion { VersionNumber = 2, Body = "Welcome {{user}}" });

    var engine = CreateEngine(repo);
    var result = await engine.RenderByNameAsync("Welcome", new { user = "carol" });

    result.Should().Be("Welcome carol");
}
```

Then append these five new tests:

```csharp
[Fact]
public async Task RenderAsync_throws_when_template_is_inactive()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template { Id = 7, IsActive = false, CurrentVersion = new TemplateVersion { Body = "x" } });

    var engine = CreateEngine(repo);
    var act = () => engine.RenderAsync(7, new { });

    await act.Should().ThrowAsync<TemplateInactiveException>();
}

[Fact]
public async Task RenderAsync_throws_when_no_active_version_exists()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template { Id = 7, IsActive = true });
    repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync((TemplateVersion?)null);

    var engine = CreateEngine(repo);
    var act = () => engine.RenderAsync(7, new { });

    await act.Should().ThrowAsync<NoActiveVersionException>();
}

[Fact]
public async Task RenderAsync_renders_last_active_version_when_latest_is_draft()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template
        {
            Id = 7, IsActive = true,
            CurrentVersion = new TemplateVersion { VersionNumber = 3, Body = "<p>draft {{ model.Name }}</p>", IsActive = false }
        });
    repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateVersion { VersionNumber = 2, Body = "<p>active {{ model.Name }}</p>", IsActive = true });

    var engine = CreateEngine(repo);
    var html = await engine.RenderAsync(7, new { Name = "X" });

    html.Should().Contain("active X");
    html.Should().NotContain("draft");
}

[Fact]
public async Task RenderByNameAsync_throws_TemplateInactiveException_for_inactive_template()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByNameAsync("Off", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template { Id = 9, IsActive = false, CurrentVersion = new TemplateVersion { Body = "x" } });

    var engine = CreateEngine(repo);
    var act = () => engine.RenderByNameAsync("Off", new { });

    await act.Should().ThrowAsync<TemplateInactiveException>();
}

[Fact]
public async Task RenderByNameAsync_renders_last_active_version_when_latest_is_draft()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByNameAsync("Inv", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template
        {
            Id = 5, IsActive = true,
            CurrentVersion = new TemplateVersion { VersionNumber = 2, Body = "draft body", IsActive = false }
        });
    repo.Setup(r => r.GetLastActiveVersionAsync(5, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateVersion { VersionNumber = 1, Body = "<p>v1 {{ model.Name }}</p>", IsActive = true });

    var engine = CreateEngine(repo);
    var html = await engine.RenderByNameAsync("Inv", new { Name = "Y" });

    html.Should().Contain("v1 Y");
}
```

(The existing `RenderAsync_throws_TemplateNotFoundException_when_template_missing` and `RenderByNameAsync_throws_TemplateNotFoundException_when_template_missing` tests stay unchanged.)

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TemplateEngineTests"`
Expected: FAIL — the 3 rewritten + 5 new tests fail (missing exception types / old draft-rendering behavior); the `RenderBodyAsync_*` and missing-template tests stay green.

- [ ] **Step 3: Implement**

Replace the two render method bodies in `TemplateEngine.cs` (keep `RenderBodyAsync` untouched):

```csharp
public async Task<string> RenderAsync(int templateId, object model, CancellationToken ct = default)
{
    var template = await _repository.GetByIdAsync(templateId, ct);
    if (template is null)
        throw new TemplateNotFoundException($"Template {templateId} not found.");
    if (!template.IsActive)
        throw new TemplateInactiveException(templateId);

    var activeVersion = await _repository.GetLastActiveVersionAsync(templateId, ct)
        ?? throw new NoActiveVersionException(templateId);

    return await RenderBodyAsync(activeVersion.Body, model, ct);
}

public async Task<string> RenderByNameAsync(string templateName, object model, CancellationToken ct = default)
{
    var template = await _repository.GetByNameAsync(templateName, ct);
    if (template is null)
        throw new TemplateNotFoundException($"Template '{templateName}' not found.");
    if (!template.IsActive)
        throw new TemplateInactiveException(template.Id);

    var activeVersion = await _repository.GetLastActiveVersionAsync(template.Id, ct)
        ?? throw new NoActiveVersionException(template.Id);

    return await RenderBodyAsync(activeVersion.Body, model, ct);
}
```

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS (pre-existing + rewritten + 5 new = 20 tests in the class).

- [ ] **Step 5: Full Application suite**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --nologo -v q`
Expected: all green (86 + 3 new behaviors; count grows by the net test delta). Re-run once on a mono crash.

- [ ] **Step 6: Commit (conditional)**

```bash
git add src/TemplateBuilder.Application/Services/TemplateEngine.cs tests/TemplateBuilder.Application.Tests/TemplateEngineTests.cs
git commit -m "feat: render API serves last active version with typed exceptions

Fork deviation (two-state-save spec): RenderAsync/RenderByNameAsync enforce
template IsActive and throw TemplateInactiveException/NoActiveVersionException
instead of silently rendering drafts."
```

### Task 3: Promotion format v2 (per-version isActive, no status string)

**Files:**
- Modify: `src/TemplateBuilder.Application/Services/ITemplatePromotionService.cs` (DTOs + `ExporterInfo.Version`)
- Modify: `src/TemplateBuilder.Application/Services/TemplatePromotionService.cs`
- Modify: `tests/TemplateBuilder.Application.Tests/TemplatePromotionServiceTests.cs`
- Modify: `tests/TemplateBuilder.Application.Tests/TemplatePromotionImportTests.cs`
- Modify: `tests/TemplateBuilder.Application.Tests/TemplatePromotionBulkZipTests.cs` (fixture only)

**Interfaces:**
- Consumes: `TemplateVersion.IsActive` (Task 1).
- Produces: export `schemaVersion: 2`; `TemplateExportVersion.IsActive` (bool); `TemplateExportTemplate` has NO `Status` property; `TemplateImportResult` shape unchanged (`Skipped` stays but is always empty); `ImportAsync` rejects schemaVersion != 2, never skips, preserves version `isActive` and template `isActive`; `CollapseStatus` is deleted.

- [ ] **Step 1: Rewrite the promotion tests (red)**

`TemplatePromotionServiceTests.cs`:
- `BuildExport_shapes_document_with_ordered_versions`: remove `Status = TemplateStatus.Published` from the fixture (keep `IsActive = true`); history fixtures become `new TemplateVersion { VersionNumber = 2, Body = "<p>two</p>", ChangeComment = "c2", IsActive = false }` and `new TemplateVersion { VersionNumber = 1, Body = "<p>one</p>", IsActive = true }`. Replace the `doc.Template.Status.Should().Be("Published");` assertion with:

```csharp
doc.SchemaVersion.Should().Be(2);
doc.Template.Versions.Select(v => v.IsActive).Should().Equal(true, false);
```

`TemplatePromotionImportTests.cs` — replace the whole file with:

```csharp
[Fact]
public async Task Import_rejects_legacy_schema_v1_file()
{
    var (svc, _, _, _) = Create();
    var json = "{ \"schemaVersion\": 1, \"template\": { \"name\": \"X\" } }";
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(json), "bob");
    result.Errors.Should().ContainSingle(e => e.Reason.Contains("schemaVersion"));
    result.Created.Should().BeEmpty();
}

[Fact]
public async Task Import_creates_template_preserving_version_and_template_flags()
{
    var (svc, _, promo, audit) = Create();
    var key = Guid.NewGuid();
    promo.GetByExternalKeyAsync(key).Returns((Template?)null);
    Template captured = null!;
    promo.AddWithVersionsAsync(Arg.Do<Template>(t => captured = t), Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>())
        .Returns(ci => ci.Arg<Template>());
    var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", IsActive = false, Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>ok</p>", IsActive = true }, new TemplateExportVersion { VersionNumber = 2, Body = "<p>draft</p>", IsActive = false } } } };

    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");

    result.Created.Should().ContainSingle(c => c.Name == "X");
    captured.IsActive.Should().BeFalse();
    captured.ExternalKey.Should().Be(key);
    await promo.Received(1).AddWithVersionsAsync(captured, Arg.Is<IReadOnlyList<TemplateVersion>>(vs => vs.Select(v => v.IsActive).SequenceEqual(new[] { true, false })), Arg.Any<CancellationToken>());
    await audit.Received(1).RecordAsync("Template", Arg.Any<int>(), AuditActions.Imported, "bob",
        Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Import_updates_existing_target_preserving_version_flags()
{
    var (svc, _, promo, audit) = Create();
    var key = Guid.NewGuid();
    var existing = new Template { Id = 9, Name = "Old", TemplateType = "Email", IsActive = true };
    promo.GetByExternalKeyAsync(key).Returns(existing);
    promo.UpdateFromImportAsync(existing, Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>())
        .Returns(new[] { 2, 3 });
    var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", IsActive = true, Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>a</p>", IsActive = false }, new TemplateExportVersion { VersionNumber = 2, Body = "<p>b</p>", IsActive = true } } } };

    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");

    result.Updated.Should().ContainSingle(u => u.Name == "X" && u.VersionsAppended == 2);
    result.Created.Should().BeEmpty();
    result.Skipped.Should().BeEmpty();
    existing.Name.Should().Be("X");
    await promo.Received(1).UpdateFromImportAsync(existing, Arg.Is<IReadOnlyList<TemplateVersion>>(vs => vs.Select(v => v.IsActive).SequenceEqual(new[] { false, true })), Arg.Any<CancellationToken>());
    await audit.Received(1).RecordAsync("Template", 9, AuditActions.Imported, "bob",
        Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Import_rejects_scriban_invalid_body()
{
    var (svc, _, _, _) = Create();
    var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = Guid.NewGuid(), Name = "X", TemplateType = "Email", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "{{ end }}" } } } };
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
    result.Errors.Should().ContainSingle(e => e.Reason.Contains("Version 1"));
}

private static (TemplatePromotionService svc, ITemplateRepository repo, ITemplatePromotionRepository promo, IAuditService audit) Create()
{
    var repo = Substitute.For<ITemplateRepository>();
    var promo = Substitute.For<ITemplatePromotionRepository>();
    var audit = Substitute.For<IAuditService>();
    return (new TemplatePromotionService(repo, promo, audit), repo, promo, audit);
}
```

`TemplatePromotionBulkZipTests.cs`: remove `Status = TemplateStatus.Published` from the `GetByIdAsync(1)` fixture (nothing else changes).

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TemplatePromotion"`
Expected: FAIL/compile errors — `TemplateExportTemplate.Status` no longer settable, `CollapseStatus` missing, `TemplateExportVersion.IsActive` missing.

- [ ] **Step 3: Implement the DTO + service changes**

`ITemplatePromotionService.cs`:
- `TemplateExportVersion` gains: `public bool IsActive { get; set; } = true;`
- `TemplateExportTemplate`: delete the `Status` property.
- `TemplateExportDocument`: `SchemaVersion` default becomes `2`.
- `ExporterInfo.Version` becomes `"1.2.0"`.

`TemplatePromotionService.cs`:
- `BuildExportAsync`: in the `Versions` projection add `IsActive = v.IsActive`; remove nothing else.
- `ImportAsync` rewrite:

```csharp
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
    if (doc is null || doc.SchemaVersion != 2)
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
            IsActive = doc.Template.IsActive
        };
        var versions = doc.Template.Versions.Select(v => new TemplateVersion
        {
            VersionNumber = v.VersionNumber,
            Body = v.Body,
            ChangeComment = v.ChangeComment,
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy,
            IsActive = v.IsActive
        }).ToList();
        var created = await _promotion.AddWithVersionsAsync(template, versions, ct);
        await _audit.RecordAsync("Template", created.Id, AuditActions.Imported, actor,
            afterState: JsonConvert.SerializeObject(new { file = doc.Template.Name, externalKey = created.ExternalKey, versionsImported = versions.Count }), ct: ct);
        result.Created.Add(new TemplateImportEntry { Name = created.Name, ExternalKey = created.ExternalKey });
        return result;
    }

    existing.Name = doc.Template.Name.Trim();
    existing.TemplateType = doc.Template.TemplateType;
    existing.Description = doc.Template.Description;
    existing.SampleData = doc.Template.SampleData;
    existing.IsActive = doc.Template.IsActive;

    var importedVersions = doc.Template.Versions.Select(v => new TemplateVersion
    {
        Body = v.Body,
        ChangeComment = v.ChangeComment is null ? $"Imported from {doc.Exporter.Name} ({doc.ExportedAt:u})" : $"{v.ChangeComment} — imported {doc.ExportedAt:u}",
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy,
        IsActive = v.IsActive
    }).ToList();

    var assigned = await _promotion.UpdateFromImportAsync(existing, importedVersions, ct);
    await _audit.RecordAsync("Template", existing.Id, AuditActions.Imported, actor,
        afterState: JsonConvert.SerializeObject(new { file = doc.Template.Name, externalKey = existing.ExternalKey, versionsImported = assigned.Length }), ct: ct);
    result.Updated.Add(new TemplateImportEntry { Name = existing.Name, ExternalKey = existing.ExternalKey, VersionsAppended = assigned.Length });
    return result;
}
```

Also delete the `CollapseStatus` method entirely.

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS (4 service/bulk + 4 import tests).

- [ ] **Step 5: Full Application suite**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --nologo -v q`
Expected: all green. Re-run once on a mono crash.

- [ ] **Step 6: Commit (conditional)**

```bash
git add src/TemplateBuilder.Application/Services/TemplatePromotionService.cs src/TemplateBuilder.Application/Services/ITemplatePromotionService.cs tests/TemplateBuilder.Application.Tests/TemplatePromotionServiceTests.cs tests/TemplateBuilder.Application.Tests/TemplatePromotionImportTests.cs tests/TemplateBuilder.Application.Tests/TemplatePromotionBulkZipTests.cs
git commit -m "feat: promotion format v2 — per-version isActive, drop status string

Fork deviation (two-state-save spec): export schemaVersion 2 carries
per-version Draft/Active flags; import never skips and preserves flags."
```

---
### Task 4: Remove the workflow — domain columns, service, endpoints, UI bindings

**Files:**
- Delete: `src/TemplateBuilder.Domain/Entities/TemplateStatus.cs`
- Delete: `src/TemplateBuilder.Application/Services/TemplateWorkflowService.cs`, `ITemplateWorkflowService.cs`, `TemplateWorkflowResult.cs`
- Delete: `tests/TemplateBuilder.Application.Tests/TemplateWorkflowServiceTests.cs`
- Delete: `src/TemplateBuilder.Editor.Mvc5/Models/SaveDraftRequest.cs`, `SubmitForReviewRequest.cs`, `RejectRequest.cs`
- Modify: `src/TemplateBuilder.Domain/Entities/Template.cs` (remove `Status`, `DraftBody`, `ReviewComment`)
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Data/TemplateBuilderDbContext.cs` (remove `ReviewComment` mapping)
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs` (remove workflow endpoints + `RunWorkflow`/`MapWorkflowResult` + `Status`/`DraftBody` references)
- Modify: `src/TemplateBuilder.Editor.Mvc5/Models/TemplateEditorViewModel.cs` (remove `Status`, `DraftBody`, `ReviewComment`)
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml` (remove status pill, workflow buttons, lock/review/draft banners, `window.tbStatus`/`tbReviewComment`)
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (remove workflow module, `isLocked`, `updateWorkflowUI`, workflow button handlers)
- Modify: `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs` (remove `ITemplateWorkflowService` registration)
- Create: `src/TemplateBuilder.Infrastructure.EF6/Migrations/SimplifyTemplateStatus.cs` + `.Designer.cs` + `.resx` (scaffolded)
- Modify: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateLifecycleColumnsTests.cs` (assert dropped columns)

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: no workflow surface anywhere; `Template` has no `Status`/`DraftBody`/`ReviewComment`; migration `SimplifyTemplateStatus` drops `dbo.Templates.Status`, `DraftBody`, `ReviewComment`; build green with zero references to the removed types.

- [ ] **Step 1: Remove Application + Domain workflow code**

Delete the 4 files listed (3 service files + `TemplateStatus.cs` + the test file — `git rm` them). In `Template.cs` remove the three properties. In `TemplateBuilderDbContext.cs` remove `template.Property(t => t.ReviewComment).HasMaxLength(1000);`.

- [ ] **Step 2: Remove Editor references (the compiler enumerates them — fix all)**

`TemplatesController.cs`:
- Remove the endpoints `SaveDraft`, `SubmitForReview`, `Approve`, `Reject`, `CancelReview`, `Publish`, the private `RunWorkflow` and `MapWorkflowResult` helpers, and the `_workflow` field + constructor parameter + `ITemplateWorkflowService` using.
- `Edit` GET: `Body = template.DraftBody ?? template.CurrentVersion?.Body ?? string.Empty` becomes `Body = template.CurrentVersion?.Body ?? string.Empty`; remove the `Status`/`DraftBody`/`ReviewComment` view-model assignments.

`TemplateEditorViewModel.cs`: remove `Status`, `DraftBody`, `ReviewComment`.

`UnityContainerExtensions.cs`: remove `container.RegisterType<ITemplateWorkflowService, TemplateWorkflowService>(new HierarchicalLifetimeManager());`.

`Edit.cshtml`:
- Remove the `tb-status-pill` span, the whole `#tb-workflow-actions` div (buttons `btn-submit-review`/`btn-approve`/`btn-reject`/`btn-cancel-review`/`btn-publish`), the `tb-lock-banner`/`tb-review-comment` banners, and the auto-save toolbar button `btn-autosave-toggle` (and the whole `tb-draft-banner` block).
- In the inline script block remove `window.tbStatus = ...`, `window.tbReviewComment = ...`; keep `templateId`, `currentVersionNumber`, `savedSampleData`, `tbTemplateId`, `tbIsCreate`.

`template-editor.js`:
- Delete the entire `// ── Workflow ──` module (from `const tbStatus =` through the `btn-publish` click handler, just before the `// Activity drawer (Edit page)` comment) and the entire `// ── Auto-save draft ──` module (from the section banner through `setTimeout(loadDraft, 500);`).
- Everywhere `updateWorkflowUI()`/`clearDraft()`/`markClean()` are called from the remaining code (saveVersion success handler around line 743, restore/duplicate/compare handlers, `_isDirty` tracking): keep `markClean()` (it only resets `_isDirty` — harmless) but delete all `clearDraft()` calls and the `clearDraft`/`saveDraft`/`loadDraft`/`updateDraftStatus`/`updateAutoSaveToggle`/`isAutoSaveEnabled` functions, `DRAFT_KEY`/`AUTOSAVE_PREF_KEY`/`AUTOSAVE_INTERVAL` consts, and the `setInterval(saveDraft, ...)` line.
- `grep` for `tbStatus` and `Draft` in the JS after the removal — no references may remain except comments.

- [ ] **Step 3: Build — iterate until 0 errors**

Run: `dotnet build TemplateBuilder.Mvc5.sln --nologo`
Expected: 0 errors (the compiler is the checklist — resolve every remaining `TemplateStatus`/`_workflow`/`DraftBody` reference it reports).

- [ ] **Step 4: Scaffold the `SimplifyTemplateStatus` migration**

migprobe recipe with `scaffolder.Scaffold("SimplifyTemplateStatus")` — the model diff drops the three template columns, so `Up()` should contain only `DropColumn` calls. No hand-edits expected; if the scaffolder also emits an `AddColumn` for `TemplateVersions.IsActive`, it means `AddVersionIsActive` (Task 1) wasn't applied to the probe's DB history — copy the probe `Migrations/` folder from the fork AFTER Task 1's migration exists and retry.

- [ ] **Step 5: Update the EF6 column test**

In `TemplateLifecycleColumnsTests.cs` (or add to `TemplateVersionStatusTests.cs`) add a test asserting the new schema:

```csharp
[Fact]
public async Task SimplifyTemplateStatus_migration_dropped_workflow_columns()
{
    using var ctx = CreateContext();
    var sql = await ctx.Database.SqlQuery<string>(
        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Templates' AND COLUMN_NAME IN ('Status','DraftBody','ReviewComment')")
        .ToListAsync();
    sql.Should().BeEmpty();
}
```

- [ ] **Step 6: Run all suites**

Run (xsp4 stopped): Domain, Application, EF6 suites in full.
Expected: Domain green (InterfaceContract unchanged); Application green (workflow tests gone: 91 − 14 = 77); EF6 green (40 + 1 new). Re-run once on a mono crash.

- [ ] **Step 7: Commit (conditional)**

```bash
git rm src/TemplateBuilder.Domain/Entities/TemplateStatus.cs src/TemplateBuilder.Application/Services/TemplateWorkflowService.cs src/TemplateBuilder.Application/Services/ITemplateWorkflowService.cs src/TemplateBuilder.Application/Services/TemplateWorkflowResult.cs tests/TemplateBuilder.Application.Tests/TemplateWorkflowServiceTests.cs src/TemplateBuilder.Editor.Mvc5/Models/SaveDraftRequest.cs src/TemplateBuilder.Editor.Mvc5/Models/SubmitForReviewRequest.cs src/TemplateBuilder.Editor.Mvc5/Models/RejectRequest.cs
git add src tests
git commit -m "feat: remove governance workflow in favor of two-state saves

Fork deviation (two-state-save spec): TemplateStatus, Status/DraftBody/
ReviewComment columns, TemplateWorkflowService and all 6 workflow endpoints
are deleted; SimplifyTemplateStatus migration drops the columns."
```

---

### Task 5: Save semantics — isActive flag, create-without-version, restore/duplicate inherit

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Models/SaveVersionRequest.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/Models/TemplateEditorViewModel.cs` (add `LatestVersionIsActive`)
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs`

**Interfaces:**
- Consumes: `TemplateVersion.IsActive` (Task 1).
- Produces: `SaveVersionRequest.IsActive` (bool, default true); `SaveVersion` writes `TemplateVersion.IsActive = request.IsActive` and audits `published` (true) or `draft_saved` (false); `CreateTemplateJson` creates NO version; `RestoreVersion` copies the source version's `IsActive`; `Duplicate` copies the latest version's body + `IsActive` into v1; `TemplateEditorViewModel.LatestVersionIsActive`.

- [ ] **Step 1: Request + view model changes**

`SaveVersionRequest.cs` — add after `SourceView`:

```csharp
public bool IsActive { get; set; } = true;
```

`TemplateEditorViewModel.cs` — add after `CurrentVersionNumber`:

```csharp
public bool LatestVersionIsActive { get; set; } = true;
```

- [ ] **Step 2: Controller changes**

`CreateTemplateJson` — delete the `if (!string.IsNullOrWhiteSpace(model.Body)) { await _repository.PublishVersionAsync(...); }` block (keep the `CreateAsync` + audit + `JsonOk`).

`Edit` GET — set `LatestVersionIsActive = template.CurrentVersion?.IsActive ?? true`.

`SaveVersion` — change the version construction to:

```csharp
published = await _repository.PublishVersionAsync(id, new TemplateVersion
{
    TemplateId = id,
    VersionNumber = nextNumber,
    Body = request.Body,
    ChangeComment = request.ChangeComment,
    IsActive = request.IsActive
});
```

and the audit line to:

```csharp
await _audit.RecordAsync("Template", id, request.IsActive ? AuditActions.Published : AuditActions.DraftSaved, CurrentActor,
    afterState: JsonConvert.SerializeObject(new { versionNumber = published.VersionNumber, versionId = published.Id, isActive = published.IsActive }));
```

and the success payload to `return JsonOk(new { versionId = published.Id, versionNumber = published.VersionNumber, isActive = published.IsActive });` (the editor JS consumes `isActive` to update the draft badge).

`RestoreVersion` — after fetching `oldBody`, also read the source version's flag (history is already loaded via `_repository` — fetch `var source = (await _repository.GetVersionHistoryAsync(id)).FirstOrDefault(v => v.Id == versionId);`), then pass `IsActive = source?.IsActive ?? true` in the new version.

`Duplicate` — after `var body = source.CurrentVersion?.Body ?? string.Empty;` add `var isActive = source.CurrentVersion?.IsActive ?? true;` and pass `IsActive = isActive` in the new v1 version.

- [ ] **Step 3: Build**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
Expected: 0 errors (pre-existing warnings unchanged).

- [ ] **Step 4: Verify via the Task 8 smoke (endpoint behavior is covered there; no Editor unit-test project exists)**

- [ ] **Step 5: Commit (conditional)**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Models src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs
git commit -m "feat: two-state save endpoints — isActive flag, no create version, inherit on restore/duplicate"
```

---

### Task 6: Edit page UI + JS — two buttons, version badges, no workflow/autosave

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml`
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/_VersionHistory.cshtml`
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js`
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css` (small additions)

**Interfaces:**
- Consumes: `window.tbLatestVersionIsActive` (set by Edit.cshtml from `Model.LatestVersionIsActive`); `SaveVersionRequest.IsActive` (Task 5).
- Produces: `btn-save-draft` (secondary) + `btn-save-version` (primary) always visible in edit mode; "Draft version" badge next to `#version-display` when the latest version is a draft; version-history cards show Active/Draft badges; no workflow/autosave UI remains.

- [ ] **Step 1: Edit.cshtml footer + script**

Replace the edit-mode footer block:

```html
@if (Model.Id.HasValue)
{
    <button type="button" id="btn-preview" class="btn btn-secondary">Preview</button>
    <button type="button" id="btn-health" class="btn btn-secondary">Health</button>
    <button type="button" id="btn-save-version" class="btn btn-primary">Save Version</button>
    <div id="health-panel" class="tb-health-panel" hidden>
        <div id="health-findings"></div>
        <div id="health-meta" class="tb-health-meta"></div>
    </div>
}
```

with:

```html
@if (Model.Id.HasValue)
{
    <button type="button" id="btn-preview" class="btn btn-secondary">Preview</button>
    <button type="button" id="btn-health" class="btn btn-secondary">Health</button>
    <button type="button" id="btn-save-draft" class="btn btn-secondary">Save Draft</button>
    <button type="button" id="btn-save-version" class="btn btn-primary">Save Version</button>
    <div id="health-panel" class="tb-health-panel" hidden>
        <div id="health-findings"></div>
        <div id="health-meta" class="tb-health-meta"></div>
    </div>
}
```

Next to the version display, add the draft badge — replace:

```html
<span id="version-display">v @Model.CurrentVersionNumber</span>
```

with:

```html
<span id="version-display">v @Model.CurrentVersionNumber</span>
@if (Model.Id.HasValue && !Model.LatestVersionIsActive)
{
    <span id="draft-version-badge" class="tb-badge tb-badge-draft">Draft version</span>
}
```

- [ ] **Step 2: _VersionHistory.cshtml badges**

Inside the version loop, after the existing `@if (isCurrent) { ... Current ... }` badge, add:

```html
<span class="tb-badge @(version.IsActive ? "tb-badge-live" : "tb-badge-draft")">@(version.IsActive ? "Active" : "Draft")</span>
```

- [ ] **Step 3: JS — saveVersion(isActive) + button wiring + status refresh**

Change `async function saveVersion()` to `async function saveVersion(isActive)` and add `isActive` to the JSON body (after `sourceView`):

```javascript
sourceView: document.getElementById('prop-source-view')?.value ?? '',
isActive,
body,
```

After a successful save, update the badge. The server includes `isActive` in the SaveVersion response (see Task 5's `JsonOk` note below), so:

```javascript
document.getElementById('version-display').textContent = `v${data.versionNumber}`;
const existing = document.getElementById('draft-version-badge');
if (data.isActive) { if (existing) existing.remove(); }
else if (!existing) {
    const b = document.createElement('span');
    b.id = 'draft-version-badge';
    b.className = 'tb-badge tb-badge-draft';
    b.textContent = 'Draft version';
    document.getElementById('version-display').after(b);
}
```

And in the controller (`SaveVersion`), change the success payload to: `return JsonOk(new { versionId = published.Id, versionNumber = published.VersionNumber, isActive = published.IsActive });`

Wire the buttons:

```javascript
document.getElementById('btn-save-draft')?.addEventListener('click', () => saveVersion(false));
document.getElementById('btn-save-version')?.addEventListener('click', () => saveVersion(true));
```

(Remove the old direct `btn-save-version` listener that called `saveVersion()` — check the existing wiring around line 700 and make the two lines above the only bindings.)

- [ ] **Step 4: CSS**

Append to `template-editor.css` (tokens already exist — `--surface2`, `--text-muted`, `--danger`, `--success`):

```css
/* ── 37. Two-state save ── */
#tb-editor-host #draft-version-badge { margin-left: 6px; vertical-align: middle; }
#tb-editor-host .tb-version-header .tb-badge { margin-left: 6px; }
```

- [ ] **Step 5: Syntax + build**

Run: `node --check src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (Expected: exit 0, no output) then `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo` (Expected: 0 errors; codegen regenerates Edit.cshtml + _VersionHistory.cshtml — `grep -rl "btn-save-draft" src/TemplateBuilder.Editor.Mvc5/obj/CodeGen/` must return `Views/Templates/Edit.cshtml.cs`).

- [ ] **Step 6: Commit (conditional)**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Views/Templates src/TemplateBuilder.Editor.Mvc5/StaticAssets src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs
git commit -m "feat: Save Draft / Save Version buttons and per-version badges in the editor"
```

---

### Task 7: Version 1.2.0 + docs

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj` (`<Version>1.1.0</Version>` → `<Version>1.2.0</Version>`)
- Modify: `src/TemplateBuilder.Editor.Mvc5/README.md`
- Modify: `PROGRESS.md`, `MEMORY.md`

- [ ] **Step 1: Bump the version**

In `TemplateBuilder.Editor.Mvc5.csproj` line 78: `<Version>1.2.0</Version>`.

- [ ] **Step 2: README**

- In the feature table remove the workflow rows (`Submit for review`, `Approve`, `Reject (with feedback)`, `Cancel review`, `Publish`, `Save server-side draft`) and add `| Save draft version | POST /Templates/{id}/SaveVersion (isActive:false) |` above the Save version row; note `GET /Templates/{id}/Health` rows stay.
- Replace the **Governance & Compliance → Template workflow** section with a **Two-state saves** section: per-version Draft/Active, editor shows latest, render API serves last Active with `TemplateNotFoundException`/`TemplateInactiveException`/`NoActiveVersionException`, template `IsActive` = servable switch.
- Update the **What's New** with a `#### v1.2.0` block: two-state save model; workflow removed (breaking); promotion format schemaVersion 2 (breaking); render API now throws typed exceptions and serves the last active version.

- [ ] **Step 3: PROGRESS.md + MEMORY.md**

PROGRESS.md: append a gate table for Tasks 1–8 with actual command outputs. MEMORY.md: durable facts — version-level IsActive semantics, render API exception contract, export schemaVersion 2 (accepts only 2), create produces no version, restore/duplicate inherit flags, migration names, "save draft = version" (no autosave).

- [ ] **Step 4: Commit (conditional)**

```bash
git add src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj src/TemplateBuilder.Editor.Mvc5/README.md PROGRESS.md MEMORY.md
git commit -m "docs: v1.2.0 — two-state save model docs, progress gates, memory"
```

---

### Task 8: End-to-end verification (pack → sample host → xsp4 → agent-browser)

**Files:** none (verification only; fixes land in whatever task owns the broken file)

- [ ] **Step 1: Full solution build + all suites** (xsp4 stopped)

Run: `dotnet build TemplateBuilder.Mvc5.sln --nologo` then Domain, Application, EF6 suites.
Expected: build 0 errors; Domain green; Application green (77 incl. engine/promotion suites); EF6 green (41).

- [ ] **Step 2: Pack + reinstall + xbuild + restart** (recipe in Global Constraints; package version 1.2.0; the sample host's 4 HintPaths move from `1.1.0` to `1.2.0`, and the old `packages/TemplateBuilder.Editor.Mvc5.1.1.0` folder is deleted)

- [ ] **Step 3: Curl smoke** (token/cookie pattern from PROGRESS.md)

Check, in order:
1. `POST /Templates/Create {"name":"TwoState Smoke","templateType":"Email"}` → `{"templateId":N}` and NO version rows (sqlcmd: `SELECT COUNT(*) FROM dbo.TemplateVersions WHERE TemplateId=N` = 0).
2. `POST /Templates/{id}/SaveVersion` body without `isActive` → defaults true; response `{"versionNumber":1,...,"isActive":true}`.
3. `POST /Templates/{id}/SaveVersion` with `"isActive":false` → v2, `"isActive":false`.
4. `POST /Templates/{id}/SaveVersion` with `"isActive":true` → v3 active.
5. `GET /Templates/{id}/Versions` partial contains v1 Active, v2 Draft, v3 Active badges.
6. `GET /Templates/{id}/Edit` shows `Draft version` badge absent (latest v3 active); then save v4 draft and verify the badge appears.
7. Workflow routes are GONE: `POST /Templates/{id}/SubmitForReview` → 404; `/Approve`, `/Reject`, `/CancelReview`, `/Publish`, `/Draft` → 404.
8. `GET /Templates/Export/{id}` → 200 attachment with `"schemaVersion": 2` and per-version `isActive`; re-import via `POST /Templates/Import -F file=@export.json` → updated, versions appended with flags preserved (check via Versions partial).
9. `GET /Templates/{id}/Health` still 200 (unbound warning); `GET /Health` 200; bulk toggle still works.
10. `GET /Audit` shows `draft_saved` + `published` rows and NO new `submitted`/`approved` rows.
11. Restore: `POST /Templates/{id}/Restore/2/2` (draft source) → new version inherits Draft; `POST /Templates/{id}/Duplicate` → new template's v1 inherits the latest version's flag.
12. Developer API (unit-tested in Task 2; optional smoke): a tiny console harness referencing the packaged DLLs renders last-active despite newer draft, and throws `TemplateInactiveException`/`NoActiveVersionException` — or rely on the Task 2 test evidence and note it.

- [ ] **Step 4: agent-browser flows** (recipes in MEMORY.md; screenshots to `/tmp/opencode/twostate-*.png`)

1. Edit page shows Save Draft + Save Version and NO status pill / workflow buttons / lock banner; click Save Draft → toast + "Draft version" badge; open History → v1 shows Draft badge.
2. Click Save Version → badge disappears; History shows Active badge on the new version; Edit reload keeps showing latest (draft included when re-saving as draft).
3. Create flow: Create form → lands on Edit with no version row (history empty state) and both buttons enabled.
4. Index page unchanged (toggles, health badges, bulk bar, import modal).

- [ ] **Step 5: Fix forward**

Any failures: return the fix to the owning task (TDD: add a failing test first), re-run Steps 1–4. Record evidence in PROGRESS.md.

---

## Self-review notes (fixes applied while writing)

- Spec coverage: D1–D13 map to Tasks 1–8 (D1–D3 → 1/2; D4 → 6; D5/D6 → 5/6; D7/D12 → 4; D8 → 5; D9 → 5; D10/D11 → 3; D13 → 7). Module 1–5 of the spec each map to ≥1 task; Out-of-scope items have no tasks (correct).
- Task ordering keeps the solution compiling at every gate: Task 1–3 additive, Task 4 is the single big removal (compiler enumerates every reference), Tasks 5–6 change behavior/UI on the surviving surface.
- `TemplateEngineTests` uses Moq (not NSubstitute) — Task 2 rewrites the three existing draft-contract tests (`RenderAsync_renders_current_version_body`, `RenderAsync_renders_empty_when_template_has_no_versions`, `RenderByNameAsync_renders_current_version_body`) because the new contract changes their expectations; the helper `CreateEngine(Mock<ITemplateRepository>? repo = null)` is reused verbatim.
- `RestoreVersion`'s flag lookup uses `GetVersionHistoryAsync` (versions include `IsActive`) instead of adding a new repository method — smallest change.
- Task 3's `TemplateImportResult.Skipped` stays in the DTO; the editor import-result UI keeps its four renderEntry kinds — no UI change needed.
- Mono-crash re-run rule, xsp4-stop-before-EF6 rule, and the `grep -c` exit-code trap are in Global Constraints (learned the hard way in the lifecycle phase).
