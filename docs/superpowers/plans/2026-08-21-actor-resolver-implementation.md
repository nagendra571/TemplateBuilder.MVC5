# Actor Resolver (Custom CreatedBy) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `TemplateBuilder.Editor.Mvc5` NuGet consumers supply their own author identity (claims, user id, username, composite) via a resolver delegate, persisted to every TemplateBuilder actor column going forward, with "anonymous" verbiage for legacy nulls.

**Architecture:** Add `Func<HttpContextBase, string?>` `ActorResolver` to `TemplateBuilderEditorOptions` (consumed in the existing `RegisterTemplateBuilderEditor` call). `TemplateBuilderControllerBase.CurrentActor` resolves through an internal pure helper (`ActorResolverChain`) with fallback chain resolver → `User.Identity.Name` → `"anonymous"`, truncated to 200 chars, cached once per request in `HttpContext.Items`. The editor stamps `CreatedBy = CurrentActor` on every new `TemplateVersion` (gap fix — currently never populated). No backfill, no Domain/Application/Infrastructure changes, no schema change.

**Tech Stack:** .NET Framework 4.8 / C# latest, ASP.NET MVC 5.3, Unity 5.11.10, RazorGenerator-precompiled views, xunit + FluentAssertions + NSubstitute, xsp4 sample-host smoke + agent-browser verification.

**Spec:** `docs/superpowers/specs/2026-08-21-actor-resolver-design.md`

## Global Constraints

- Build: `dotnet build TemplateBuilder.Mvc5.sln --nologo` — 0 errors. Tests run per-project, never concurrently: `dotnet test tests/<X>.Tests/<X>.Tests.csproj --nologo -v q`.
- **Stop the xsp4 sample host before running the EF6 suite** (shared DB — "Cannot drop database because it is currently in use"; kill by PID from `ss -ltnp | grep :8081`, never pkill the name).
- **Mono test-host crashes are transient:** a `dotnet test` run for a net48 project can abort with "Test host process crashed" (mono_crash dumps). Re-run once before treating it as a real failure; never debug a single crashed run.
- Views are RazorGenerator-precompiled — never ship `.cshtml`; `dotnet build` regenerates `obj/CodeGen` (BLOCKERS #10).
- Sample-host verification cycle (learned in the lifecycle phase): `dotnet pack src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Release -o /tmp/opencode/nupkg-test` → extract the nupkg and copy the 4 DLLs from `lib/net48/` into `samples/TemplateBuilder.SampleMvc5Host/packages/TemplateBuilder.Editor.Mvc5.<ver>/lib/net48/` (nuget.exe may be absent after environment resets — DLL copy is equivalent) → bump the 4 `<HintPath>`s in the sample csproj → `xbuild samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj /p:Configuration=Debug` → restart xsp4: `XSP_BIN=/tmp/opencode/xsp/src/Mono.WebServer.XSP/bin/Debug; MONO_PATH=$XSP_BIN setsid mono $XSP_BIN/Mono.WebServer.XSP.exe --applications /:/workspaces/TemplateBuilder.Mvc5/samples/TemplateBuilder.SampleMvc5Host --port 8081 --nonstop > /tmp/opencode/xsp4.log 2>&1 < /dev/null &` (first request after boot can 500 once — EF init race; retry). If `/tmp/opencode/xsp` was wiped, rebuild xsp per BLOCKERS #11 (clone mono/xsp, checkout `72b24c0`, generate AssemblyInfo.cs from `.in`, `SignAssembly=false`, xbuild `src/Mono.WebServer.XSP/Mono.WebServer.XSP.csproj /p:Configuration=Debug`).
- `grep -c` exit-code trap: `grep -c` exits 1 when the count is 0 — it makes `&&` chains short-circuit. Append `|| true` or use `grep -c ... ; test $? -le 1`.
- `HttpContextBase` lives in `System.Web` — the test project needs `<Reference Include="System.Web" />`.
- Do not modify `Domain/` or `Application/` (verbatim ports; actor already flows through their string `actor` parameters). No backfill of existing rows.
- Only commit when the user explicitly asks (repo rule).

---

### Task 1: ActorResolver option + resolution chain + CurrentActor wiring

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorOptions.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/ActorResolverChain.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs:51` (set `TemplateBuilderEditorOptions.Current = options;` after `TemplateBuilderAuthorizationFilter.Configure(...)`)
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplateBuilderControllerBase.cs:8` (CurrentActor chain + per-request cache)
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj` (InternalsVisibleTo)
- Create: `tests/TemplateBuilder.Editor.Mvc5.Tests/TemplateBuilder.Editor.Mvc5.Tests.csproj`
- Create: `tests/TemplateBuilder.Editor.Mvc5.Tests/ActorResolverChainTests.cs`
- Modify: `TemplateBuilder.Mvc5.sln` (add test project via `dotnet sln`)

**Interfaces:**
- Consumes: `TemplateBuilderEditorOptions` (existing), `TemplateBuilderAuthorizationFilter.Configure` (existing, untouched)
- Produces:
  - `TemplateBuilderEditorOptions.ActorResolver` — `public Func<HttpContextBase, string?>?` (consumers set this)
  - `TemplateBuilderEditorOptions.Current` — `internal static TemplateBuilderEditorOptions?` (set by registration; read by base controller)
  - `internal static class ActorResolverChain` with `public static string Resolve(Func<HttpContextBase, string?>? resolver, string? identityName, HttpContextBase? httpContext)` — returns trimmed-chain actor, max 200 chars, never null/whitespace
  - `TemplateBuilderControllerBase.CurrentActor` — `protected string`, now resolver-backed, cached in `HttpContext.Items["TemplateBuilder.Editor.Mvc5.Actor"]`

- [ ] **Step 1: Scaffold the test project** (TDD — tests come first)

Create `tests/TemplateBuilder.Editor.Mvc5.Tests/TemplateBuilder.Editor.Mvc5.Tests.csproj` (mirror `tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj` exactly, but reference the Mvc5 project and `System.Web`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="10.0.1" />
    <PackageReference Include="FluentAssertions" Version="8.10.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="System.Web" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TemplateBuilder.Editor.Mvc5\TemplateBuilder.Editor.Mvc5.csproj" />
  </ItemGroup>
</Project>
```

Register it in the solution:

```bash
dotnet sln TemplateBuilder.Mvc5.sln add tests/TemplateBuilder.Editor.Mvc5.Tests/TemplateBuilder.Editor.Mvc5.Tests.csproj
```

- [ ] **Step 2: Write the failing chain tests**

Create `tests/TemplateBuilder.Editor.Mvc5.Tests/ActorResolverChainTests.cs`:

```csharp
using System;
using System.Web;
using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Editor.Mvc5;

namespace TemplateBuilder.Editor.Mvc5.Tests;

public class ActorResolverChainTests
{
    private static HttpContextBase Http() => Substitute.For<HttpContextBase>();

    [Fact]
    public void Resolve_uses_resolver_result_when_non_blank()
    {
        var resolver = new Func<HttpContextBase, string?>(_ => "jdoe");
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("jdoe");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_resolver_returns_null()
    {
        var resolver = new Func<HttpContextBase, string?>(_ => null);
        ActorResolverChain.Resolve(resolver, "alice", Http()).Should().Be("alice");
    }

    [Fact]
    public void Resolve_falls_back_to_identity_name_when_resolver_returns_whitespace()
    {
        var resolver = new Func<HttpContextBase, string?>(_ => "   ");
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
        HttpContextBase? received = null;
        var resolver = new Func<HttpContextBase, string?>(c => { received = c; return "bob"; });
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
    public void Resolve_keeps_values_under_200_characters_unchanged()
    {
        ActorResolverChain.Resolve(null, "bob", Http()).Should().Be("bob");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/TemplateBuilder.Editor.Mvc5.Tests/TemplateBuilder.Editor.Mvc5.Tests.csproj --nologo -v q`
Expected: build fails — `ActorResolverChain` does not exist. (If the test host crashes, re-run once — mono flake.)

- [ ] **Step 4: Add the InternalsVisibleTo to the Mvc5 project**

Add to `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj` (inside the root `<Project>`, alongside the existing ItemGroups):

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="TemplateBuilder.Editor.Mvc5.Tests" />
  </ItemGroup>
```

- [ ] **Step 5: Implement the option + chain**

Modify `src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorOptions.cs` to:

```csharp
using System;
using System.Web;
using TemplateBuilder.Editor.Mvc5.Authorization;

namespace TemplateBuilder.Editor.Mvc5;

public class TemplateBuilderEditorOptions
{
    internal static TemplateBuilderEditorOptions? Current { get; set; }

    public string ConnectionString { get; set; } = string.Empty;
    public TemplateBuilderAuthorizationOptions Authorization { get; set; } = new();
    public Func<HttpContextBase, string?>? ActorResolver { get; set; }
}
```

Create `src/TemplateBuilder.Editor.Mvc5/ActorResolverChain.cs`:

```csharp
using System;
using System.Web;

namespace TemplateBuilder.Editor.Mvc5;

internal static class ActorResolverChain
{
    private const int MaxActorLength = 200;

    public static string Resolve(Func<HttpContextBase, string?>? resolver, string? identityName, HttpContextBase? httpContext)
    {
        var actor = resolver?.Invoke(httpContext!);
        if (string.IsNullOrWhiteSpace(actor))
            actor = identityName;
        if (string.IsNullOrWhiteSpace(actor))
            actor = "anonymous";
        return actor.Length <= MaxActorLength ? actor : actor.Substring(0, MaxActorLength);
    }
}
```

- [ ] **Step 6: Wire options.Current in registration**

Modify `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs` — immediately after `TemplateBuilderAuthorizationFilter.Configure(options.Authorization);` (line 51) add:

```csharp
        TemplateBuilderEditorOptions.Current = options;
```

- [ ] **Step 7: Rewire CurrentActor**

Modify `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplateBuilderControllerBase.cs` — replace line 8 (`protected string CurrentActor => User?.Identity?.Name ?? "anonymous";`) with:

```csharp
    private const string ActorCacheKey = "TemplateBuilder.Editor.Mvc5.Actor";

    protected string CurrentActor
    {
        get
        {
            var httpContext = HttpContext;
            if (httpContext?.Items[ActorCacheKey] is string cached)
                return cached;
            var actor = ActorResolverChain.Resolve(
                TemplateBuilderEditorOptions.Current?.ActorResolver,
                User?.Identity?.Name,
                httpContext);
            if (httpContext is not null)
                httpContext.Items[ActorCacheKey] = actor;
            return actor;
        }
    }
```

(`HttpContext` on `Controller` is already `HttpContextBase`; `User` is `IPrincipal`.)

- [ ] **Step 8: Run the chain tests**

Run: `dotnet test tests/TemplateBuilder.Editor.Mvc5.Tests/TemplateBuilder.Editor.Mvc5.Tests.csproj --nologo -v q`
Expected: 9 PASS. Re-run once if the host crashes (mono flake).

- [ ] **Step 9: Full build + regression suites**

Run: `dotnet build TemplateBuilder.Mvc5.sln --nologo` — Expected: 0 errors. Then:
- `dotnet test tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj --nologo -v q`
- `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --nologo -v q`
(xsp4 already stopped — EF6 suite runs in Task 4.)
Expected: all green.

- [ ] **Step 10: Commit** (only if the user has asked to commit; otherwise leave staged/unstaged and report)

```bash
git add src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorOptions.cs src/TemplateBuilder.Editor.Mvc5/ActorResolverChain.cs src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs src/TemplateBuilder.Editor.Mvc5/Controllers/TemplateBuilderControllerBase.cs src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj tests/TemplateBuilder.Editor.Mvc5.Tests/ TemplateBuilder.Mvc5.sln docs/superpowers/specs/2026-08-21-actor-resolver-design.md
git commit -m "feat: configurable ActorResolver for CreatedBy/audit identity"
```

---

### Task 2: Stamp CreatedBy on every new TemplateVersion

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs:138-145` (SaveVersion), `:190-197` (RestoreVersion), `:326-333` (Duplicate)

**Interfaces:**
- Consumes: `CurrentActor` from Task 1 (protected property on `TemplateBuilderControllerBase`)
- Produces: template versions persisted with non-null `CreatedBy` going forward (no backfill)

- [ ] **Step 1: Add `CreatedBy = CurrentActor` to the three publish call sites**

In `SaveVersion` (the `new TemplateVersion { ... }` inside `PublishVersionAsync`, currently `TemplatesController.cs:138`), add the property:

```csharp
            published = await _repository.PublishVersionAsync(id, new TemplateVersion
            {
                TemplateId = id,
                VersionNumber = nextNumber,
                Body = request.Body,
                ChangeComment = request.ChangeComment,
                IsActive = request.IsActive,
                CreatedBy = CurrentActor
            });
```

In `RestoreVersion` (currently `TemplatesController.cs:190`):

```csharp
            published = await _repository.PublishVersionAsync(id, new TemplateVersion
            {
                TemplateId = id,
                VersionNumber = nextNumber,
                Body = oldBody,
                ChangeComment = $"Restored from v{sourceVersionNumber}",
                IsActive = source?.IsActive ?? true,
                CreatedBy = CurrentActor
            });
```

In `Duplicate` (currently `TemplatesController.cs:326`):

```csharp
            await _repository.PublishVersionAsync(newTemplate.Id, new TemplateVersion
            {
                TemplateId = newTemplate.Id,
                VersionNumber = 1,
                Body = body,
                ChangeComment = $"Duplicated from '{source.Name}'",
                IsActive = isActive,
                CreatedBy = CurrentActor
            });
```

- [ ] **Step 2: Verify**

Run: `dotnet build TemplateBuilder.Mvc5.sln --nologo` — Expected: 0 errors. Then confirm exactly three stamps:

```bash
grep -c "CreatedBy = CurrentActor" src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs
```

Expected: 3 (note the `grep -c` exit-code trap — a count of 0 exits 1; a count of 3 exits 0).

- [ ] **Step 3: Commit** (only if the user has asked to commit)

```bash
git add src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs
git commit -m "fix: stamp CreatedBy on every new template version (was never populated)"
```

---

### Task 3: UI "anonymous" verbiage + consumer docs + sample demo

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/_VersionHistory.cshtml:22`
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js:1930`
- Modify: `src/TemplateBuilder.Editor.Mvc5/README.md` (package README — ships in the nupkg)
- Modify: `README.md` (repo root)
- Modify: `samples/TemplateBuilder.SampleMvc5Host/App_Start/UnityConfig.cs`

**Interfaces:**
- Consumes: `options.ActorResolver` from Task 1

- [ ] **Step 1: Razor partial — always render the author with "anonymous" fallback**

Replace line 22 of `src/TemplateBuilder.Editor.Mvc5/Views/Templates/_VersionHistory.cshtml`:

```razor
                <span class="tb-version-meta" style="margin-left:auto;">@version.CreatedAt.ToString("dd MMM yyyy") · @(string.IsNullOrWhiteSpace(version.CreatedBy) ? "anonymous" : version.CreatedBy)</span>
```

- [ ] **Step 2: Editor JS — snippet version history fallback**

In `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js:1930`, change:

```js
                    <div class="tb-version-meta">${escapeHtml(v.createdBy || '')} · ${new Date(v.createdAt).toLocaleString()}</div>
```

to:

```js
                    <div class="tb-version-meta">${escapeHtml(v.createdBy || 'anonymous')} · ${new Date(v.createdAt).toLocaleString()}</div>
```

- [ ] **Step 3: Syntax-check the JS and rebuild views**

Run: `node --check src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (exit 0, no output), then `dotnet build TemplateBuilder.Mvc5.sln --nologo` (0 errors — the RazorGenerator precompile regenerates `obj/CodeGen`).

- [ ] **Step 4: Package README — new "Author identity" section + What's New**

In `src/TemplateBuilder.Editor.Mvc5/README.md`, insert a new subsection under "## Access Control" (after the "#### What is protected" block, before "## Setup Diagnostic Page" at line ~199). Use a 4-backtick fence when adding this content to README.md (it contains a code sample):

````markdown
## Author Identity (CreatedBy)

Every TemplateBuilder table that records an author (`TemplateVersion.CreatedBy`,
`SnippetVersion.CreatedBy`, snippet usage `UsedBy`, and the audit log `Actor`) is stamped
with the current user, resolved in this order:

1. **`options.ActorResolver`** (your custom resolver, if set)
2. `User.Identity.Name`
3. `"anonymous"`

Without configuration the editor stores `User.Identity.Name` (or `"anonymous"` when the
request is unauthenticated or the name is empty). Existing records are never backfilled —
legacy rows display `"anonymous"` in the UI.

Supply your own identity from your existing `RegisterTemplateBuilderEditor` call — e.g. a
claims value:

```csharp
container.RegisterTemplateBuilderEditor(options =>
{
    options.ConnectionString = connectionString;
    // Store the "sub" claim (or any claim / custom user lookup) as the author
    options.ActorResolver = ctx => ctx.User?.FindFirst("sub")?.Value;
});
```

The resolver receives the request's `HttpContextBase`, so it can read claims, session, or
any of your own services captured in the closure. It runs once per request; a `null` or
blank result falls back to the chain below it. The stored value is truncated to 200
characters (the column limit). Exceptions thrown by your resolver propagate.
````

Add a `#### v1.3.0` entry at the top of the "## What's New" section (line ~377, above `#### v1.2.0`):

```markdown
#### v1.3.0

- New `TemplateBuilderEditorOptions.ActorResolver` — supply your own author identity
  (claims, user id, username) stored as `CreatedBy` / audit `Actor`. Falls back to
  `User.Identity.Name`, then `"anonymous"`. Legacy null values now display "anonymous".
- Template version history now stamps `CreatedBy` on every save (previously never
  populated); existing versions are not backfilled.
```

- [ ] **Step 5: Root README — brief mention**

In `README.md` (repo root), after the "## Requirements" section, add:

```markdown
## Customizing the author identity

Consumers supply their own user identity (claims, user id, etc.) via
`options.ActorResolver` in `RegisterTemplateBuilderEditor` — see the package README's
"Author Identity (CreatedBy)" section. Values flow to `TemplateVersion.CreatedBy`,
`SnippetVersion.CreatedBy`, snippet usage, and audit rows; the fallback chain is
resolver → `User.Identity.Name` → `"anonymous"`.
```

- [ ] **Step 6: Sample host — demonstrate the resolver**

Modify `samples/TemplateBuilder.SampleMvc5Host/App_Start/UnityConfig.cs` inside the existing `RegisterTemplateBuilderEditor` options lambda (after the connection string line, before the closing brace):

```csharp
                // Demo of TemplateBuilderEditorOptions.ActorResolver: any custom identity
                // logic. This sample reads an optional X-TB-Actor header so the flow is
                // verifiable end-to-end (curl -H "X-TB-Actor: alice" ...). In a real app
                // resolve from claims/session instead — a raw header is spoofable and is
                // demo-only here. Falls back to User.Identity.Name -> "anonymous".
                options.ActorResolver = ctx =>
                {
                    var header = ctx?.Request.Headers["X-TB-Actor"];
                    return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
                };
```

- [ ] **Step 7: Verify**

Run: `node --check src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` and `dotnet build TemplateBuilder.Mvc5.sln --nologo` — Expected: 0 errors. (Sample host is xbuild-only; it builds in Task 4.)

- [ ] **Step 8: Commit** (only if the user has asked to commit)

```bash
git add src/TemplateBuilder.Editor.Mvc5/Views/Templates/_VersionHistory.cshtml src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js src/TemplateBuilder.Editor.Mvc5/README.md README.md samples/TemplateBuilder.SampleMvc5Host/App_Start/UnityConfig.cs
git commit -m "docs: author identity (CreatedBy) guidance, anonymous UI fallback, sample demo"
```

---

### Task 4: End-to-end verification + version bump to 1.3.0

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj:78` (Version 1.2.0 → 1.3.0)
- Modify: `samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj:51-61` (4 HintPaths 1.2.0 → 1.3.0)

**Interfaces:**
- Consumes: everything from Tasks 1–3

- [ ] **Step 1: Bump package version**

In `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj` change `<Version>1.2.0</Version>` to `<Version>1.3.0</Version>`.

- [ ] **Step 2: Full build + all four test suites** (xsp4 stopped)

Run: `dotnet build TemplateBuilder.Mvc5.sln --nologo` — 0 errors. Then, sequentially (never concurrently; re-run once on mono crash):
- `dotnet test tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj --nologo -v q`
- `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --nologo -v q`
- `dotnet test tests/TemplateBuilder.Editor.Mvc5.Tests/TemplateBuilder.Editor.Mvc5.Tests.csproj --nologo -v q`
- `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --nologo -v q` (Docker SQL Server must be up; xsp4 stopped)

Expected: all green.

- [ ] **Step 3: Pack + inspect the nupkg**

Run:
```bash
dotnet pack src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Release -o /tmp/opencode/nupkg-test
unzip -o /tmp/opencode/nupkg-test/TemplateBuilder.Editor.Mvc5.1.3.0.nupkg -d /tmp/opencode/nupkg-inspect
ls /tmp/opencode/nupkg-inspect/lib/net48/ /tmp/opencode/nupkg-inspect/README.md
```

Expected: `lib/net48/` contains the 4 DLLs (Editor.Mvc5, Domain, Application, Infrastructure.EF6); `README.md` exists at package root and contains `## Author Identity (CreatedBy)`:

```bash
grep -c "Author Identity (CreatedBy)" /tmp/opencode/nupkg-inspect/README.md
```

Expected: 1 (exit 0 — remember the `grep -c` exit-code trap).

Also confirm the README's version header matches the package version (1.3.0):

```bash
grep -c "Current version: 1.3.0" /tmp/opencode/nupkg-inspect/README.md
```

Expected: 1 (exit 0 — same `grep -c` exit-code trap).

- [ ] **Step 4: Reinstall into the sample host + rebuild**

```bash
mkdir -p samples/TemplateBuilder.SampleMvc5Host/packages/TemplateBuilder.Editor.Mvc5.1.3.0/lib/net48
cp /tmp/opencode/nupkg-inspect/lib/net48/*.dll samples/TemplateBuilder.SampleMvc5Host/packages/TemplateBuilder.Editor.Mvc5.1.3.0/lib/net48/
```

In `samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj`, replace all 4 occurrences of `packages\TemplateBuilder.Editor.Mvc5.1.2.0\` with `packages\TemplateBuilder.Editor.Mvc5.1.3.0\` (lines 52, 55, 58, 61), then delete the old folder:

```bash
rm -rf samples/TemplateBuilder.SampleMvc5Host/packages/TemplateBuilder.Editor.Mvc5.1.2.0
xbuild samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj /p:Configuration=Debug
```

Expected: build succeeds. Then restart xsp4 (exact command in Global Constraints). First request after boot can 500 once (EF init race) — retry.

- [ ] **Step 5: E2E — resolver path via curl (header demo)**

With the sample host up, run (bash — cookie + token + header):

```bash
BASE=http://localhost:8081
curl -s -c /tmp/opencode/tb-cookies.txt "$BASE/Templates/" -o /tmp/opencode/tb-page.html
TOKEN=$(grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' /tmp/opencode/tb-page.html | sed 's/.*value="//;s/"$//')
# NOTE: the header name is "RequestVerificationToken" — no X- prefix. The package attribute
# (ValidateJsonAntiForgeryTokenAttribute) and the editor JS both use the unprefixed name.
curl -s -b /tmp/opencode/tb-cookies.txt -H "RequestVerificationToken: $TOKEN" -H "X-TB-Actor: alice" \
  -H "Content-Type: application/json" \
  -d '{"name":"ActorResolver E2E","templateType":"Html","description":"","sourceView":null,"isActive":true,"body":"<p>hello</p>","changeComment":"first save"}' \
  "$BASE/Templates/1/SaveVersion"
curl -s -b /tmp/opencode/tb-cookies.txt "$BASE/Templates/1/Versions" -o /tmp/opencode/tb-versions.html
grep -o "alice" /tmp/opencode/tb-versions.html | head -1
curl -s -b /tmp/opencode/tb-cookies.txt "$BASE/Templates/1/Audit" | grep -o '"actor":"alice"' | head -1
```

Expected: SaveVersion returns a `versionId`; the Versions page shows `alice` (resolver-stamped `CreatedBy`); the Audit endpoint returns `"actor":"alice"`. If template id 1 doesn't exist, create it first via the UI flow in Step 6 (or use the returned template id).

- [ ] **Step 6: E2E — fallback path via agent-browser (no header → "anonymous")**

Use agent-browser (per project conventions; credentials/skills at the top of this session's instructions) against `http://localhost:8081/Templates/`:
1. Create a new template (no X-TB-Actor header is sent by the browser — resolver returns null → falls back to `User.Identity.Name` → null → `"anonymous"`).
2. Save a version.
3. Open the version history — verify the meta line renders `· anonymous`.
4. Open the Snippets page, create a snippet, verify its version meta renders `anonymous`.
5. If an existing template (created before this feature) still has null `CreatedBy`, verify its version card also renders `anonymous` (no backfill check).

- [ ] **Step 7: Verify DB rows directly (optional but conclusive)**

If `sqlcmd`/Docker SQL Server is available for the sample DB (`TemplateBuilderMvc5` — see sample `web.config`):
```sql
SELECT TOP 5 CreatedBy FROM TemplateVersions ORDER BY Id DESC;   -- expect 'alice' (new) / NULL (legacy)
SELECT TOP 5 Actor FROM AuditLogs ORDER BY Id DESC;              -- expect 'alice' / 'anonymous'
```
Legacy rows stay NULL — no backfill, by design.

- [ ] **Step 8: Commit** (only if the user has asked to commit)

```bash
git add src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj
git commit -m "chore: bump to 1.3.0 (ActorResolver feature)"
```
