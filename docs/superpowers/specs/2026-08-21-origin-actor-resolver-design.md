# Design: Actor Resolver (Custom CreatedBy) — TemplateBuilder.Editor (origin)

**Date:** 2026-08-21
**Audience:** implementer in the origin repo `github.com/nagendra571/TemplateBuilder` (private). Ports the fork's actor-resolver feature (implemented and verified in `TemplateBuilder.Mvc5`, v1.3.0) with deliberate adaptations. Requires the two-state save model (2.0.0), lifecycle & ops (2.1.0), and audit log + activity drawer (2.2.0) — this feature **supersedes 2.2.0's hardcoded `CurrentActor`** and feeds it the resolver.

## Goal

Let consumers of the `TemplateBuilder.Editor` NuGet package supply their own author identity (username, user id, claim value, composite) from their existing `AddTemplateBuilderEditor(options => ...)` registration. Going forward, that value is stored in every TemplateBuilder actor column (`TemplateVersion.CreatedBy`, and audit `Actor` via the upgraded `CurrentActor`). **No backfill** of existing records — legacy rows render `"anonymous"`.

## Decisions (approved by the product owner)

| # | Decision | Rationale |
|---|---|---|
| R1 | **Mechanism: resolver delegate on options** — `TemplateBuilderEditorOptions.ActorResolver` (`Func<HttpContext, string?>?`), set in the same `AddTemplateBuilderEditor` call where the consumer configures `ConnectionString`. No extra DI ceremony for consumers. | Fork parity (the fork shipped the same shape as `TemplateBuilderEditorOptions.ActorResolver` in v1.3.0). Rejected: `IActorProvider` interface (more consumer ceremony), claim-type config (inflexible for user info outside claims). |
| R2 | **Fallback chain:** resolver → `User?.Identity?.Name` → `"anonymous"`. A `null`/whitespace resolver result falls through. Never throws for blank results. | Preserves the pre-feature behavior byte-for-byte when no resolver is set (backward compatible). |
| R3 | **Truncate the final value to 200 chars** (column max on `CreatedBy`/`Actor`) | Avoids `DbUpdateException` on insert. |
| R4 | **Resolve once per request**, cached in `HttpContext.Items` | `CurrentActor` is referenced by many call sites per request. |
| R5 | **Resolver exceptions propagate** | Consumer code fails loudly; never silently write `"anonymous"` over a broken resolver. |
| R6 | **No backfill.** Existing rows keep their values (null for template versions). UI renders `"anonymous"` for null authors. | Explicit product decision. |
| R7 | **Gap fix:** the editor stamps `CreatedBy = CurrentActor` on every new `TemplateVersion` it publishes — **including the Create-publishes-v1 site** (the origin keeps Create's v1 publish, unlike the fork's two-state model). Sites: `Create`, `SaveVersion`, `RestoreVersion`, `Duplicate`. | `TemplateVersion.CreatedBy` exists but is **never populated today** — verified gap shared with the fork. |
| R8 | **Supersedes 2.2.0's `CurrentActor`:** the audit batch adds `protected string CurrentActor => User?.Identity?.Name ?? "anonymous";` to both controllers; this feature replaces that property body with the resolver chain. Audit `Actor` rows then honor the resolver automatically. | One source of truth for the actor; the audit wiring calls `CurrentActor` already. |
| R9 | **Scope reduction (snippets):** the origin has **no `SnippetVersion` and no `SnippetUsage` tables** (snippets are list/create/delete only) — there is nothing to stamp for snippets, and no snippet version-history UI to update. | The origin's surface defines the scope (same logic as audit decision A3). |
| R10 | **Cache lives inside the chain** (not at call sites): `ActorResolverChain.Resolve` writes the result to `HttpContext.Items` when an `HttpContext` is provided. The origin has no shared controller base, so per-call caching would duplicate on both controllers — the chain caches instead. | Fork had a controller base for the cache; the origin does not. One behavior, one implementation. |
| R11 | **DI:** an internal `ActorResolverAccessor` singleton (holding the configured delegate) + the chain are registered in `AddTemplateBuilderEditor` only (Editor). The render-only Core package is untouched. | Same rule as audit A12. |
| R12 | **Version: 2.3.0** (after 2.0.0 two-state, 2.1.0 lifecycle, 2.2.0 audit-activity). | SemVer: additive, non-breaking. |
| R13 | UI "anonymous" fallback applies where `CreatedBy` is rendered in the template version-history surface (`_VersionHistory.cshtml` + the JS compare/history path). If the origin's partial does not render `CreatedBy` at all, skip the UI change and note it (the stamping is the contract). | Display is polish; stamping is the requirement. |

## Current state (origin — verified against `main`, commit 194cf15; assumes 2.0.0–2.2.0 landed)

- `TemplateVersion.CreatedBy` exists (`string`, max 200) but is **never populated**: `PublishVersionAsync` does not set it, and no controller builds a version with `CreatedBy`. Same verified gap as the fork.
- Controllers (`TemplatesController`, `SnippetsController`) are ASP.NET Core attribute-routed; **no shared controller base**. The audit batch (2.2.0) adds `protected string CurrentActor => User?.Identity?.Name ?? "anonymous";` to both.
- `Create` is a **form POST** that publishes v1 when the body is non-empty (Create-publishes-v1 — origin keeps this; the fork removed it).
- `SaveVersion` builds a new `TemplateVersion` with `IsActive = request.IsActive ?? true`; `RestoreVersion` fetches the source via `GetVersionAsync` and inherits `IsActive`; `Duplicate` copies `source.CurrentVersion.Body` and inherits `IsActive`.
- Snippets: `GET/POST/DELETE /Templates/Api/Snippets` only — no versions, no usage, no restore.
- `AddTemplateBuilderEditor(...)` in `src/TemplateBuilder.Editor/ServiceCollectionExtensions.cs` registers DbContext, repos, sanitizer, engine, view discovery, `MigrationHostedService`, authorization, and (since 2.2.0) the audit services.
- Options class: `TemplateBuilderEditorOptions` (verify exact name/location when implementing — the fork mirrors the origin's options shape).
- Tests: Moq + FluentAssertions + InMemory EF; `Editor.Tests` has `TemplatesControllerTests` with controller-level tests.

## Reference implementation (fork)

The fork implemented and verified this feature end-to-end (commits in `github.com/nagendra571/TemplateBuilder.MVC5`, private, v1.3.0). Exact shapes to port:

| Fork piece | Shape |
|---|---|
| API | `TemplateBuilderEditorOptions.ActorResolver` — `Func<HttpContextBase, string?>?` (fork's MVC5 `HttpContextBase`; the origin uses ASP.NET Core `HttpContext`) |
| Chain | `ActorResolverChain.Resolve(resolver, identityName, httpContext)` → `string`; chain: resolver → identity name → `"anonymous"`; `Substring`-truncate at 200; resolver invoked with the request context |
| Cache | Fork: `HttpContext.Items["TemplateBuilder.Editor.Mvc5.Actor"]` in the controller base. Origin adaptation (R10): inside the chain, key `"TemplateBuilder.Editor.Actor"` |
| Stamps | Fork: `CreatedBy = CurrentActor` on SaveVersion/RestoreVersion/Duplicate (3 sites — the fork's Create does not publish v1). Origin: 4 sites incl. Create (R7) |
| UI | `_VersionHistory.cshtml` meta line renders `" · " + CreatedBy` with `"anonymous"` fallback; JS snippet-history meta uses `createdBy || 'anonymous'` (fork snippet path — n/a in the origin) |
| Tests | 11 chain tests: resolver wins; null/whitespace resolver result → identity; no resolver → identity; both absent → anonymous; whitespace identity → anonymous; context passed to resolver; truncation >200; exactly 200 kept; exception propagates; (origin adds: cache hit does not re-invoke) |

Consumer story (verbatim shape):

```csharp
builder.Services.AddTemplateBuilderEditor(options =>
{
    options.ConnectionString = connectionString;
    // Claims-based: pull a claim (sub, employee id, email...)
    options.ActorResolver = ctx => ctx.User?.FindFirst("sub")?.Value;
    // Or any custom logic — user service, session, composite "jdoe (12345)"
    options.ActorResolver = _ => MyAppUserContext.Current?.Id.ToString();
});
```

## Module 1 — API + chain (Editor)

- `TemplateBuilderEditorOptions` gains `public Func<HttpContext, string?>? ActorResolver { get; set; }` (`using Microsoft.AspNetCore.Http;`).
- `ActorResolverChain` — internal static class (`src/TemplateBuilder.Editor/ActorResolverChain.cs`):

```csharp
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

- `ActorResolverAccessor` — internal sealed class holding the configured delegate:

```csharp
internal sealed class ActorResolverAccessor
{
    public ActorResolverAccessor(Func<HttpContext, string?>? resolver) => Resolver = resolver;
    public Func<HttpContext, string?>? Resolver { get; }
}
```

- DI in `AddTemplateBuilderEditor` (Editor only, R11): `services.AddSingleton(new ActorResolverAccessor(options.ActorResolver));`
- No migration, no Domain/Application/Infrastructure change (the `CreatedBy` column exists; the actor already flows through the repos' existing `actor`-free paths — the stamps are set on the version objects by the controllers).

## Module 2 — CurrentActor upgrade (supersedes 2.2.0)

On **both** `TemplatesController` and `SnippetsController` (the origin has no shared base — the property stays duplicated per the 2.2.0 pattern):

- Add constructor parameter `ActorResolverAccessor actorResolver`.
- Replace the 2.2.0 property body:

```csharp
protected string CurrentActor => ActorResolverChain.Resolve(_actorResolver.Resolver, User?.Identity?.Name, HttpContext);
```

- `SnippetsController` needs this even though snippets don't stamp — its audit calls (2.2.0) use `CurrentActor`, and the resolver must apply there too.

## Module 3 — Stamping (gap fix, going forward)

Add `CreatedBy = CurrentActor` to the `TemplateVersion` in **all four publish sites** of `TemplatesController`:

| Site | Endpoint | Shape |
|---|---|---|
| 1 | `Create` (form POST) | the v1 publish when body non-empty |
| 2 | `SaveVersion` | `IsActive = request.IsActive ?? true` publish |
| 3 | `RestoreVersion` | `IsActive = source.IsActive ?? true` publish |
| 4 | `Duplicate` | v1, `IsActive = source.CurrentVersion?.IsActive ?? true` publish |

No backfill. Snippets: nothing to stamp (R9).

## Module 4 — UI fallback (template version history only)

- `Views/Templates/_VersionHistory.cshtml`: locate the `CreatedBy` rendering in the version-card meta line. If present, render `"anonymous"` when null/whitespace (always show ` · <author>`). If the origin partial does not render `CreatedBy`, make no change and note it (R13).
- `wwwroot/js/template-editor.js`: apply the same fallback in the version-history/compare rendering if it renders `createdBy`. (The fork's snippet-history fallback is n/a — no snippet versions in the origin.)

## Module 5 — Testing & verification

- **Editor.Tests** — `ActorResolverChainTests` (11 tests; `DefaultHttpContext`, no mocking needed):
  resolver wins; resolver null → identity; resolver whitespace → identity; no resolver → identity; both absent → `"anonymous"`; identity whitespace → `"anonymous"`; context passed to resolver; truncates 250 → 200; exactly 200 kept; exception propagates (`Assert.Throws`); **cache: second `Resolve` with the same context does not re-invoke the resolver** (R10).
- **Editor.Tests** — `TemplatesControllerTests` additions: `Create`/`SaveVersion`/`RestoreVersion`/`Duplicate` pass `CreatedBy = CurrentActor` to `PublishVersionAsync` (Moq `ITemplateRepository.Verify` on the `TemplateVersion` argument; set `controller.ControllerContext` with a `DefaultHttpContext` whose `User` is a `ClaimsPrincipal` with a known name, and verify the resolved actor).
- **e2e** (Web at `https://localhost:7275/`): create → save version → version history shows the configured resolver value; a no-resolver save shows `"anonymous"`; legacy rows (if any) render `"anonymous"`; audit rows (2.2.0) carry the resolved actor. `GET /Templates/_setup` green.
- **Pack**: nupkgs inspected; README What's New → 2.3.0 (README-sync lesson — the fork's own v1.3.0 release caught a stale "Current version" header in the nupkg README; check it).

## Versioning

`TemplateBuilder.Editor` → **2.3.0** (Core unchanged — no render-contract impact).

## Out of scope (future work)

- Snippet actor columns/UI (no snippet versions/usage in the origin).
- Backfilling legacy rows.
- Structured actor storage (object/JSON) — the columns are `string` (max 200).
- Changes to `TemplateBuilder.Core` or the render contract.
- Changes to the fork repo (standalone fork — the origin implementation is its own).

## Port/fork deviation log

- `Func<HttpContextBase, string?>` (MVC5) → `Func<HttpContext, string?>` (ASP.NET Core).
- Cache moved from the controller base into the chain (no controller base in the origin) — key `TemplateBuilder.Editor.Actor`.
- **Stamps: 4 sites including `Create`** (origin keeps Create-publishes-v1; the fork removed it) vs the fork's 3.
- No snippet stamps/UI fallback (no `SnippetVersion`/`SnippetUsage` in the origin).
- Test framework: Moq + `DefaultHttpContext` (origin convention) vs the fork's NSubstitute.
- Supersedes 2.2.0's hardcoded `CurrentActor` body (2.2.0 wires `CurrentActor` into audit; this feature redefines the property, so audit automatically honors the resolver).
