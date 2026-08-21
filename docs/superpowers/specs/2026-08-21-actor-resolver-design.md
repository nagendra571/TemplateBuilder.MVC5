# Actor Resolver (Custom CreatedBy) — Design

- **Date:** 2026-08-21
- **Status:** Approved (design), pending implementation plan
- **Scope:** `TemplateBuilder.Editor.Mvc5` NuGet package — public API addition

## Problem

`TemplateBuilder.Editor.Mvc5` persists author identity in `TemplateVersion.CreatedBy`,
`SnippetVersion.CreatedBy`, `SnippetUsage.UsedBy`, and audit `Actor` rows. Today the only
mechanism is `TemplateBuilderControllerBase.CurrentActor =>
User?.Identity?.Name ?? "anonymous"` (`src/TemplateBuilder.Editor.Mvc5/Controllers/TemplateBuilderControllerBase.cs:8`).

Consumers of the NuGet package cannot control this value:

- Apps using claims-based auth (OWIN/claims) where `User.Identity.Name` is empty or not the
  identity they want stored (e.g. they want `sub`, an employee id, an email).
- Apps whose user info lives outside `IPrincipal` (custom user context, session, DB).
- Anonymous-mode hosts that still want a meaningful actor recorded.

Additionally, `TemplateVersion.CreatedBy` is **never populated** even today: the editor's
`SaveVersion`/`RestoreVersion`/`Duplicate` actions build `TemplateVersion` objects without
`CreatedBy`, and `TemplateRepository.PublishVersionAsync` does not set it. Only
`SnippetVersion.CreatedBy` is stamped (via the repo's `actor` parameter).

## Requirement

Consumers must be able to supply their own user identity value (username, user id, claim
value, composite, etc.) from their existing `RegisterTemplateBuilderEditor(options => ...)`
registration. Going forward, that value is stored in **all** TemplateBuilder tables wherever
an actor column applies. **No backfill** of existing records.

## Decisions (approved)

1. **Mechanism: resolver delegate on options** — `TemplateBuilderEditorOptions.ActorResolver`,
   a `Func<HttpContextBase, string?>`. Set in the same `RegisterTemplateBuilderEditor` call
   where the consumer already configures `ConnectionString`. No extra DI wiring.
   (Rejected: `IActorProvider` interface + Unity registration — more consumer ceremony;
   claim-type mapping option — inflexible for user info outside claims.)
2. **Fallback chain:** resolver → `User?.Identity?.Name` → `"anonymous"`. Never throws for a
   null/empty result.
3. **No backfill.** Existing rows keep their current values (null for template versions).
4. **Gap fix going forward:** the editor stamps `CreatedBy = CurrentActor` on every new
   `TemplateVersion` it publishes.
5. **UI verbiage:** version-history display renders `"anonymous"` when `CreatedBy` is null.
6. **Resolver exceptions propagate** — consumer code fails loudly rather than silently
   writing `"anonymous"`.
7. **Truncate** the resolved value to 200 chars (column max) to avoid `DbUpdateException`.

## API Surface

```csharp
// src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorOptions.cs
using System.Web;

public class TemplateBuilderEditorOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public TemplateBuilderAuthorizationOptions Authorization { get; set; } = new();
    public Func<HttpContextBase, string?>? ActorResolver { get; set; }
}
```

Consumer usage:

```csharp
container.RegisterTemplateBuilderEditor(options =>
{
    options.ConnectionString = ...;
    options.ActorResolver = ctx => ctx.User?.FindFirst("sub")?.Value; // claims
    options.ActorResolver = _ => MyAppUserContext.Current?.Id.ToString(); // custom
});
```

## Resolution Semantics

`TemplateBuilderControllerBase.CurrentActor` delegates to a pure, testable helper
(e.g. `ActorResolverChain.Resolve(Func<HttpContextBase, string?>? resolver,
string? identityName, HttpContextBase httpContext)`):

1. If `resolver` is non-null, invoke with the request's `HttpContextBase`.
2. If the result is null/whitespace, fall back to `User?.Identity?.Name`.
3. If still null/whitespace, return `"anonymous"`.
4. Truncate the final value to 200 chars.
5. Resolver exceptions propagate (no swallowing).

The resolver is invoked **once per request** and cached (e.g. `HttpContext.Items` key) because
`CurrentActor` is referenced by ~15 call sites per request.

## Files Touched

| File | Change |
|------|--------|
| `src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorOptions.cs` | Add `ActorResolver` property |
| `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplateBuilderControllerBase.cs` | `CurrentActor` uses resolution chain + per-request cache |
| `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs` | Stamp `CreatedBy = CurrentActor` in `SaveVersion`, `RestoreVersion`, `Duplicate` |
| `src/TemplateBuilder.Editor.Mvc5/Views/Templates/_VersionHistory.cshtml` | `"anonymous"` fallback for null `CreatedBy` |
| `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` | `v.createdBy || 'anonymous'` fallback |
| `tests/TemplateBuilder.Editor.Mvc5.Tests/` (new) | xunit + FluentAssertions + NSubstitute; chain tests |
| `README.md`, `src/TemplateBuilder.Editor.Mvc5/README.md` | "Customizing the author identity (CreatedBy)" section |
| `samples/TemplateBuilder.SampleMvc5Host/App_Start/UnityConfig.cs` | Demonstrate `ActorResolver` |

No Domain/Application/Infrastructure changes (they are verbatim ports; the actor already
flows through their existing `actor` string parameters). No schema change.

## Testing

- New `tests/TemplateBuilder.Editor.Mvc5.Tests` project (repo convention: one test project
  per src project; none exists for the Mvc5 layer today).
- `ActorResolverChain` unit tests: resolver wins; null/whitespace resolver result → identity
  name; both absent → `"anonymous"`; 200-char truncation; whitespace-only identity name.
- Controller stamping is exercised via the chain tests + manual sample verification
  (controllers have no existing test seam; no test project for the Mvc5 layer today).

## Verification

- Build solution; run all test projects.
- Run the sample host (xsp4, as in prior tasks) and confirm: version history shows the
  resolver-supplied value; `"anonymous"` verbiage appears for legacy rows; snippet versions,
  audit rows, and usage rows carry the resolved actor.
- Confirm the nupkg README ships the new section if packing is performed.

## Out of Scope

- Backfilling existing rows.
- Structured actor storage (object/JSON) — columns are `string` (max 200).
- Changes to the origin `TemplateBuilder` repo (standalone fork; this is a fork-only feature).
- Changes to Domain/Application/Infrastructure layers.
