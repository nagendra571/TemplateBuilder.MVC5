# Two-State Save Model (TemplateBuilder.Editor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the fork's two-state save model to TemplateBuilder.Editor: `TemplateVersion.IsActive` (Draft/Active per version), Save Draft + Save Version buttons with per-version badges, and a render API that serves the last Active version with typed exceptions — while keeping the origin's autosave and Create behavior.

**Architecture:** Additive domain change (`TemplateVersion.IsActive` + two exceptions + two repository accessors + EF Core migration), then rewire `TemplateEngine` (last-active selection through the existing version-aware cache — draft saves must not evict the cached active body), then controller/view/JS changes (record `bool? IsActive`, two buttons, badges), then version 2.0.0 + docs. Works in both `TemplateBuilder.Editor` and `TemplateBuilder.Core` (shared `TemplateEngine`).

**Tech Stack:** .NET 8 / .NET 10 multi-target, ASP.NET Core MVC (Razor RCL), EF Core 8/10 SqlServer, System.Text.Json, Scriban 7.2.6, xUnit + Moq + FluentAssertions, InMemory EF for repo tests.

**Spec:** `docs/superpowers/specs/2026-08-21-origin-two-state-save-design.md` — decisions D1–D11 are quoted from there.

## Global Constraints

- Repo: `github.com/nagendra571/TemplateBuilder` (private), branch `main`. Work from the repo root; `git pull` first.
- Build: `dotnet build TemplateBuilder.slnx` — must end 0 errors on both TFMs (net8.0 + net10.0).
- Tests: `dotnet test tests/TemplateBuilder.Application.Tests`, `tests/TemplateBuilder.Editor.Tests`, `tests/TemplateBuilder.Infrastructure.Tests`, `tests/TemplateBuilder.Domain.Tests` — run individually (never concurrently); net8+net10 tests run per-TFM via `dotnet test` (net10 default; add `-f net8.0` for the net8 pass where the CI does).
- JSON: System.Text.Json only (NO Newtonsoft). `Ok(new {...})` / `[FromBody]` records; explicit serializer options use `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.
- Antiforgery: `[ValidateAntiForgeryToken]` + the JS `RequestVerificationToken` header (ASP.NET Core native — no changes).
- Views/assets: RCL `.cshtml` + `wwwroot` are edited directly (no RazorGenerator). All CSS selectors scoped `#tb-editor-host`.
- EF Core migrations: `dotnet ef migrations add <Name> --project src/TemplateBuilder.Infrastructure` (design-time factory `TemplateBuilderDbContextFactory` exists). `MigrationHostedService` applies migrations at app startup.
- e2e host: `src/TemplateBuilder.Web` at `https://localhost:7275/` (`dotnet run --project src/TemplateBuilder.Web`). `GET /Templates/_setup` verifies integration.
- Version bumps: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` and `src/TemplateBuilder.Core/TemplateBuilder.Core.csproj` → `2.0.0`. Sync the packages' README "What's New" with the bump (repo lesson: README must match the package).
- Commits: conventional style (`feat:`, `fix:`, `docs:`, `chore:`); only commit what the task lists; the user approves pushes separately.
- Reference implementation: the fork (`github.com/nagendra571/TemplateBuilder.MVC5`, commits `785aa9e`..`b2d0c1a`) implemented and e2e-verified this exact feature — consult its files for exact code where this plan is terse (spec Module "Reference implementation" maps the stack constructs).
- Do NOT touch: autosave module behavior (D5), Create-publishes-v1 (D6), sample data, snippets, authorization, SetupController.

---

### Task 1: Domain + repository + migration (additive)

**Files:**
- Modify: `src/TemplateBuilder.Domain/Entities/TemplateVersion.cs`
- Create: `src/TemplateBuilder.Domain/Exceptions/TemplateInactiveException.cs`, `src/TemplateBuilder.Domain/Exceptions/NoActiveVersionException.cs`
- Modify: `src/TemplateBuilder.Domain/Interfaces/ITemplateRepository.cs`
- Modify: `src/TemplateBuilder.Infrastructure/Repositories/TemplateRepository.cs`
- Modify: `src/TemplateBuilder.Infrastructure/Data/Configurations/TemplateVersionConfiguration.cs` (no change needed for a plain `bool` — verify; skip if EF Core maps it fine)
- Create: `src/TemplateBuilder.Infrastructure/Migrations/<timestamp>_AddVersionIsActive.cs` (+ Designer; scaffolded)
- Modify: `tests/TemplateBuilder.Infrastructure.Tests/Repositories/TemplateRepositoryTests.cs`
- Modify: `tests/TemplateBuilder.Domain.Tests/Entities/TemplateVersionTests.cs` (add a default-IsActive test)

**Interfaces:**
- Produces:
  - `TemplateVersion.IsActive` — `bool`, default `true`.
  - `TemplateInactiveException(int templateId)` — message `"Template {templateId} is inactive and not servable."`
  - `NoActiveVersionException(int templateId)` — message `"Template {templateId} has no active version to serve."`
  - `ITemplateRepository.GetLastActiveVersionAsync(int templateId, CancellationToken ct = default)` → `Task<TemplateVersion?>` (latest version with `IsActive == true`, `null` if none).
  - `ITemplateRepository.GetVersionAsync(int versionId, CancellationToken ct = default)` → `Task<TemplateVersion?>`.

- [ ] **Step 1: Write the failing tests**

`TemplateRepositoryTests.cs` (InMemory `CreateContext()` helper already exists in the file):

```csharp
[Fact]
public async Task GetLastActiveVersionAsync_ReturnsLatestActive_SkipsDrafts()
{
    await using var context = CreateContext();
    var repo = new TemplateRepository(context);
    var template = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
    await repo.PublishVersionAsync(template.Id, new TemplateVersion { Body = "active-1", IsActive = true });
    await repo.PublishVersionAsync(template.Id, new TemplateVersion { Body = "draft-2", IsActive = false });
    var active3 = await repo.PublishVersionAsync(template.Id, new TemplateVersion { Body = "active-3", IsActive = true });
    await repo.PublishVersionAsync(template.Id, new TemplateVersion { Body = "draft-4", IsActive = false });

    var result = await repo.GetLastActiveVersionAsync(template.Id);

    result.Should().NotBeNull();
    result!.Id.Should().Be(active3.Id);
    result.Body.Should().Be("active-3");
}

[Fact]
public async Task GetLastActiveVersionAsync_ReturnsNull_WhenAllVersionsAreDrafts()
{
    await using var context = CreateContext();
    var repo = new TemplateRepository(context);
    var template = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
    await repo.PublishVersionAsync(template.Id, new TemplateVersion { Body = "draft", IsActive = false });

    var result = await repo.GetLastActiveVersionAsync(template.Id);

    result.Should().BeNull();
}

[Fact]
public async Task GetVersionAsync_ReturnsSingleVersion()
{
    await using var context = CreateContext();
    var repo = new TemplateRepository(context);
    var template = await repo.CreateAsync(new Template { Name = "A", TemplateType = "Email" });
    var v = await repo.PublishVersionAsync(template.Id, new TemplateVersion { Body = "x" });

    var result = await repo.GetVersionAsync(v.Id);

    result.Should().NotBeNull();
    result!.Body.Should().Be("x");
}
```

`tests/TemplateBuilder.Domain.Tests/Entities/TemplateVersionTests.cs` (add):

```csharp
[Fact]
public void TemplateVersion_DefaultsToActive()
{
    var v = new TemplateVersion();
    v.IsActive.Should().BeTrue();
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.Tests` and `dotnet test tests/TemplateBuilder.Domain.Tests`
Expected: FAIL — `IsActive`/`GetLastActiveVersionAsync`/`GetVersionAsync` missing (CS1061/CS0117).

- [ ] **Step 3: Implement**

`TemplateVersion.cs` — add after `CreatedBy`:

```csharp
public bool IsActive { get; set; } = true;
```

Exceptions (mirror `TemplateNotFoundException.cs` style):

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
Task<TemplateVersion?> GetVersionAsync(int versionId, CancellationToken ct = default);
```

`TemplateRepository.cs` — add after `GetVersionBodyAsync`:

```csharp
public async Task<TemplateVersion?> GetLastActiveVersionAsync(int templateId, CancellationToken ct = default) =>
    await _context.TemplateVersions
        .Where(v => v.TemplateId == templateId && v.IsActive)
        .OrderByDescending(v => v.VersionNumber)
        .FirstOrDefaultAsync(ct);

public async Task<TemplateVersion?> GetVersionAsync(int versionId, CancellationToken ct = default) =>
    await _context.TemplateVersions
        .AsNoTracking()
        .FirstOrDefaultAsync(v => v.Id == versionId, ct);
```

- [ ] **Step 4: Scaffold the migration**

Run: `dotnet ef migrations add AddVersionIsActive --project src/TemplateBuilder.Infrastructure`
The generated `Up()` will contain `AddColumn<bool>(name: "IsActive", table: "TemplateVersions", type: "bit", nullable: false, defaultValue: false)` — hand-edit `defaultValue` to `true`:

```csharp
migrationBuilder.AddColumn<bool>(
    name: "IsActive",
    table: "TemplateVersions",
    type: "bit",
    nullable: false,
    defaultValue: true);
```

- [ ] **Step 5: Run — verify green**

Run the Step 2 commands. Expected: PASS (3 repo tests + 1 entity test). If the InMemory provider doesn't apply migrations (it doesn't by default), the schema comes from the model — fine; migration validity is verified at e2e (Task 5: the Web host's `MigrationHostedService` applies it on a fresh DB).

- [ ] **Step 6: Full suites + build**

Run: `dotnet build TemplateBuilder.slnx` (0 errors, both TFMs) then all four test projects.
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/TemplateBuilder.Domain src/TemplateBuilder.Infrastructure tests/TemplateBuilder.Domain.Tests tests/TemplateBuilder.Infrastructure.Tests
git commit -m "feat: version-level IsActive with last-active accessor and typed exceptions"
```

---

### Task 2: TemplateEngine render contract + cache interplay

**Files:**
- Modify: `src/TemplateBuilder.Application/Services/TemplateEngine.cs`
- Modify: `tests/TemplateBuilder.Application.Tests/Services/TemplateEngineTests.cs`

**Interfaces:**
- Consumes: `GetLastActiveVersionAsync` (Task 1); exceptions (Task 1).
- Produces (behavior contract): `RenderAsync`/`RenderByNameAsync` throw `TemplateNotFoundException` (missing), `TemplateInactiveException` (`IsActive == false`), `NoActiveVersionException` (no Active version); otherwise render the last Active version's body via the existing cache. `RenderBodyAsync` unchanged.

- [ ] **Step 1: Rewrite the failing tests**

In `TemplateEngineTests.cs`, REWRITE the two tests that mock `GetCurrentVersionIdAsync`:

```csharp
[Fact]
public async Task RenderAsync_UnknownTemplateId_ThrowsTemplateNotFoundException()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((Template?)null);
    var engine = CreateEngine(repo.Object);

    var act = async () => await engine.RenderAsync(999, new { });

    await act.Should().ThrowAsync<TemplateNotFoundException>();
}

[Fact]
public async Task RenderAsync_ValidTemplate_FetchesLastActiveVersionBodyAndRenders()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 1, Name = "A", IsActive = true });
    repo.Setup(r => r.GetLastActiveVersionAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new TemplateVersion { Id = 10, Body = "<p>{{ model.Title }}</p>" });
    repo.Setup(r => r.GetVersionBodyAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync("<p>{{ model.Title }}</p>");
    var engine = CreateEngine(repo.Object);

    var result = await engine.RenderAsync(1, new { Title = "Hi" });

    result.Should().Be("<p>Hi</p>");
}
```

Add these new tests:

```csharp
[Fact]
public async Task RenderAsync_InactiveTemplate_ThrowsTemplateInactiveException()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 7, IsActive = false });
    var engine = CreateEngine(repo.Object);

    var act = async () => await engine.RenderAsync(7, new { });

    await act.Should().ThrowAsync<TemplateInactiveException>();
}

[Fact]
public async Task RenderAsync_NoActiveVersion_ThrowsNoActiveVersionException()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 7, IsActive = true });
    repo.Setup(r => r.GetLastActiveVersionAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync((TemplateVersion?)null);
    var engine = CreateEngine(repo.Object);

    var act = async () => await engine.RenderAsync(7, new { });

    await act.Should().ThrowAsync<NoActiveVersionException>();
}

[Fact]
public async Task RenderAsync_LatestVersionIsDraft_ServesOlderActiveBody()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 1, IsActive = true,
        CurrentVersion = new TemplateVersion { Id = 12, VersionNumber = 3, Body = "draft", IsActive = false }
    });
    repo.Setup(r => r.GetLastActiveVersionAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateVersion { Id = 11, VersionNumber = 2, Body = "<p>{{ model.Title }}</p>", IsActive = true });
    repo.Setup(r => r.GetVersionBodyAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync("<p>{{ model.Title }}</p>");
    var engine = CreateEngine(repo.Object);

    var result = await engine.RenderAsync(1, new { Title = "Active" });

    result.Should().Be("<p>Active</p>");
}

[Fact]
public async Task RenderByNameAsync_InactiveTemplate_ThrowsTemplateInactiveException()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByNameAsync("Off", It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 9, IsActive = false });
    var engine = CreateEngine(repo.Object);

    var act = async () => await engine.RenderByNameAsync("Off", new { });

    await act.Should().ThrowAsync<TemplateInactiveException>();
}

[Fact]
public async Task DraftSave_DoesNotEvictCachedActiveBody()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 1, IsActive = true });
    repo.Setup(r => r.GetLastActiveVersionAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateVersion { Id = 10, Body = "<p>{{ model.Title }}</p>", IsActive = true });
    repo.Setup(r => r.GetVersionBodyAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync("<p>{{ model.Title }}</p>");
    var engine = CreateEngine(repo.Object);

    await engine.RenderAsync(1, new { Title = "One" });   // warms the cache
    await engine.RenderAsync(1, new { Title = "Two" });   // same active version id — must hit the cache

    repo.Verify(r => r.GetVersionBodyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task ActiveSave_RefetchesBody()
{
    var repo = new Mock<ITemplateRepository>();
    var activeId = 10;
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 1, IsActive = true });
    repo.Setup(r => r.GetLastActiveVersionAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => new TemplateVersion { Id = activeId, Body = "<p>{{ model.Title }}</p>", IsActive = true });
    repo.Setup(r => r.GetVersionBodyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int id, CancellationToken _) => $"<p>{{{{ model.Title }}}} {id}</p>");
    var engine = CreateEngine(repo.Object);

    await engine.RenderAsync(1, new { Title = "One" });   // caches version 10
    activeId = 11;                                         // an active save happened
    var result = await engine.RenderAsync(1, new { Title = "Two" });

    result.Should().Contain("11");
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Application.Tests --filter "FullyQualifiedName~TemplateEngineTests"`
Expected: FAIL — old tests mock `GetCurrentVersionIdAsync` which the new code no longer calls; new tests fail on missing exceptions/behavior.

- [ ] **Step 3: Implement**

Replace the two render method bodies (keep `RenderBodyAsync` and `GetBodyAsync` as-is):

```csharp
public async Task<string> RenderAsync(int templateId, object model, CancellationToken ct = default)
{
    var template = await _repository.GetByIdAsync(templateId, ct);
    if (template is null)
        throw new TemplateNotFoundException(templateId);
    if (!template.IsActive)
        throw new TemplateInactiveException(templateId);

    var activeVersion = await _repository.GetLastActiveVersionAsync(templateId, ct)
        ?? throw new NoActiveVersionException(templateId);

    var body = await GetBodyAsync(templateId, activeVersion.Id, ct);
    return await RenderBodyAsync(body, model, ct);
}

public async Task<string> RenderByNameAsync(string templateName, object model, CancellationToken ct = default)
{
    var template = await _repository.GetByNameAsync(templateName, ct);
    if (template is null)
        throw new TemplateNotFoundException(templateName);
    if (!template.IsActive)
        throw new TemplateInactiveException(template.Id);

    var activeVersion = await _repository.GetLastActiveVersionAsync(template.Id, ct)
        ?? throw new NoActiveVersionException(template.Id);

    var body = await GetBodyAsync(template.Id, activeVersion.Id, ct);
    return await RenderBodyAsync(body, model, ct);
}
```

`GetBodyAsync` is unchanged — it already compares `cached.VersionId == <the id passed in>`; passing the last Active version id gives the required interplay (draft saves never change it; active saves do).

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS (all TemplateEngineTests incl. the cache interplay tests).

- [ ] **Step 5: Full suite + build**

Run: `dotnet build TemplateBuilder.slnx` then `dotnet test tests/TemplateBuilder.Application.Tests`.
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Application tests/TemplateBuilder.Application.Tests
git commit -m "feat: render API serves last active version with typed exceptions"
```

---

### Task 3: Controller/endpoints + Editor tests

**Files:**
- Modify: `src/TemplateBuilder.Editor/Models/SaveVersionRequest.cs`
- Modify: `src/TemplateBuilder.Editor/Models/TemplateEditorViewModel.cs`
- Modify: `src/TemplateBuilder.Editor/Controllers/TemplatesController.cs`
- Modify: `tests/TemplateBuilder.Editor.Tests/Controllers/TemplatesControllerTests.cs`

**Interfaces:**
- Consumes: `TemplateVersion.IsActive` (T1), `GetVersionAsync` (T1).
- Produces: `SaveVersionRequest` record gains `bool? IsActive = null`; `SaveVersion` sets `IsActive = request.IsActive ?? true` and returns `Ok(new { versionId, versionNumber, isActive })`; `RestoreVersion`/`Duplicate` inherit the source flag; `TemplateEditorViewModel.LatestVersionIsActive`; Edit GET sets it.

- [ ] **Step 1: Write the failing tests**

`TemplatesControllerTests.cs` — follow the file's existing `CreateController` helper. Add:

```csharp
[Fact]
public async Task SaveVersion_WithoutIsActive_DefaultsToActive()
{
    var repo = new Mock<ITemplateRepository>();
    var template = new Template { Id = 1, Name = "A", TemplateType = "Email" };
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(template);
    TemplateVersion? captured = null;
    repo.Setup(r => r.PublishVersionAsync(1, It.IsAny<TemplateVersion>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int id, TemplateVersion v, CancellationToken _) => { captured = v; return v; });
    repo.Setup(r => r.GetNextVersionNumberAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(2);
    var controller = CreateController(repo.Object);

    var result = await controller.SaveVersion(1, new SaveVersionRequest("A", "Email", null, "<p>x</p>", null));

    result.Should().BeOfType<OkObjectResult>();
    captured!.IsActive.Should().BeTrue();
}

[Fact]
public async Task SaveVersion_IsActiveFalse_CreatesDraftVersion()
{
    var repo = new Mock<ITemplateRepository>();
    var template = new Template { Id = 1, Name = "A", TemplateType = "Email" };
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(template);
    TemplateVersion? captured = null;
    repo.Setup(r => r.PublishVersionAsync(1, It.IsAny<TemplateVersion>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int id, TemplateVersion v, CancellationToken _) => { captured = v; return v; });
    repo.Setup(r => r.GetNextVersionNumberAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(2);
    var controller = CreateController(repo.Object);

    var result = await controller.SaveVersion(1, new SaveVersionRequest("A", "Email", null, "<p>x</p>", null, IsActive: false));

    var ok = (OkObjectResult)result;
    ok.Value.Should().BeEquivalentTo(new { versionId = 0, versionNumber = 2, isActive = false });
    captured!.IsActive.Should().BeFalse();
}

[Fact]
public async Task RestoreVersion_InheritsSourceIsActive()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetVersionAsync(5, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TemplateVersion { Id = 5, VersionNumber = 1, Body = "<p>old</p>", IsActive = false });
    repo.Setup(r => r.GetNextVersionNumberAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(3);
    TemplateVersion? captured = null;
    repo.Setup(r => r.PublishVersionAsync(1, It.IsAny<TemplateVersion>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int id, TemplateVersion v, CancellationToken _) => { captured = v; return v; });
    var controller = CreateController(repo.Object);

    var result = await controller.RestoreVersion(1, 5, 1);

    result.Should().BeOfType<OkObjectResult>();
    captured!.IsActive.Should().BeFalse();
}

[Fact]
public async Task Duplicate_InheritsLatestVersionIsActive()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 3, Name = "Src", TemplateType = "Email",
        CurrentVersion = new TemplateVersion { Body = "<p>x</p>", IsActive = false }
    });
    repo.Setup(r => r.CreateAsync(It.IsAny<Template>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template { Id = 9, Name = "Copy", TemplateType = "Email" });
    TemplateVersion? captured = null;
    repo.Setup(r => r.PublishVersionAsync(9, It.IsAny<TemplateVersion>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int id, TemplateVersion v, CancellationToken _) => { captured = v; return v; });
    var controller = CreateController(repo.Object);

    var result = await controller.Duplicate(3, new DuplicateRequest("Copy"));

    result.Should().BeOfType<OkObjectResult>();
    captured!.IsActive.Should().BeFalse();
}

[Fact]
public async Task Edit_SetsLatestVersionIsActive()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template
    {
        Id = 1, Name = "A", TemplateType = "Email",
        CurrentVersion = new TemplateVersion { VersionNumber = 2, Body = "<p>d</p>", IsActive = false }
    });
    var discovery = new Mock<ISqlViewDiscoveryService>();
    discovery.Setup(d => d.GetViewNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
    var controller = CreateController(repo.Object, discovery.Object);

    var result = await controller.Edit(1);

    var view = (ViewResult)result;
    var model = (TemplateEditorViewModel)view.Model!;
    model.LatestVersionIsActive.Should().BeFalse();
}

[Fact]
public void SaveVersionRequest_JsonWithoutIsActive_BindsNull()
{
    var json = """{"name":"A","templateType":"Email","body":"<p>x</p>"}""";
    var request = System.Text.Json.JsonSerializer.Deserialize<SaveVersionRequest>(
        json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    request.Should().NotBeNull();
    request!.IsActive.Should().BeNull();
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests`
Expected: FAIL — record lacks `IsActive`, controller doesn't use it, view model lacks `LatestVersionIsActive`.

- [ ] **Step 3: Implement**

`SaveVersionRequest.cs`:

```csharp
public record SaveVersionRequest(
    string Name,
    string TemplateType,
    string? Description,
    string Body,
    string? ChangeComment,
    bool? IsActive = null);
```

`TemplateEditorViewModel.cs` — add after `CurrentVersionNumber`:

```csharp
public bool LatestVersionIsActive { get; set; } = true;
```

`TemplatesController.cs`:
- `Edit`: add `LatestVersionIsActive = template.CurrentVersion?.IsActive ?? true` to the view model.
- `SaveVersion`: version construction gains `IsActive = request.IsActive ?? true`; response becomes `Ok(new { versionId = version.Id, versionNumber = version.VersionNumber, isActive = version.IsActive })`.
- `RestoreVersion`: replace the `GetVersionBodyAsync` fetch with a single `GetVersionAsync(versionId)` call for both body and flag:

```csharp
var source = await _repository.GetVersionAsync(versionId, ct);
if (source is null) return NotFound(new ErrorResult("VERSION_NOT_FOUND", $"Version {versionId} not found."));
var nextNumber = await _repository.GetNextVersionNumberAsync(id, ct);
var version = await _repository.PublishVersionAsync(id, new TemplateVersion
{
    TemplateId = id,
    VersionNumber = nextNumber,
    Body = source.Body,
    ChangeComment = $"Restored from v{sourceVersionNumber}",
    IsActive = source.IsActive
}, ct);
```

- `Duplicate`: after `var body = source.CurrentVersion?.Body ?? string.Empty;` add `var isActive = source.CurrentVersion?.IsActive ?? true;` and set `IsActive = isActive` on the new v1.

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS (all Editor tests). Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor tests/TemplateBuilder.Editor.Tests
git commit -m "feat: two-state save endpoints — isActive flag, inherit on restore/duplicate"
```

---

### Task 4: Editor UI — two buttons, badges, autosave interplay

**Files:**
- Modify: `src/TemplateBuilder.Editor/Views/Templates/Edit.cshtml`
- Modify: `src/TemplateBuilder.Editor/Views/Templates/_VersionHistory.cshtml`
- Modify: `src/TemplateBuilder.Editor/wwwroot/js/template-editor.js`
- Modify: `src/TemplateBuilder.Editor/wwwroot/css/template-editor.css`

**Interfaces:**
- Consumes: `LatestVersionIsActive` (T3), `SaveVersion` response `isActive` (T3).
- Produces: `btn-save-draft` (secondary) + `btn-save` (primary) in edit-mode footer; `#draft-version-badge` next to `#version-display` when latest is a draft; Active/Draft badges in version history cards; JS `saveVersion(isActive)`.

- [ ] **Step 1: Edit.cshtml**

In the edit-mode footer (`@if (!isNew)` block), change:

```html
<button type="button" id="btn-save" class="btn btn-primary">Save Version</button>
```

to:

```html
<button type="button" id="btn-save-draft" class="btn btn-secondary">Save Draft</button>
<button type="button" id="btn-save" class="btn btn-primary">Save Version</button>
```

Next to the version display (the `tb-version-row` div), after `#version-display`:

```html
@if (!isNew && !Model.LatestVersionIsActive)
{
    <span id="draft-version-badge" class="tb-badge tb-badge-draft">Draft version</span>
}
```

- [ ] **Step 2: _VersionHistory.cshtml**

Inside the version card header, after the `isCurrent` Current badge block:

```html
<span class="tb-badge @(v.IsActive ? "tb-badge-live" : "tb-badge-draft")">@(v.IsActive ? "Active" : "Draft")</span>
```

- [ ] **Step 3: template-editor.js**

Change `async function saveVersion()` → `async function saveVersion(isActive)`; add to the JSON body after `changeComment`:

```javascript
isActive,
```

On success, after `document.getElementById('version-display').textContent = ...`, update the badge:

```javascript
const existingBadge = document.getElementById('draft-version-badge');
if (data.isActive) {
    if (existingBadge) existingBadge.remove();
} else if (!existingBadge) {
    const b = document.createElement('span');
    b.id = 'draft-version-badge';
    b.className = 'tb-badge tb-badge-draft';
    b.textContent = 'Draft version';
    document.getElementById('version-display').after(b);
}
```

Replace the single binding at ~line 1456:

```javascript
document.getElementById('btn-save')?.addEventListener('click', () => saveVersion(true));
document.getElementById('btn-save-draft')?.addEventListener('click', () => saveVersion(false));
```

(Remove the old `document.getElementById('btn-save')?.addEventListener('click', saveVersion);` line.)

**Autosave interplay (D5 — no behavior change to autosave itself):** the existing `clearDraft()` in the success path already clears the localStorage buffer on any version save; it now also runs for draft-version saves, which is correct (a saved version supersedes the unsaved buffer). `loadDraft`'s `versionNumber !== currentVersionNumber` guard keeps working. Do NOT modify `saveDraft`/`loadDraft`/`DRAFT_KEY`/`AUTOSAVE_PREF_KEY`/`btn-autosave-toggle`.

- [ ] **Step 4: template-editor.css**

Append at the end of the file:

```css
/* ── Two-state save (Draft/Active versions) ── */
#tb-editor-host #draft-version-badge { margin-left: 6px; vertical-align: middle; }
#tb-editor-host .tb-version-header .tb-badge { margin-left: 6px; }
```

- [ ] **Step 5: Verify**

Run: `node --check src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (exit 0, no output), then `dotnet build TemplateBuilder.slnx` (0 errors).

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Editor/Views src/TemplateBuilder.Editor/wwwroot
git commit -m "feat: Save Draft / Save Version buttons and per-version badges in the editor"
```

---

### Task 5: Version 2.0.0 + README + e2e verification

**Files:**
- Modify: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` (`<Version>` → `2.0.0`)
- Modify: `src/TemplateBuilder.Core/TemplateBuilder.Core.csproj` (`<Version>` → `2.0.0`)
- Modify: both projects' READMEs (What's New + Render-in-code section: typed exceptions + last-active serving)
- Modify: `src/TemplateBuilder.Web` (nothing — e2e only)
- Create: e2e evidence notes (no committed files needed)

- [ ] **Step 1: Version bumps + README**

Bump both csprojs to `2.0.0`. READMEs: add a `#### v2.0.0` What's New block — two-state save model (Save Draft / Save Version, per-version Draft/Active badges), render API serves the last Active version and throws `TemplateInactiveException`/`NoActiveVersionException` (breaking: inactive templates previously threw `TemplateNotFoundException`), autosave and Create behavior unchanged.

- [ ] **Step 2: Build + all suites**

Run: `dotnet build TemplateBuilder.slnx` (0 errors) then all four test projects. Expected: all green.

- [ ] **Step 3: e2e — fresh DB + UI flow**

Run: `dotnet run --project src/TemplateBuilder.Web` (first boot applies `AddVersionIsActive` via `MigrationHostedService`; verify with SSMS/sqlcmd: `TemplateVersions.IsActive` bit NOT NULL default 1, all legacy rows = 1).

Browser flow (localhost:7275):
1. Create template with a body → lands on Edit with v1 (Active).
2. Type + **Save Draft** → toast, `v2` display, "Draft version" badge appears; History shows v2 **Draft**.
3. **Save Version** → `v3` display, badge gone; History shows v1 Active / v2 Draft / v3 Active + Current marker on v3.
4. Reload Edit → body shows v3 (latest, not the draft) — D4.
5. **Autosave regression**: type something, reload → "Unsaved draft found" banner appears; Restore brings the text back; Discard clears. (D5.)
6. Restore v2 (the draft) → new v4 inherits Draft; badge reappears.
7. Duplicate → new template's v1 inherits the latest version's flag.
8. `GET /Templates/_setup` — all checks pass.

- [ ] **Step 4: Developer API verification (packaged DLLs)**

`dotnet pack` both projects. In a scratch console app referencing the packed `TemplateBuilder.Core` 2.0.0 nupkg (or the Web app's DI), verify: a template with v3=draft, v2=active → `RenderByNameAsync` returns v2's body; `IsActive=false` template → `TemplateInactiveException`; all-draft template → `NoActiveVersionException`; unknown → `TemplateNotFoundException`. Inspect the nupkgs: README inside shows v2.0.0 What's New (the repo's README-sync lesson).

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor src/TemplateBuilder.Core
git commit -m "docs: v2.0.0 — two-state save model (breaking render contract)"
```

---

## Self-review notes

- Spec coverage: D1–D11 → Tasks 1–5 (D1 → T1; D2/D3 + cache → T2; D8/D9 + record gotcha → T3; D4/D5/D6 → T4; D10 → T5). D11 (lifecycle) is the next spec.
- The record-binding gotcha (`bool? IsActive = null` + `?? true`) is deliberate — System.Text.Json constructor deserialization does not reliably honor positional defaults for value types, and a `false`-defaulting draft save would be a silent data bug.
- `GetVersionAsync` (new, T1) replaces the fork's history-scan approach for Restore — one row, no full-history fetch.
- Cache interplay tests (T2 Step 1) assert the actual mechanism (call-count via Moq `Verify`; id-change via mutable captured id) so the "drafts never evict/serve" property is pinned.
- Autosave module (D5) is deliberately untouched; only the shared success-path `clearDraft()` continues to run for both buttons.
- e2e uses the Web sample at port 7275 (per the origin's version-compare plan convention).
