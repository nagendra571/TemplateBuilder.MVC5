# Lifecycle & Ops (TemplateBuilder.Editor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add dev→prod promotion (versioned JSON export/import with stable external-key identity), template health checking (field drift vs live SQL view schema), and bulk operations (activate/deactivate/export/delete) to TemplateBuilder.Editor.

**Architecture:** New Application services (`ITemplatePromotionService`, `ITemplateHealthService`) over a new Domain interface (`ITemplatePromotionRepository`, EF Core implementation) plus two `ITemplateRepository` additions (`DeleteAsync`, `GetAllIncludingInactiveAsync`). Three new `Template` columns (`ExternalKey` unique Guid, `SourceView`, `SourceViewSnapshot`). Controllers expose System.Text.Json endpoints; RCL views + `wwwroot` assets get a bulk toolbar, health badges, a Health page, an import modal, and editor health integration. Requires the two-state save model (per-version `IsActive`) — the export format carries it.

**Tech Stack:** .NET 8 / .NET 10 multi-target, ASP.NET Core MVC (Razor RCL), EF Core 8/10 SqlServer, System.Text.Json, Scriban 7.2.6 (AST), System.IO.Compression (shared framework), xUnit + Moq + FluentAssertions, InMemory EF for repo tests.

**Spec:** `docs/superpowers/specs/2026-08-21-origin-lifecycle-ops-design.md` — decisions L1–L14 are quoted from there.

## Global Constraints

- Repo: `github.com/nagendra571/TemplateBuilder` (private), branch `main`. `git pull` first; work from the repo root.
- Build: `dotnet build TemplateBuilder.slnx` — 0 errors on both TFMs (net8.0 + net10.0).
- Tests: run the four test projects individually (`dotnet test tests/TemplateBuilder.Application.Tests`, `tests/TemplateBuilder.Editor.Tests`, `tests/TemplateBuilder.Infrastructure.Tests`, `tests/TemplateBuilder.Domain.Tests`); never concurrently.
- JSON: System.Text.Json only — camelCase via `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }` for file/blob serialization; `Ok(...)`/`[FromBody]` for endpoints.
- Antiforgery: `[ValidateAntiForgeryToken]` + the JS `RequestVerificationToken` header (native). Import (multipart `[FromForm] IFormFile`) also carries the header.
- Views/assets: RCL `.cshtml` + `wwwroot` edited directly; all CSS scoped `#tb-editor-host`; reuse existing token classes (`tb-badge-live`/`tb-badge-draft`, `btn btn-secondary`/`btn btn-primary`, modal classes).
- EF Core migrations: `dotnet ef migrations add <Name> --project src/TemplateBuilder.Infrastructure`; `MigrationHostedService` applies at startup; InMemory tests bypass migrations (schema from the model — migration validity is verified at e2e on a fresh DB).
- e2e host: `src/TemplateBuilder.Web` at `https://localhost:7275/`; `GET /Templates/_setup` diagnostics.
- Version: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` → `2.1.0` (or fold into 2.0.0 if released with the two-state feature — owner decides at release; this plan assumes 2.1.0). README "What's New" must be in sync (repo lesson).
- Commits: conventional style; only what each task lists; pushes approved separately.
- Reference implementation: the fork's lifecycle phase (`github.com/nagendra571/TemplateBuilder.MVC5`, commits `fbc1e54`..`46fc1f9`) — consult for exact algorithms (Scriban AST walk, import upsert, ZIP packaging) and adapt per the spec's stack-mapping table (EF Core LINQ, System.Text.Json, RCL views, MS DI). **The fork repo is private — if you don't have access, the fully-embedded tests in each task plus the spec's Module rules are the complete contract; the implementations follow standard patterns (System.Text.Json, EF Core LINQ, ASP.NET Core MVC).**
- Also required: the two-state save model (spec `2026-08-21-origin-two-state-save-design.md`, plan `2026-08-21-origin-two-state-save-implementation.md`) — `TemplateVersion.IsActive` must exist before this plan starts (Task 2's export format carries it).
- Do NOT touch: autosave, Create behavior, snippets, authorization, the two-state render contract (post-two-state), `TemplateBuilder.Core` (L12).

---

### Task 1: Domain + EF Core foundation (ExternalKey, SourceView, Snapshot, DeleteAsync, GetAllIncludingInactiveAsync)

**Files:**
- Modify: `src/TemplateBuilder.Domain/Entities/Template.cs`
- Modify: `src/TemplateBuilder.Domain/Interfaces/ITemplateRepository.cs`
- Modify: `src/TemplateBuilder.Infrastructure/Data/Configurations/TemplateConfiguration.cs`
- Modify: `src/TemplateBuilder.Infrastructure/Repositories/TemplateRepository.cs`
- Create: `src/TemplateBuilder.Infrastructure/Migrations/<timestamp>_AddLifecycleOps.cs` (+ Designer; scaffolded + hand-edited)
- Modify: `tests/TemplateBuilder.Infrastructure.Tests/Repositories/TemplateRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `Template.ExternalKey` — `Guid`, non-nullable, unique index `IX_Templates_ExternalKey`.
  - `Template.SourceView` — `string?`, max 200; `Template.SourceViewSnapshot` — `string?`, nvarchar(max).
  - `ITemplateRepository.DeleteAsync(int id, CancellationToken ct = default)` → `Task<bool>` (true = deleted, false = not found).
  - `ITemplateRepository.GetAllIncludingInactiveAsync(CancellationToken ct = default)` → `Task<IReadOnlyList<Template>>` (all templates, ordered by Name).

- [ ] **Step 1: Write the failing tests**

`TemplateRepositoryTests.cs` (InMemory `CreateContext()` helper exists):

```csharp
[Fact]
public async Task CreateAsync_AssignsNonEmptyExternalKey()
{
    await using var context = CreateContext();
    var repo = new TemplateRepository(context);
    var t = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
    t.ExternalKey.Should().NotBe(Guid.Empty);
}

[Fact]
public async Task ExternalKeys_AreUniquePerRow()
{
    await using var context = CreateContext();
    var repo = new TemplateRepository(context);
    var a = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
    var b = await repo.CreateAsync(new Template { Name = "B", TemplateType = "Email" });
    a.ExternalKey.Should().NotBe(b.ExternalKey);
}

[Fact]
public async Task DeleteAsync_RemovesTemplateAndVersions_ReturnsTrueThenFalse()
{
    await using var context = CreateContext();
    var repo = new TemplateRepository(context);
    var t = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
    await repo.PublishVersionAsync(t.Id, new TemplateVersion { Body = "<p>v1</p>" });

    (await repo.DeleteAsync(t.Id)).Should().BeTrue();
    (await repo.GetByIdAsync(t.Id)).Should().BeNull();
    (await repo.GetVersionHistoryAsync(t.Id)).Should().BeEmpty();
    (await repo.DeleteAsync(t.Id)).Should().BeFalse();
}

[Fact]
public async Task GetAllIncludingInactiveAsync_IncludesInactiveTemplates()
{
    await using var context = CreateContext();
    var repo = new TemplateRepository(context);
    await repo.CreateAsync(new Template { Name = "Off", TemplateType = "Email", IsActive = false });
    await repo.CreateAsync(new Template { Name = "On", TemplateType = "Email", IsActive = true });

    var all = await repo.GetAllIncludingInactiveAsync();

    all.Select(t => t.Name).Should().BeEquivalentTo("Off", "On");
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.Tests`
Expected: FAIL — `ExternalKey`/`DeleteAsync`/`GetAllIncludingInactiveAsync` missing.

- [ ] **Step 3: Implement**

`Template.cs` — add after `Description`:

```csharp
public Guid ExternalKey { get; set; } = Guid.NewGuid();
public string? SourceView { get; set; }
public string? SourceViewSnapshot { get; set; }
```

`ITemplateRepository.cs` — add:

```csharp
Task<bool> DeleteAsync(int id, CancellationToken ct = default);
Task<IReadOnlyList<Template>> GetAllIncludingInactiveAsync(CancellationToken ct = default);
```

`TemplateConfiguration.cs` — add inside `Configure`:

```csharp
builder.Property(t => t.ExternalKey).IsRequired();
builder.HasIndex(t => t.ExternalKey).IsUnique();
builder.Property(t => t.SourceView).HasMaxLength(200);
builder.Property(t => t.SourceViewSnapshot).HasColumnType("nvarchar(max)");
```

`TemplateRepository.cs`:

```csharp
public async Task<IReadOnlyList<Template>> GetAllIncludingInactiveAsync(CancellationToken ct = default) =>
    await _context.Templates
        .AsNoTracking()
        .Include(t => t.CurrentVersion)
        .OrderBy(t => t.Name)
        .ToListAsync(ct);

public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
{
    var template = await _context.Templates.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (template is null) return false;

    var versions = await _context.TemplateVersions.Where(v => v.TemplateId == id).ToListAsync(ct);
    _context.TemplateVersions.RemoveRange(versions);
    _context.Templates.Remove(template);
    await _context.SaveChangesAsync(ct);
    return true;
}
```

(Delete order matters: versions first — FK `NoAction` on `TemplateId`; `CurrentVersionId` FK is `SetNull`, handled by EF Core on delete. If the InMemory provider complains about the row-version shim during delete, note it — SqlServer does not.)

`CreateAsync` — add the Guid guard before save:

```csharp
if (template.ExternalKey == Guid.Empty)
    template.ExternalKey = Guid.NewGuid();
```

- [ ] **Step 4: Scaffold the migration**

Run: `dotnet ef migrations add AddLifecycleOps --project src/TemplateBuilder.Infrastructure`
Hand-edit `Up()` to add the NEWID() backfill and the unique index (the scaffolder emits the three AddColumns with `defaultValue: new Guid("00000000-...")`):

```csharp
migrationBuilder.Sql("UPDATE dbo.Templates SET ExternalKey = NEWID() WHERE ExternalKey = '00000000-0000-0000-0000-000000000000'");
migrationBuilder.CreateIndex(
    name: "IX_Templates_ExternalKey",
    table: "Templates",
    column: "ExternalKey",
    unique: true);
```

- [ ] **Step 5: Run — verify green**

Run the Step 2 command. Expected: PASS (4 new tests). Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Domain src/TemplateBuilder.Infrastructure tests/TemplateBuilder.Infrastructure.Tests
git commit -m "feat: lifecycle-ops data foundation (ExternalKey, SourceView, snapshot, delete)"
```

---

### Task 2: Promotion repository + service (export/import v2 + bulk ZIP)

**Files:**
- Create: `src/TemplateBuilder.Domain/Interfaces/ITemplatePromotionRepository.cs`
- Create: `src/TemplateBuilder.Infrastructure/Repositories/TemplatePromotionRepository.cs`
- Create: `src/TemplateBuilder.Application/Services/ITemplatePromotionService.cs` (DTOs + interface)
- Create: `src/TemplateBuilder.Application/Services/TemplatePromotionService.cs`
- Create: `tests/TemplateBuilder.Infrastructure.Tests/Repositories/TemplatePromotionRepositoryTests.cs`
- Create: `tests/TemplateBuilder.Application.Tests/Services/TemplatePromotionServiceTests.cs`, `TemplatePromotionImportTests.cs`, `TemplatePromotionBulkZipTests.cs`

**Interfaces:**
- Consumes: `Template.ExternalKey`/`TemplateVersion.IsActive` (T1/two-state), `ITemplateRepository.GetByIdAsync`/`GetVersionHistoryAsync`/`DeleteAsync`.
- Produces (exact shapes — spec Module 1):
  - `ITemplatePromotionRepository`: `GetByExternalKeyAsync(Guid, ct)`, `AddWithVersionsAsync(Template, IReadOnlyList<TemplateVersion>, ct)` (preserves original version numbers; sets CurrentVersionId to the last), `UpdateFromImportAsync(Template, IReadOnlyList<TemplateVersion>, ct)` (sets UpdatedAt, appends versions from max+1, single SaveChanges), `GetMaxVersionNumberAsync(int, ct)`.
  - `ITemplatePromotionService`: `BuildExportAsync(int, ct)` → `TemplateExportDocument?`; `SerializeExport(doc)` → camelCase indented JSON (schemaVersion 2); `SanitizeFileName(name)`; `ImportAsync(byte[] fileBytes, string actor, ct)` → `TemplateImportResult`; `BuildBulkZipAsync(IReadOnlyList<int>, ct)` → `byte[]`.
  - DTOs: `TemplateExportVersion { VersionNumber, Body, ChangeComment?, CreatedAt, CreatedBy?, IsActive }`; `TemplateExportTemplate { ExternalKey, Name, TemplateType, Description?, IsActive, Versions }`; `TemplateExportDocument { SchemaVersion = 2, Exporter, ExportedAt, Template }`; `ExporterInfo { Name = "TemplateBuilder.Editor", Version = "2.1.0" }`; `TemplateImportEntry { Name?, Reason?, ExternalKey, VersionsAppended }`; `TemplateImportResult { Created, Updated, Skipped, Errors }`.
  - Bulk ZIP: per-template `{Sanitized}.template.json` + `_summary.json` (`{ schemaVersion = 2, exportedAt, files: [{ id, name, status }] }`).

- [ ] **Step 1: Write the failing tests**

`TemplatePromotionRepositoryTests.cs` (new file — copy the private `CreateContext()` InMemory helper verbatim from `TemplateRepositoryTests.cs`; add the same `using Microsoft.EntityFrameworkCore;`):

```csharp
[Fact]
public async Task AddWithVersionsAsync_PreservesOriginalVersionNumbers()
{
    await using var context = CreateContext();
    var repo = new TemplatePromotionRepository(context);
    var t = await repo.AddWithVersionsAsync(
        new Template { Name = "P", TemplateType = "Email", ExternalKey = Guid.NewGuid() },
        new List<TemplateVersion>
        {
            new() { VersionNumber = 1, Body = "<p>one</p>" },
            new() { VersionNumber = 2, Body = "<p>two</p>" }
        });
    var history = await context.TemplateVersions.Where(v => v.TemplateId == t.Id).ToListAsync();
    history.Select(v => v.VersionNumber).Should().Equal(1, 2);
    t.CurrentVersionId.Should().Be(history.Single(v => v.VersionNumber == 2).Id);
}

[Fact]
public async Task UpdateFromImportAsync_AppendsVersionsFromMaxPlusOne()
{
    await using var context = CreateContext();
    var repo = new TemplatePromotionRepository(context);
    var t = await repo.AddWithVersionsAsync(
        new Template { Name = "P", TemplateType = "Email", ExternalKey = Guid.NewGuid() },
        new List<TemplateVersion> { new() { VersionNumber = 1, Body = "one" } });

    var assigned = await repo.UpdateFromImportAsync(t, new List<TemplateVersion>
    {
        new() { Body = "imported a", IsActive = false },
        new() { Body = "imported b", IsActive = true }
    });

    assigned.Should().Equal(2, 3);
    var history = await context.TemplateVersions.Where(v => v.TemplateId == t.Id).OrderBy(v => v.VersionNumber).ToListAsync();
    history[1].IsActive.Should().BeFalse();
    history[2].IsActive.Should().BeTrue();
}

[Fact]
public async Task GetByExternalKeyAsync_RoundTrips()
{
    await using var context = CreateContext();
    var repo = new TemplatePromotionRepository(context);
    var key = Guid.NewGuid();
    await repo.AddWithVersionsAsync(new Template { Name = "P", TemplateType = "Email", ExternalKey = key }, new List<TemplateVersion>());
    (await repo.GetByExternalKeyAsync(key)).Should().NotBeNull();
    (await repo.GetByExternalKeyAsync(Guid.NewGuid())).Should().BeNull();
}
```

`TemplatePromotionServiceTests.cs`:

```csharp
[Fact]
public async Task BuildExportAsync_ShapesDocument_WithOrderedVersions_AndFlags()
{
    var repo = new Mock<ITemplateRepository>();
    var promo = new Mock<ITemplatePromotionRepository>();
    repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 7, ExternalKey = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Name = "Invoice", TemplateType = "Email", IsActive = true
    });
    repo.Setup(r => r.GetVersionHistoryAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TemplateVersion>
    {
        new() { VersionNumber = 2, Body = "<p>two</p>", IsActive = false },
        new() { VersionNumber = 1, Body = "<p>one</p>", IsActive = true }
    });
    var svc = new TemplatePromotionService(repo.Object, promo.Object);

    var doc = await svc.BuildExportAsync(7);

    doc.Should().NotBeNull();
    doc!.SchemaVersion.Should().Be(2);
    doc.Exporter.Name.Should().Be("TemplateBuilder.Editor");
    doc.Template.Versions.Select(v => v.VersionNumber).Should().Equal(1, 2);
    doc.Template.Versions.Select(v => v.IsActive).Should().Equal(true, false);
    var json = svc.SerializeExport(doc);
    json.Should().Contain("\"schemaVersion\"");
    json.Should().Contain("\"externalKey\"");
}

[Theory]
[InlineData("Invoice v3", "Invoice_v3")]
[InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
public void SanitizeFileName_StripsInvalidChars(string input, string expected)
    => new TemplatePromotionService(new Mock<ITemplateRepository>().Object, new Mock<ITemplatePromotionRepository>().Object)
        .SanitizeFileName(input).Should().Be(expected);
```

`TemplatePromotionImportTests.cs`:

```csharp
private static TemplatePromotionService Create(
    Mock<ITemplateRepository>? repo = null, Mock<ITemplatePromotionRepository>? promo = null)
    => new(repo?.Object ?? new Mock<ITemplateRepository>().Object,
           promo?.Object ?? new Mock<ITemplatePromotionRepository>().Object);

[Fact]
public async Task Import_RejectsNonSchema2File()
{
    var svc = Create();
    var json = """{"schemaVersion":1,"template":{"name":"X"}}""";
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(json), "bob");
    result.Errors.Should().ContainSingle(e => e.Reason!.Contains("schemaVersion"));
}

[Fact]
public async Task Import_CreatesTemplate_PreservingFlags()
{
    var promo = new Mock<ITemplatePromotionRepository>();
    var key = Guid.NewGuid();
    promo.Setup(p => p.GetByExternalKeyAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync((Template?)null);
    Template? captured = null;
    promo.Setup(p => p.AddWithVersionsAsync(It.IsAny<Template>(), It.IsAny<IReadOnlyList<TemplateVersion>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((Template t, IReadOnlyList<TemplateVersion> _, CancellationToken _) => { captured = t; return t; });
    var svc = Create(promo: promo);
    var doc = new TemplateExportDocument
    {
        Template = new TemplateExportTemplate
        {
            ExternalKey = key, Name = "X", TemplateType = "Email", IsActive = false,
            Versions = { new() { VersionNumber = 1, Body = "<p>ok</p>", IsActive = true }, new() { VersionNumber = 2, Body = "<p>d</p>", IsActive = false } }
        }
    };

    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");

    result.Created.Should().ContainSingle(c => c.Name == "X");
    captured!.IsActive.Should().BeFalse();
    captured.ExternalKey.Should().Be(key);
}

[Fact]
public async Task Import_UpdatesExisting_PreservingVersionFlags()
{
    var promo = new Mock<ITemplatePromotionRepository>();
    var key = Guid.NewGuid();
    var existing = new Template { Id = 9, Name = "Old", TemplateType = "Email", IsActive = true };
    promo.Setup(p => p.GetByExternalKeyAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    IReadOnlyList<TemplateVersion>? captured = null;
    promo.Setup(p => p.UpdateFromImportAsync(existing, It.IsAny<IReadOnlyList<TemplateVersion>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((Template _, IReadOnlyList<TemplateVersion> vs, CancellationToken _) => { captured = vs; return vs.Select(v => v.VersionNumber).ToArray(); });
    var svc = Create(promo: promo);
    var doc = new TemplateExportDocument
    {
        Template = new TemplateExportTemplate
        {
            ExternalKey = key, Name = "X", TemplateType = "Email", IsActive = true,
            Versions = { new() { VersionNumber = 1, Body = "<p>a</p>", IsActive = false } }
        }
    };

    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");

    result.Updated.Should().ContainSingle(u => u.Name == "X");
    result.Skipped.Should().BeEmpty();
    captured!.Single().IsActive.Should().BeFalse();
}

[Fact]
public async Task Import_RejectsInvalidScribanBody()
{
    var svc = Create();
    var doc = new TemplateExportDocument
    {
        Template = new TemplateExportTemplate
        {
            ExternalKey = Guid.NewGuid(), Name = "X", TemplateType = "Email",
            Versions = { new() { VersionNumber = 1, Body = "{{ end }}" } }
        }
    };
    var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
    result.Errors.Should().ContainSingle(e => e.Reason!.Contains("Version 1"));
}
```

`TemplatePromotionBulkZipTests.cs`:

```csharp
[Fact]
public async Task BulkZipAsync_ContainsPerTemplateFiles_AndSummaryManifest()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 1, ExternalKey = Guid.NewGuid(), Name = "Invoice v3", TemplateType = "Email"
    });
    repo.Setup(r => r.GetVersionHistoryAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<TemplateVersion> { new() { VersionNumber = 1, Body = "<p>one</p>", IsActive = true } });
    repo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync((Template?)null);
    var svc = new TemplatePromotionService(repo.Object, new Mock<ITemplatePromotionRepository>().Object);

    var zip = await svc.BuildBulkZipAsync(new[] { 1, 2 });

    using var ms = new MemoryStream(zip);
    using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
    archive.Entries.Select(e => e.Name).Should().Contain("Invoice_v3.template.json");
    archive.Entries.Select(e => e.Name).Should().Contain("_summary.json");
    using var sr = new StreamReader(archive.GetEntry("_summary.json")!.Open());
    var summary = sr.ReadToEnd();
    summary.Should().Contain("\"schemaVersion\": 2");
    summary.Should().Contain("not found");
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.Tests --filter "FullyQualifiedName~TemplatePromotion"` and `dotnet test tests/TemplateBuilder.Application.Tests --filter "FullyQualifiedName~TemplatePromotion"`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement**

Port from the fork's `TemplatePromotionService`/`TemplatePromotionRepository` (spec's reference implementation) with these adaptations:
- **System.Text.Json instead of Newtonsoft** — one shared `JsonSerializerOptions`:

```csharp
private static readonly JsonSerializerOptions CamelJson = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
};
```

- `SerializeExport` → `JsonSerializer.Serialize(document, CamelJson)`; import parse → `JsonSerializer.Deserialize<TemplateExportDocument>(Encoding.UTF8.GetString(fileBytes), CamelJson)`.
- ZIP via `System.IO.Compression` (no package).
- EF Core LINQ in the repository (`FirstOrDefaultAsync`, `MaxAsync`, `ToListAsync`).
- No `CollapseStatus`, no locked-skip (spec L3).

- [ ] **Step 4: Run — verify green**

Run the Step 2 commands. Expected: PASS. Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Domain/Interfaces/ITemplatePromotionRepository.cs src/TemplateBuilder.Infrastructure/Repositories/TemplatePromotionRepository.cs src/TemplateBuilder.Application/Services tests/TemplateBuilder.Application.Tests/Services tests/TemplateBuilder.Infrastructure.Tests/Repositories
git commit -m "feat: promotion service — export/import v2 and bulk ZIP"
```

---

### Task 3: Health check engine (Application)

**Files:**
- Create: `src/TemplateBuilder.Application/Services/ITemplateHealthService.cs` (DTOs + interface)
- Create: `src/TemplateBuilder.Application/Services/TemplateHealthService.cs`
- Create: `tests/TemplateBuilder.Application.Tests/Services/TemplateHealthServiceTests.cs`

**Interfaces:**
- Consumes: `ITemplateRepository` (existing), `ISqlViewDiscoveryService.GetViewColumnsAsync` (returns `IReadOnlyList<SqlColumnInfo>` with `Name/DataType/MaxLength/IsNullable`), Scriban AST (`Template.Page.Children`, `ScriptNode.Children`, `ScriptMemberExpression.Target/.Member`, `ScriptVariable.Name` — the fork's verified member names; use `Scriban.Template.Parse`, NOT `Template.Parse` to avoid the entity-name clash).
- Produces:
  - `HealthSeverity { Info = 0, Warning = 1, Critical = 2 }`; `HealthFinding { Severity, Code, Message }`; `TemplateHealthReport { TemplateId, SourceView?, ViewMissing, Tokens, Findings, SnapshotTakenAt?, Worst }`.
  - `ITemplateHealthService`: `CheckAsync(int templateId, ct)` → `TemplateHealthReport`; `ExtractModelPathsAsync(string body, ct)` → `IReadOnlyList<string>`; `BuildSnapshotJsonAsync(string viewName, ct)` → `string` (`{ takenAt, columns }` camelCase).

- [ ] **Step 1: Write the failing tests** (port from the fork's `TemplateHealthServiceTests` — same 5 tests, Moq instead of NSubstitute, System.Text.Json for the snapshot fixture serialization)

```csharp
[Fact]
public async Task Extract_HandlesNestedLoopsConditionals_AndIgnoresLiterals()
{
    var svc = new TemplateHealthService(new Mock<ITemplateRepository>().Object, new Mock<ISqlViewDiscoveryService>().Object);
    const string body = """
<p>{{ model.FirstName }} {{ model.User.Name }}</p>
{{ for item in model.Items }}{{ item.Qty }}{{ end }}
{{ if model.HasDiscount }}yes{{ end }}
{{ "literal model.Nope" }} {{ 'model.Single' }}
""";
    var paths = await svc.ExtractModelPathsAsync(body);
    paths.Should().BeEquivalentTo("FirstName", "User.Name", "Items", "HasDiscount");
}

[Fact]
public async Task Check_ReportsMissingView_AndMissingColumn()
{
    var repo = new Mock<ITemplateRepository>();
    var discovery = new Mock<ISqlViewDiscoveryService>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 1, Name = "T", SourceView = "v_Gone",
        SourceViewSnapshot = JsonSerializer.Serialize(new { takenAt = DateTime.UtcNow, columns = new List<SqlColumnInfo> { new() { Name = "FirstName", DataType = "nvarchar", MaxLength = 100, IsNullable = false } } }),
        CurrentVersion = new TemplateVersion { Body = "<p>{{ model.FirstName }}</p><p>{{ model.Nope }}</p>" }
    });
    discovery.Setup(d => d.GetViewColumnsAsync("v_Gone", It.IsAny<CancellationToken>())).ReturnsAsync(new List<SqlColumnInfo>());
    var svc = new TemplateHealthService(repo.Object, discovery.Object);

    var report = await svc.CheckAsync(1);

    report.ViewMissing.Should().BeTrue();
    report.Findings.Should().Contain(f => f.Code == "view_missing" && f.Severity == HealthSeverity.Critical);
}

[Fact]
public async Task Check_ReportsTypeAndLengthDrift_FromSnapshot()
{
    var repo = new Mock<ITemplateRepository>();
    var discovery = new Mock<ISqlViewDiscoveryService>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 1, Name = "T", SourceView = "v_Cust",
        SourceViewSnapshot = JsonSerializer.Serialize(new { takenAt = DateTime.UtcNow, columns = new List<SqlColumnInfo> { new() { Name = "CustomerName", DataType = "nvarchar", MaxLength = 100, IsNullable = true } } }),
        CurrentVersion = new TemplateVersion { Body = "<p>{{ model.CustomerName }}</p>" }
    });
    discovery.Setup(d => d.GetViewColumnsAsync("v_Cust", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<SqlColumnInfo> { new() { Name = "CustomerName", DataType = "nvarchar", MaxLength = 500, IsNullable = false } });
    var svc = new TemplateHealthService(repo.Object, discovery.Object);

    var report = await svc.CheckAsync(1);

    report.Findings.Should().Contain(f => f.Code == "column_length_changed" && f.Severity == HealthSeverity.Warning);
    report.Findings.Should().Contain(f => f.Code == "column_nullability_changed" && f.Severity == HealthSeverity.Warning);
}

[Fact]
public async Task Check_ReportsTypeChange_WithoutRedundantLengthFinding()
{
    var repo = new Mock<ITemplateRepository>();
    var discovery = new Mock<ISqlViewDiscoveryService>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 1, Name = "T", SourceView = "v_C",
        SourceViewSnapshot = JsonSerializer.Serialize(new { takenAt = DateTime.UtcNow, columns = new List<SqlColumnInfo> { new() { Name = "Amount", DataType = "nvarchar", MaxLength = 100, IsNullable = true } } }),
        CurrentVersion = new TemplateVersion { Body = "<p>{{ model.Amount }}</p>" }
    });
    discovery.Setup(d => d.GetViewColumnsAsync("v_C", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<SqlColumnInfo> { new() { Name = "Amount", DataType = "int", MaxLength = null, IsNullable = false } });
    var report = await new TemplateHealthService(repo.Object, discovery.Object).CheckAsync(1);

    report.Findings.Should().Contain(f => f.Code == "column_type_changed");
    report.Findings.Should().NotContain(f => f.Code == "column_length_changed");
}

[Fact]
public async Task Check_UnboundTemplateWithTokens_ReportsWarning()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 1, Name = "T", SourceView = null, CurrentVersion = new TemplateVersion { Body = "<p>{{ model.FirstName }}</p>" }
    });
    var report = await new TemplateHealthService(repo.Object, new Mock<ISqlViewDiscoveryService>().Object).CheckAsync(1);
    report.Findings.Should().Contain(f => f.Code == "unbound_tokens" && f.Severity == HealthSeverity.Warning);
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Application.Tests --filter "FullyQualifiedName~TemplateHealthService"`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement** — port the fork's `TemplateHealthService.cs` verbatim with two adaptations:
1. `Scriban.Template.Parse` (fully qualified — the `Template` entity name clashes).
2. `BuildSnapshotJsonAsync` uses `JsonSerializer.Serialize(new { takenAt = DateTime.UtcNow, columns }, CamelJson)` — add a private static CamelJson options instance (or a shared internal helper; prefer a small internal static `JsonDefaults` class in Application if the promotion service already defined one — reuse it, don't duplicate).

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS (5 tests). Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Application/Services tests/TemplateBuilder.Application.Tests/Services
git commit -m "feat: template health check engine (Scriban AST token extraction + snapshot drift)"
```

---

### Task 4: Controllers, DI, request DTOs

**Files:**
- Modify: `src/TemplateBuilder.Editor/Controllers/TemplatesController.cs`
- Create: `src/TemplateBuilder.Editor/Controllers/HealthController.cs`
- Create: `src/TemplateBuilder.Editor/Models/HealthIndexViewModel.cs`, `src/TemplateBuilder.Editor/Models/BulkIdsRequest.cs`
- Modify: `src/TemplateBuilder.Editor/Models/SaveVersionRequest.cs` (add `string? SourceView = null`)
- Modify: `src/TemplateBuilder.Editor/Models/TemplateEditorViewModel.cs` (add `string? SourceView` + `List<string> AvailableViews` already exists)
- Modify: `src/TemplateBuilder.Editor/ServiceCollectionExtensions.cs` (register `ITemplatePromotionRepository`, `ITemplatePromotionService`, `ITemplateHealthService` as Scoped)
- Modify: `tests/TemplateBuilder.Editor.Tests/Controllers/TemplatesControllerTests.cs`

**Interfaces:**
- Consumes: services from Tasks 2–3; `GetAllIncludingInactiveAsync` (T1); `DeleteAsync` (T1); `BuildSnapshotJsonAsync` (T3).
- Produces routes (spec Module 1/2): `GET Templates/Export/{id:int}`; `POST Templates/Import` (multipart); `POST Templates/BulkActivate|BulkDeactivate|BulkExport|BulkDelete`; `GET Templates/{id:int}/Health`; `GET Health`; `GET Health/Summaries?ids=`.

- [ ] **Step 1: Write the failing tests**

`TemplatesControllerTests.cs` — extend `CreateController` with the two new dependencies (`ITemplatePromotionService? promo = null, ITemplateHealthService? health = null`). Add:

```csharp
[Fact]
public async Task ExportTemplate_ReturnsFileAttachment()
{
    var promo = new Mock<ITemplatePromotionService>();
    promo.Setup(p => p.BuildExportAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateExportDocument { Template = new TemplateExportTemplate { Name = "Inv", TemplateType = "Email" } });
    promo.Setup(p => p.SerializeExport(It.IsAny<TemplateExportDocument>())).Returns("{}");
    promo.Setup(p => p.SanitizeFileName("Inv")).Returns("Inv");
    var controller = CreateController(promo: promo);

    var result = await controller.ExportTemplate(1);

    result.Should().BeOfType<FileContentResult>();
    var file = (FileContentResult)result;
    file.ContentType.Should().Be("application/json");
}

[Fact]
public async Task BulkDelete_ReturnsSucceededAndFailed()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 1, Name = "A", TemplateType = "Email" });
    repo.Setup(r => r.DeleteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    repo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync((Template?)null);
    var controller = CreateController(repo.Object);

    var result = await controller.BulkDelete(new BulkIdsRequest { Ids = new List<int> { 1, 2 } });

    result.Should().BeOfType<OkObjectResult>();
    var ok = (OkObjectResult)result;
    ok.Value.Should().BeEquivalentTo(new { succeeded = new[] { 1 }, failed = new[] { new { id = 2, reason = "NOT_FOUND" } } });
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests`
Expected: FAIL — controller lacks the endpoints/dependencies.

- [ ] **Step 3: Implement**

`TemplatesController` — extend the constructor with `ITemplatePromotionService promotion, ITemplateHealthService health`; add the endpoints per the spec's route table (port the fork's controller code, adapting: `Ok(...)`/`NotFound(...)`/`BadRequest(new ErrorResult(...))` instead of `Content(JsonConvert.SerializeObject(...))`; `[FromForm] IFormFile file` for Import; `[FromBody] BulkIdsRequest` for bulk; `File(bytes, contentType)` for downloads; `Content-Disposition` via `Response.Headers["Content-Disposition"] = $"attachment; filename={...}"`). Import result and health report JSON: `Ok(result)` / `Ok(report)` (System.Text.Json camelCase default for MVC).

`SaveVersion` — after the `Description` update, add:

```csharp
var previousSourceView = template.SourceView;
template.SourceView = string.IsNullOrWhiteSpace(request.SourceView) ? null : request.SourceView.Trim();
if (!string.Equals(previousSourceView, template.SourceView, StringComparison.OrdinalIgnoreCase))
    template.SourceViewSnapshot = template.SourceView is null
        ? null
        : await _health.BuildSnapshotJsonAsync(template.SourceView, ct);
```

`HealthController` (attribute routes `Health`, `Health/Summaries`): Index loads `GetAllIncludingInactiveAsync` and runs `CheckAsync` per template into `HealthIndexViewModel`; Summaries parses the comma-separated `ids`, returns `Ok(list)` of `{ templateId, severity, findingCount }` with `SeverityName(HealthSeverity)` mapping (critical/warning/healthy).

`BulkIdsRequest`:

```csharp
public class BulkIdsRequest
{
    public List<int> Ids { get; set; } = new();
}
```

`ServiceCollectionExtensions.AddTemplateBuilderEditor` — after the engine registration:

```csharp
services.AddScoped<ITemplatePromotionRepository, TemplatePromotionRepository>();
services.AddScoped<ITemplatePromotionService, TemplatePromotionService>();
services.AddScoped<ITemplateHealthService, TemplateHealthService>();
```

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS. Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor tests/TemplateBuilder.Editor.Tests
git commit -m "feat: promotion/health/bulk endpoints and DI registration"
```

---

### Task 5: Views + assets (Index bulk bar + import modal, Health page, editor health UI)

**Files:**
- Modify: `src/TemplateBuilder.Editor/Views/Templates/Index.cshtml`
- Modify: `src/TemplateBuilder.Editor/Views/Templates/Edit.cshtml`
- Create: `src/TemplateBuilder.Editor/Views/Health/Index.cshtml`
- Modify: `src/TemplateBuilder.Editor/wwwroot/css/template-editor.css` (append Section "Lifecycle & Ops")

**Interfaces:**
- Consumes: routes from Task 4; `TemplateEditorViewModel.SourceView`/`AvailableViews`; `HealthIndexViewModel`.
- Produces the element ids Task 6's JS binds to (exact list below).

- [ ] **Step 1: Index.cshtml**

- Header actions: add `<button type="button" id="btn-import-open" class="btn btn-secondary">Import</button>`.
- Bulk toolbar under the page header (hidden until selection): `#tb-bulk-bar` with `#tb-bulk-count`, `#btn-bulk-activate`, `#btn-bulk-deactivate`, `#btn-bulk-export`, `#btn-bulk-delete`, `#btn-bulk-clear`.
- Table: prepend `<th class="tb-check-col"><input type="checkbox" id="tb-check-all"></th>` and `<th>Health</th>`; each row prepends `<td class="tb-check-col"><input type="checkbox" class="tb-row-check" value="@t.Id"></td>` and `<td><span class="tb-health-badge" data-template-id="@t.Id">—</span></td>`; add an `Export` row action link (`@Url.Action("ExportTemplate", "Templates", new { id = t.Id })`).
- Import modal before the host close (`#import-modal` overlay with `#import-file`, `#btn-import-close`, `#btn-import-submit`, `#import-error`, `#import-result`, hint text — no Review/Approved wording).

- [ ] **Step 2: Edit.cshtml**

- Properties panel, after the Type select block: Source SQL View select `#prop-source-view` (options from `Model.AvailableViews`, `@(Model.SourceView == view ? "selected" : "")`).
- Footer: `<button type="button" id="btn-health" class="btn btn-secondary">Health</button>` next to Preview; `<div id="health-panel" class="tb-health-panel" hidden><div id="health-findings"></div><div id="health-meta" class="tb-health-meta"></div></div>`.

- [ ] **Step 3: Health page** — port the fork's `Views/Health/Index.cshtml` with the origin's `HealthIndexViewModel { Rows: List<HealthRowViewModel { TemplateId, Name, Report }> }` (the fork's row shape; the controller builds it).

- [ ] **Step 4: CSS** — append the fork's lifecycle Section 36 rules (bulk bar, check/health columns, import result entries, health panel/findings), adjusted to the origin's tokens (`--surface`, `--border`, `--radius-md`, `--success-bg`/`--success`/`--success-border`, `--warning-*`, `--danger-*`, `--surface2`, `--text-muted` — verify names in the origin's `template-editor.css` token block first and map any renamed tokens).

- [ ] **Step 5: Verify**

Run: `dotnet build TemplateBuilder.slnx` (0 errors; RCL views compile directly). Expected: no build steps beyond that (no codegen).

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Editor/Views src/TemplateBuilder.Editor/wwwroot/css
git commit -m "feat: bulk toolbar, health page, import modal, editor health UI"
```

---

### Task 6: Editor JS modules

**Files:**
- Modify: `src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (append guarded modules)

**Interfaces:**
- Consumes: `_csrf` const, `showToast(msg)`, `escapeHtml(str)` (all exist), the Task 4 routes, the Task 5 element ids.
- Produces behavior only.

- [ ] **Step 1: Append the four modules** (port from the fork's `template-editor.js` lifecycle section verbatim, adjusting `window.tbTemplateId`/`window.tbIsCreate` guards to the origin's inline script — the origin sets `const templateId = @(Model.Id?.ToString() ?? "null")`, so guard with `templateId > 0`; `window.tbTemplateId` does not exist there)

1. `initBulkOps` — guard `#tb-bulk-bar`; select-all; bulkPost with `JSON.stringify({ ids })` + `RequestVerificationToken` header; toast + reload handlers for activate/deactivate/delete; blob download for export ZIP.
2. `initHealthBadges` — guard `.tb-health-badge` presence; fetch `/Health/Summaries?ids=...`; severity-styled text.
3. `initImportModal` — open/close via the overlay `open` class (the origin's `.modal-overlay.open` pattern — same as the fork); FormData file upload with the token header; render created/updated/skipped/error entries; toast + reload.
4. `initEditorHealth` — guard `#btn-health`/`#health-panel`/`templateId > 0`; fetch `/Templates/${templateId}/Health`; render findings by severity; meta line.

- [ ] **Step 2: Syntax check**

Run: `node --check src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` — exit 0, no output.

- [ ] **Step 3: Commit**

```bash
git add src/TemplateBuilder.Editor/wwwroot/js/template-editor.js
git commit -m "feat: bulk selection, import modal, health badge/editor JS modules"
```

---

### Task 7: End-to-end verification

**Files:** none (verification only; fixes land in the owning task)

- [ ] **Step 1: Build + all suites**

`dotnet build TemplateBuilder.slnx` (0 errors) then the four test projects. Expected: all green.

- [ ] **Step 2: e2e (Web at https://localhost:7275/)** — the spec Module 5 checklist:

1. Fresh DB boot applies `AddLifecycleOps` (sqlcmd: 3 new columns; `ExternalKey` unique index; legacy rows keyed by NEWID).
2. Create template (v1 Active) → `GET /Templates/Export/{id}` → attachment `{schemaVersion:2,...isActive:true}` (filename sanitized).
3. `POST /Templates/Import` multipart (`curl -F "file=@export.json"` + token header) → `{created:[...]}`; re-import → `{updated:[...], versionsAppended:1}` with flags preserved (check history badges).
4. Health: bind the template to a scratch view via `prop-source-view` + SaveVersion (snapshot written); `docker exec`/sqlcmd `ALTER VIEW` (drop a column, widen another) → `GET /Templates/{id}/Health` shows `column_missing` critical + drift warnings; `/Health` page chips + rows; `/Health/Summaries?ids=` → badge JSON; index badges populated.
5. Bulk: check 2 rows → toolbar appears; Deactivate → toast + Inactive badges; Activate back; Export ZIP (save, `unzip -l` → 2 entries + `_summary.json` with `schemaVersion: 2`); Delete → rows gone (confirm dialog).
6. `GET /Templates/_setup` — all checks pass.

- [ ] **Step 3: Pack + inspect**

`dotnet pack` both projects; extract the nupkgs: 4 DLLs + README + `wwwroot`/views as RCL content per TFM, README What's New says 2.1.0.

- [ ] **Step 4: Fix forward** — any failure returns to the owning task (TDD first), re-run Steps 1–3. Record evidence in the PR description/commit messages.

---

### Task 8: Version + docs

**Files:**
- Modify: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` (`<Version>` → `2.1.0`; or fold into 2.0.0 per owner decision)
- Modify: `src/TemplateBuilder.Editor/README.md` (features table rows for export/import/health/bulk; a Lifecycle & Ops section; What's New 2.1.0 block)
- Modify: this repo's `docs/superpowers/plans/2026-08-21-origin-lifecycle-ops-implementation.md` — no; the ORIGIN's `docs/superpowers/` if it tracks plans (optional)

- [ ] **Step 1:** bump + README (feature table: Export template, Import template export file, Bulk activate/deactivate/export/delete, Template health check, Health overview page, Health summaries; Lifecycle & Ops section describing promotion identity/upsert/flag preservation, health findings, bulk ops).
- [ ] **Step 2:** `dotnet build TemplateBuilder.slnx` — 0 errors.
- [ ] **Step 3:** Commit `docs: v2.1.0 — lifecycle & ops docs`.

---

## Self-review notes

- Spec coverage: L1–L4 → T2; L5–L7 → T3; L8–L10 → T1 + T4; L11–L13 → T4/T5/T6 constraints; L14 → T8. All Module 1–5 requirements mapped.
- The fork's lifecycle implementation is the reference for exact algorithms; the stack mapping table in the two-state spec applies (EF Core LINQ, System.Text.Json, RCL views, MS DI, native `List<int>` binding).
- `DeleteAsync` ordering (versions before template) is explicit because the origin's FK config is `NoAction` on `TemplateId` — the fork's null-CurrentVersionId pre-step is NOT needed (EF Core `SetNull`).
- Health snapshot serialization reuses a shared camelCase `JsonSerializerOptions` (Application-internal static) — no duplicated options instances.
- Import `Skipped` stays in the result shape (always empty) so the JS entry renderer is identical to the fork's.
- e2e DB edits use sqlcmd against the Web sample's database only — never the test InMemory contexts.
