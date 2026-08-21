# Actor Resolver (Custom CreatedBy) — TemplateBuilder.Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `TemplateBuilder.Editor` consumers supply their own author identity via `TemplateBuilderEditorOptions.ActorResolver` (set in their existing `AddTemplateBuilderEditor` call), persisted to `TemplateVersion.CreatedBy` going forward and to audit `Actor` via the upgraded `CurrentActor`, with "anonymous" rendering for legacy nulls.

**Architecture:** Add `Func<HttpContext, string?>` `ActorResolver` to the editor options; an internal `ActorResolverChain` (resolver → `User.Identity.Name` → `"anonymous"`, 200-char truncation, once-per-request `HttpContext.Items` cache) + internal `ActorResolverAccessor` singleton registered in `AddTemplateBuilderEditor`; both controllers' `CurrentActor` (added by 2.2.0) delegate to the chain; the editor stamps `CreatedBy = CurrentActor` on all four `TemplateVersion` publish sites (Create, SaveVersion, RestoreVersion, Duplicate). No backfill, no schema change, no Domain/Application/Infrastructure change.

**Tech Stack:** .NET 8 / .NET 10 multi-target, ASP.NET Core MVC (Razor RCL), EF Core 8/10 (no migration needed), System.Text.Json, xUnit + Moq + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-08-21-origin-actor-resolver-design.md` — decisions R1–R13 are quoted from there.

## Global Constraints

- Repo: `github.com/nagendra571/TemplateBuilder` (private), branch `main`. `git pull` first; work from the repo root.
- **Prerequisite**: two-state (2.0.0), lifecycle (2.1.0), and audit-activity (2.2.0) must be merged (or in flight). Task 2 upgrades the 2.2.0 `CurrentActor` property — if 2.2.0 is not yet merged, add the property itself (with the chain body from Task 2) instead of replacing it, and note it in the commit message.
- Build: `dotnet build TemplateBuilder.slnx` — 0 errors on both TFMs (net8.0 + net10.0). Tests: run the four test projects individually; never concurrently.
- JSON: System.Text.Json only (no Newtonsoft). This feature adds no new JSON state.
- Antiforgery: `[ValidateAntiForgeryToken]` + the `RequestVerificationToken` header (native, ASP.NET Core) — no work needed.
- Views/assets: RCL `.cshtml` + `wwwroot` edited directly; all CSS scoped `#tb-editor-host`.
- EF Core: no migration — the `CreatedBy` column (max 200) already exists.
- e2e host: `src/TemplateBuilder.Web` at `https://localhost:7275/`; `GET /Templates/_setup` diagnostics.
- Version: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` → `2.3.0`; package README "What's New" in sync — **and check the README's "Current version:" header line** (the fork's 1.3.0 release caught a stale version header shipping inside the nupkg; grep the extracted README, not just the source).
- Commits: conventional style (`feat:`, `fix:`, `docs:`, `chore:`); only what each task lists; pushes approved separately.
- Reference implementation: the fork's actor-resolver commits (`github.com/nagendra571/TemplateBuilder.MVC5`, private, v1.3.0) — exact shapes in the spec's reference table. **The fork is private — if inaccessible, the embedded tests + spec rules are the complete contract.**
- Do NOT touch: `TemplateBuilder.Core`, the render contract, authorization, setup page, autosave, snippets (nothing to stamp — no snippet versions/usage in the origin), Domain, Application, Infrastructure.

---

### Task 1: Options API + ActorResolverChain + accessor + DI + chain tests

**Files:**
- Modify: `src/TemplateBuilder.Editor/ServiceCollectionExtensions.cs` (options class — locate it first: it is referenced by `AddTemplateBuilderEditor(...)`; add the `ActorResolver` property there; register the accessor)
- Create: `src/TemplateBuilder.Editor/ActorResolverChain.cs`
- Create: `src/TemplateBuilder.Editor/ActorResolverAccessor.cs`
- Test: `tests/TemplateBuilder.Editor.Tests/ActorResolverChainTests.cs`

**Interfaces:**
- Produces (spec Module 1 — exact shapes):
  - `TemplateBuilderEditorOptions.ActorResolver` — `public Func<HttpContext, string?>?` (consumers set this)
  - `internal static class ActorResolverChain` with `public static string Resolve(Func<HttpContext, string?>? resolver, string? identityName, HttpContext? httpContext)` — chain + truncation + cache (key `"TemplateBuilder.Editor.Actor"`)
  - `internal sealed class ActorResolverAccessor` — `public Func<HttpContext, string?>? Resolver { get; }`, ctor takes the delegate
  - DI: `services.AddSingleton(new ActorResolverAccessor(options.ActorResolver));` in `AddTemplateBuilderEditor` (Editor only — R11)

- [ ] **Step 1: Locate the options class**

Read `src/TemplateBuilder.Editor/ServiceCollectionExtensions.cs`. The options type referenced by `AddTemplateBuilderEditor` is expected to be named `TemplateBuilderEditorOptions` (the fork mirrors the origin's shape). If it lives in a separate file, edit that file. If the name differs, use the actual name and note it in the commit message. If the options object is only built inline, create `src/TemplateBuilder.Editor/TemplateBuilderEditorOptions.cs` with the existing members moved over (do not change their behavior).

- [ ] **Step 2: Write the failing chain tests**

`tests/TemplateBuilder.Editor.Tests/ActorResolverChainTests.cs` (new file):

```csharp
using System;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TemplateBuilder.Editor;
using Xunit;

namespace TemplateBuilder.Editor.Tests;

public class ActorResolverChainTests
{
    private static HttpContext Http() => new DefaultHttpContext();

    [Fact]
    public void Resolve_uses_resolver_result_when_non_blank()
    {
        var resolver = new Func<HttpContext, string?>(_ => "jdoe");
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("jdoe");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_resolver_returns_null()
    {
        var resolver = new Func<HttpContext, string?>(_ => null);
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("alice");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_resolver_returns_whitespace()
    {
        var resolver = new Func<HttpContext, string?>(_ => "   ");
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("alice");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_no_resolver()
    {
        ActorResolverChain.Resolve(null, "alice", Http()).Should().Be("alice");
    }

    [Fact]
    public void Resolve_uses_anonymous_when_resolver_and_identity_are_absent()
    {
        ActorResolverChain.Resolve(null, null, Http()).Should().Be("anonymous");
    }

    [Fact]
    public void Resolve_uses_anonymous_when_identity_name_is_whitespace()
    {
        ActorResolverChain.Resolve(null, "  ", Http()).Should().Be("anonymous");
    }

    [Fact]
    public void Resolve_passes_http_context_to_resolver()
    {
        var ctx = Http();
        HttpContext? received = null;
        var resolver = new Func<HttpContext, string?>(c => { received = c; return "bob"; });
        ActorResolverChain.Resolve(resolver, "alice", ctx);
        received.Should().BeSameAs(ctx);
    }

    [Fact]
    public void Resolve_truncates_result_to_200_characters()
    {
        var longValue = new string('x', 250);
        ActorResolverChain.Resolve(null, longValue, Http()).Should().Be(new string('x', 200));
    }

    [Fact]
    public void Resolve_keeps_exactly_200_characters()
    {
        var value = new string('x', 200);
        ActorResolverChain.Resolve(null, value, Http()).Should().Be(value);
    }

    [Fact]
    public void Resolve_propagates_resolver_exceptions()
    {
        var resolver = new Func<HttpContext, string?>(_ => throw new InvalidOperationException("user store down"));
        var act = () => ActorResolverChain.Resolve(resolver, "alice", Http());
        act.Should().Throw<InvalidOperationException>().WithMessage("user store down");
    }

    [Fact]
    public void Resolve_caches_per_request_context()
    {
        var ctx = Http();
        var calls = 0;
        var resolver = new Func<HttpContext, string?>(_ => { calls++; return "bob"; });
        ActorResolverChain.Resolve(resolver, "alice", ctx);
        ActorResolverChain.Resolve(resolver, "alice", ctx);
        calls.Should().Be(1);
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("bob");
        calls.Should().Be(2);
    }
}
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests`
Expected: FAIL to compile — `ActorResolverChain` does not exist.

- [ ] **Step 4: Implement**

Create `src/TemplateBuilder.Editor/ActorResolverChain.cs` (verbatim from spec Module 1):

```csharp
using System;
using Microsoft.AspNetCore.Http;

namespace TemplateBuilder.Editor;

internal static class ActorResolverChain
{
    private const int MaxActorLength = 200;
    private const string CacheKey = "TemplateBuilder.Editor.Actor";

    public static string Resolve(Func<HttpContext, string?>? resolver, string? identityName, HttpContext? httpContext)
    {
        if (httpContext?.Items[CacheKey] is string cached)
            return cached;
        var actor = resolver?.Invoke(httpContext!);
        if (string.IsNullOrWhiteSpace(actor))
            actor = identityName;
        if (string.IsNullOrWhiteSpace(actor))
            actor = "anonymous";
        var result = actor.Length <= MaxActorLength ? actor : actor.Substring(0, MaxActorLength);
        if (httpContext is not null)
            httpContext.Items[CacheKey] = result;
        return result;
    }
}
```

Create `src/TemplateBuilder.Editor/ActorResolverAccessor.cs`:

```csharp
using System;
using Microsoft.AspNetCore.Http;

namespace TemplateBuilder.Editor;

internal sealed class ActorResolverAccessor
{
    public ActorResolverAccessor(Func<HttpContext, string?>? resolver) => Resolver = resolver;
    public Func<HttpContext, string?>? Resolver { get; }
}
```

Add the property to the options class and register the accessor in `AddTemplateBuilderEditor`:

```csharp
public Func<HttpContext, string?>? ActorResolver { get; set; }
```

```csharp
services.AddSingleton(new ActorResolverAccessor(options.ActorResolver));
```

(`using Microsoft.AspNetCore.Http;` + `using TemplateBuilder.Editor;` as needed in `ServiceCollectionExtensions.cs`.)

- [ ] **Step 5: Run — verify green**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests`
Expected: PASS (11 new tests). Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Editor tests/TemplateBuilder.Editor.Tests
git commit -m "feat: ActorResolver option and resolution chain (resolver -> identity -> anonymous)"
```

---

### Task 2: Upgrade CurrentActor on both controllers

**Files:**
- Modify: `src/TemplateBuilder.Editor/Controllers/TemplatesController.cs`
- Modify: `src/TemplateBuilder.Editor/Controllers/SnippetsController.cs`

**Interfaces:**
- Consumes: `ActorResolverChain`, `ActorResolverAccessor` from Task 1
- Produces: `protected string CurrentActor` on both controllers, resolved via the chain (spec Module 2, R8 — supersedes 2.2.0's hardcoded property)

- [ ] **Step 1: Locate the 2.2.0 CurrentActor property**

Grep both controllers for `CurrentActor`. If 2.2.0 is merged, the property is `protected string CurrentActor => User?.Identity?.Name ?? "anonymous";` — replace it. If 2.2.0 is not merged (no property exists), add the property with the new body and note it in the commit message (Global Constraints prerequisite note).

- [ ] **Step 2: Wire the accessor in**

Both controllers gain a constructor parameter and a field:

```csharp
private readonly ActorResolverAccessor _actorResolver;
```

(added to the existing constructor, next to the other dependencies — e.g. `TemplatesController(ITemplateRepository repository, ..., IAuditService audit, ActorResolverAccessor actorResolver)` — match the current parameter order; do not reorder existing params.)

Replace/add the property on both controllers:

```csharp
protected string CurrentActor => ActorResolverChain.Resolve(_actorResolver.Resolver, User?.Identity?.Name, HttpContext);
```

- [ ] **Step 3: Verify**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests` (controller tests still construct with the new ctor param — update the test constructors to pass `new ActorResolverAccessor(null)` where needed) then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/TemplateBuilder.Editor/Controllers tests/TemplateBuilder.Editor.Tests
git commit -m "feat: CurrentActor resolves through ActorResolver (supersedes hardcoded identity name)"
```

---

### Task 3: Stamp CreatedBy on all four TemplateVersion publish sites

**Files:**
- Modify: `src/TemplateBuilder.Editor/Controllers/TemplatesController.cs`
- Test: `tests/TemplateBuilder.Editor.Tests/TemplatesControllerTests.cs`

**Interfaces:**
- Consumes: `CurrentActor` from Task 2
- Produces: every published `TemplateVersion` carries `CreatedBy = CurrentActor` (spec Module 3 — 4 sites: Create, SaveVersion, RestoreVersion, Duplicate; no backfill)

- [ ] **Step 1: Write the failing stamp tests**

Append to `tests/TemplateBuilder.Editor.Tests/TemplatesControllerTests.cs`. Match the existing controller-construction pattern of that file (the test class already builds `TemplatesController` with Moq repos; add `new ActorResolverAccessor(null)` and set the controller's `ControllerContext` so `User.Identity.Name` is known):

```csharp
private static TemplatesController BuildController(Mock<ITemplateRepository> repo)
{
    var controller = new TemplatesController(
        repo.Object,
        /* ...existing mocked dependencies... */,
        new ActorResolverAccessor(null));
    controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice") }))
        }
    };
    return controller;
}
```

(The `...existing mocked dependencies...` placeholder expands to the file's existing dependency list — repos, sanitizer, engine, view discovery, sample data, audit service/repo, promotion, health, as the current constructor requires. Do not add or remove any.)

```csharp
[Fact]
public async Task SaveVersion_stamps_CreatedBy_with_current_actor()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template { Id = 1, Name = "T" });
    repo.Setup(r => r.GetNextVersionNumberAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(1);
    repo.Setup(r => r.PublishVersionAsync(It.IsAny<int>(), It.IsAny<TemplateVersion>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int _, TemplateVersion v, CancellationToken _) => { v.Id = 99; return v; });

    var controller = BuildController(repo);
    var result = await controller.SaveVersion(1, new SaveVersionRequest("T", "Html", null, "<p>hi</p>", null, true));

    repo.Verify(r => r.PublishVersionAsync(1,
        It.Is<TemplateVersion>(v => v.CreatedBy == "alice"), It.IsAny<CancellationToken>()), Times.Once);
}
```

Add the same pattern for `Create` (the v1 publish), `RestoreVersion`, and `Duplicate` — each verifies `PublishVersionAsync` receives a `TemplateVersion` with `CreatedBy == "alice"`. Match each endpoint's existing request/model shapes from the file's current tests.

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests --filter "FullyQualifiedName~TemplatesControllerTests"`
Expected: FAIL — stamps missing (`Times.Once` verify fails: `CreatedBy` is null).

- [ ] **Step 3: Implement**

In `TemplatesController.cs`, add `CreatedBy = CurrentActor,` to the `TemplateVersion` object built in each of the four publish sites:

1. `Create` — the v1 publish (when the submitted body is non-empty)
2. `SaveVersion` — alongside `IsActive = request.IsActive ?? true`
3. `RestoreVersion` — alongside `IsActive = source?.IsActive ?? true`
4. `Duplicate` — the new v1, alongside `IsActive = source.CurrentVersion?.IsActive ?? true`

Keep every other property exactly as-is. Do not touch `PublishVersionAsync` (the stamp is set by the caller on the version object).

- [ ] **Step 4: Run — verify green**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests --filter "FullyQualifiedName~TemplatesControllerTests"` — Expected: PASS. Then the full `dotnet test tests/TemplateBuilder.Editor.Tests` and `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor/Controllers tests/TemplateBuilder.Editor.Tests
git commit -m "fix: stamp CreatedBy on every published template version (was never populated)"
```

---

### Task 4: UI "anonymous" fallback + package README + sample host demo

**Files:**
- Modify: `src/TemplateBuilder.Editor/Views/Templates/_VersionHistory.cshtml` (only if it renders `CreatedBy` — R13)
- Modify: `src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (only if it renders `createdBy` in version history/compare — R13)
- Modify: `src/TemplateBuilder.Editor/README.md` (package README — ships in the nupkg)
- Modify: `src/TemplateBuilder.Web/Program.cs` (or wherever the sample host calls `AddTemplateBuilderEditor`)

**Interfaces:**
- Consumes: `options.ActorResolver` from Task 1

- [ ] **Step 1: Locate the CreatedBy rendering**

Grep `_VersionHistory.cshtml` and `template-editor.js` for `CreatedBy`/`createdBy`. The fork's partial renders a meta line like `@version.CreatedAt.ToString("dd MMM yyyy") · <author>` (with a conditional " · author" suffix); the fork's JS renders `${v.createdBy || ''}` in version history.

- [ ] **Step 2: Apply the fallback where present**

If the partial renders `CreatedBy`, change the meta line so the author is always shown with the fallback (fork shape):

```razor
@version.CreatedAt.ToString("dd MMM yyyy") · @(string.IsNullOrWhiteSpace(version.CreatedBy) ? "anonymous" : version.CreatedBy)
```

If the JS renders `createdBy` in version history/compare, apply the same fallback (`v.createdBy || 'anonymous'`). If neither renders the author, make no view/JS change and say so in the commit message (R13 — the stamping is the contract).

- [ ] **Step 3: Package README — "Author Identity (CreatedBy)" section + What's New**

In `src/TemplateBuilder.Editor/README.md`, add a section (after the Access Control section — mirror the fork's placement) and a `#### v2.3.0` entry at the top of "What's New". Use a 4-backtick outer fence for this block in README.md (it contains a code sample):

````markdown
## Author Identity (CreatedBy)

Every TemplateBuilder table that records an author (`TemplateVersion.CreatedBy` and the
audit log `Actor`) is stamped with the current user, resolved in this order:

1. **`options.ActorResolver`** (your custom resolver, if set)
2. `User.Identity.Name`
3. `"anonymous"`

Without configuration the editor stores `User.Identity.Name` (or `"anonymous"` when the
request is unauthenticated or the name is empty). Existing records are never backfilled —
legacy rows display `"anonymous"` in the UI.

Supply your own identity from your existing `AddTemplateBuilderEditor` call — e.g. a
claims value:

```csharp
builder.Services.AddTemplateBuilderEditor(options =>
{
    options.ConnectionString = connectionString;
    // Store the "sub" claim (or any claim / custom user lookup) as the author
    options.ActorResolver = ctx => ctx.User?.FindFirst("sub")?.Value;
});
```

The resolver receives the request's `HttpContext`, so it can read claims, session, or any
of your own services captured in the closure. It runs once per request; a `null` or blank
result falls back to the chain below it. Values are stored as returned — trim inside the
resolver if your source may carry stray whitespace. The stored value is truncated to 200
characters (the column limit). Exceptions thrown by your resolver propagate.
````

What's New entry:

```markdown
#### v2.3.0

- New `TemplateBuilderEditorOptions.ActorResolver` — supply your own author identity
  (claims, user id, username) stored as `CreatedBy` / audit `Actor`. Falls back to
  `User.Identity.Name`, then `"anonymous"`. Legacy null values now display "anonymous".
- Template version history now stamps `CreatedBy` on every save (previously never
  populated); existing versions are not backfilled.
```

- [ ] **Step 4: Sample host demo**

In `src/TemplateBuilder.Web/Program.cs` (or wherever the sample calls `AddTemplateBuilderEditor`), add the demo resolver inside the existing options lambda — mirror the fork's demo (reads an optional `X-TB-Actor` header so the flow is verifiable end-to-end; header-based actors are spoofable — demo-only):

```csharp
// Demo of ActorResolver: any custom identity logic. Reads an optional X-TB-Actor header
// so the flow is verifiable end-to-end (curl -H "X-TB-Actor: alice" ...). In a real app
// resolve from claims/session instead — a raw header is spoofable and is demo-only here.
options.ActorResolver = ctx =>
{
    var header = ctx?.Request.Headers["X-TB-Actor"].ToString();
    return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
};
```

- [ ] **Step 5: Verify**

Run: `node --check src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (if the JS changed), then `dotnet build TemplateBuilder.slnx` — 0 errors. The sample host builds as part of the solution.

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Editor src/TemplateBuilder.Web
git commit -m "docs: author identity (CreatedBy) guidance, anonymous UI fallback, sample demo"
```

---

### Task 5: End-to-end verification + version bump to 2.3.0

**Files:**
- Modify: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` (Version → 2.3.0)
- Modify: `src/TemplateBuilder.Editor/README.md` (verify the "Current version:" header line, if present, says 2.3.0)

**Interfaces:**
- Consumes: everything from Tasks 1–4

- [ ] **Step 1: Bump the version**

`src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` → `<Version>2.3.0</Version>` (from 2.2.0). Check the README's "Current version:" header (if it exists) and What's New are both 2.3.0.

- [ ] **Step 2: Full build + all four test projects**

Run: `dotnet build TemplateBuilder.slnx` — 0 errors on both TFMs. Then, sequentially (never concurrently): `dotnet test tests/TemplateBuilder.Domain.Tests`, `dotnet test tests/TemplateBuilder.Application.Tests`, `dotnet test tests/TemplateBuilder.Editor.Tests`, `dotnet test tests/TemplateBuilder.Infrastructure.Tests`. Expected: all green.

- [ ] **Step 3: e2e — resolver path + fallback path (Web host)**

`dotnet run --project src/TemplateBuilder.Web` (https://localhost:7275/; accept the self-signed cert). Then:

1. **Resolver path** — with the header: `curl -k -H "RequestVerificationToken: <token from the editor page>" -H "X-TB-Actor: alice" ...` (the sample demo reads the header; the antiforgery token is the hidden `__RequestVerificationToken` input + cookie from a GET of the editor page — ASP.NET Core validates the `RequestVerificationToken` header natively). Create a template and save a version; verify the version history meta shows `alice` and (if 2.2.0 audit is present) the audit row's `actor` is `alice`.
2. **Fallback path** — browser flow without the header: create a template and save a version; verify the version history shows `anonymous`. (The browser never sends `X-TB-Actor`, so the resolver returns null → identity name (null in the anonymous sample) → `"anonymous"`.)
3. If pre-existing templates have null `CreatedBy`, verify their version cards also render `anonymous` (no backfill check).
4. `GET /Templates/_setup` — all checks pass.

- [ ] **Step 4: Pack + inspect the nupkgs**

```bash
dotnet pack -c Release -o /tmp/opencode/nupkg-test
unzip -l /tmp/opencode/nupkg-test/TemplateBuilder.Editor.*.nupkg
unzip -o /tmp/opencode/nupkg-test/TemplateBuilder.Editor.*.nupkg -d /tmp/opencode/nupkg-inspect
grep -c "Author Identity (CreatedBy)" /tmp/opencode/nupkg-inspect/README.md
grep -c "Current version: 2.3.0" /tmp/opencode/nupkg-inspect/README.md   # or whatever the README header format is — it must say 2.3.0
```

Expected: the DLL set + RCL views/assets are as expected; both greps print 1 (remember the `grep -c` exit-code trap — count 0 exits 1 and breaks `&&` chains). Inspect the extracted README, not just the source (repo lesson: stale READMEs ship when only the source is checked).

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor
git commit -m "chore: bump to 2.3.0 (ActorResolver feature)"
```
