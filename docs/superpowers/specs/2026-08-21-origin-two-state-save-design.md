# Design: Two-state save model (Draft/Active versions) — TemplateBuilder.Editor (origin)

**Date:** 2026-08-21
**Audience:** implementer in the origin repo `github.com/nagendra571/TemplateBuilder` (private). This spec ports the fork's two-state save model (`TemplateBuilder.Mvc5`, already implemented and verified) to the origin with deliberate adaptations. The fork's implementation is the reference implementation — cited per file below.

## Goal

Give TemplateBuilder.Editor the same two-state save model the fork shipped: every version is marked **Draft** or **Active** (`TemplateVersion.IsActive`); the editor always shows **Save Draft** and **Save Version** buttons; the developer render API (`ITemplateEngine.RenderAsync` / `RenderByNameAsync`) serves the **last Active** version — draft saves are invisible to API consumers; template-level `IsActive` remains the servable switch; and nothing-servable conditions throw typed exceptions.

## Decisions (brainstormed with the product owner; all approved)

| # | Decision | Rationale |
|---|---|---|
| D1 | Status lives on each version — `TemplateVersion.IsActive` (`bool`, default `true`; true = Active save, false = Draft save) | Version history shows which saves are drafts vs active; template's current status = latest version's status |
| D2 | Template-level `IsActive` stays and means "not servable to the API at all" | Already the origin's semantics (`GetAllAsync`/`GetCurrentVersionIdAsync` filter on it); unchanged |
| D3 | Render API throws typed exceptions: `TemplateNotFoundException` (missing), `TemplateInactiveException` (`IsActive == false`), `NoActiveVersionException` (no Active version exists) — and serves the **last Active** version, never the latest draft | Library-style contract. **BREAKING**: the origin currently throws `TemplateNotFoundException` for inactive templates — consumers catching that for inactive templates must migrate (see Module 5) |
| D4 | Editor shows the latest version regardless of Draft/Active, with a visible "Draft version" badge; version history cards show per-version Active/Draft badges | Product owner: "if a version is draft, then in edit system shows draft version" |
| D5 | **Autosave is KEPT as-is** (the origin's localStorage-only autosave, since v1.2.0) | Origin feature users rely on; it is "unsaved work recovery", distinct from Save-Draft versions. The autosave buffer is cleared on any version save (already the behavior) |
| D6 | **Create behavior is KEPT**: the form POST still publishes v1 when the body is non-empty; the initial version is Active (`IsActive` defaults true) | Origin behavior since v1.3.1 ("Edit screen opens with content intact"); no product complaint |
| D7 | No status machinery to remove | The origin never had `TemplateStatus`/Review/Approved/Published — this is a pure addition, unlike the fork |
| D8 | Restore and Duplicate **inherit** the source version's `IsActive` (`?? true` null-safety) | Restoring/duplicating preserves the save quality of the content being copied |
| D9 | **No audit wiring** | The origin has no audit log (the fork's governance audit is fork-native); SaveVersion does not record audit rows |
| D10 | Version bump: **2.0.0** (breaking — SemVer) | D3 changes a public exception contract; consumers must opt in |
| D11 | Export/import format: see the lifecycle spec (`2026-08-21-origin-lifecycle-ops-design.md`); the two-state save lands first so lifecycle's per-version `isActive` export has a source | Features are sequenced: two-state → lifecycle |

## Current state (origin gap analysis — verified against `main`, commit 194cf15, ~1.5.2-era)

> Note: NuGet 1.6.0 (SampleData endpoints, palette search) is published but **not** on `main`. This spec targets `main` as-is. The implementer should `git pull` at start and rebase over anything newer that landed.

- **Domain** (`src/TemplateBuilder.Domain/Entities/`):
  - `Template.cs`: `Id, Name, TemplateType, Description, CurrentVersionId, IsActive, CreatedAt, UpdatedAt, RowVersion, Versions, CurrentVersion`. No `SampleData`, no `Status`/`DraftBody`/`ReviewComment`, no `ExternalKey`/`SourceView`.
  - `TemplateVersion.cs`: `Id, TemplateId, VersionNumber, Body, ChangeComment, CreatedAt, CreatedBy`. **No `IsActive`.**
  - Exceptions (`Exceptions/`): `TemplateNotFoundException`, `TemplateRenderException`, `SchemaVersionMismatchException`.
- **Repository** (`src/TemplateBuilder.Infrastructure/Repositories/TemplateRepository.cs`, interface in `Domain/Interfaces/ITemplateRepository.cs`):
  - `GetByIdAsync`/`GetByNameAsync`/`GetAllAsync`: `AsNoTracking()` + `.Include(t => t.CurrentVersion)`.
  - **`GetAllAsync` filters `Where(t => t.IsActive)`**; **`GetCurrentVersionIdAsync` filters `t.IsActive`** (inactive → null → engine throws `TemplateNotFoundException` today).
  - `GetVersionHistoryAsync` (desc), `GetNextVersionNumberAsync`, `CreateAsync`, `UpdateTemplateAsync`, `PublishVersionAsync`. No `DeleteAsync`.
- **Engine** (`src/TemplateBuilder.Application/Services/TemplateEngine.cs`):
  - `RenderAsync(templateId, model)`: `GetCurrentVersionIdAsync` → null → `TemplateNotFoundException`; then `GetBodyAsync(templateId, currentVersionId)`.
  - `RenderByNameAsync(name, model)`: `GetByNameAsync` → `template is null || !template.IsActive` → `TemplateNotFoundException`; then same body path.
  - **Body caching**: `IMemoryCache`, key `tb_{templateId}`, `CacheEntry(VersionId, Body)` record; cache hit only when `cached.VersionId == currentVersionId`; else evict + `GetOrCreateAsync` (stampede-safe). Options: `EnableCaching` (default true), `CacheDurationMinutes` (30).
  - **Case-insensitive model access** (`CaseInsensitiveScriptObject`) already present — nothing to port.
- **DI**:
  - Editor: `src/TemplateBuilder.Editor/ServiceCollectionExtensions.cs` `AddTemplateBuilderEditor(...)` registers DbContext, `ITemplateRepository`, `ISnippetRepository`, `IHtmlSanitizerService`, `ITemplateEngine`, `ISqlViewDiscoveryService`, `MigrationHostedService`, authorization convention.
  - Core (render-only package): `src/TemplateBuilder.Core/Extensions/ServiceCollectionExtensions.cs` `AddTemplateBuilder(...)` registers DbContext, repos, `ITemplateEngine`, memory cache. **The render contract change applies to BOTH packages** (same `TemplateEngine` class).
- **Editor UI** (Razor RCL — `.cshtml` ships in the package, edited directly; no RazorGenerator):
  - `Views/Templates/Edit.cshtml`: `btn-autosave-toggle` (canvas heading), `tb-draft-banner` + `btn-draft-restore`/`btn-draft-discard`, `validate-panel`, `version-display` (`v @Model.CurrentVersionNumber`) + `btn-history`, `save-comment`, footer `btn-preview` + **`btn-save`** ("Save Version", primary) for existing templates / `btn-create` (form submit) for new. Inline script: `const templateId = @(Model.Id?.ToString() ?? "null"); const currentVersionNumber = @Model.CurrentVersionNumber;`. SunEditor loaded from CDN (`cdn.jsdelivr.net/npm/suneditor@2.47.10`).
  - `Views/Templates/Index.cshtml`: list table (Name/Type/Version/Updated/Status badges `tb-badge-live`/`tb-badge-draft` + Enable/Disable `toggleActive`), stats sidebar, `_csrf` const, inline duplicate modal.
  - `Views/Templates/_VersionHistory.cshtml`: version cards, "Current" badge, Compare/Restore buttons.
  - `wwwroot/js/template-editor.js` (~2,098 lines): `saveVersion()` at ~520 (POST `/Templates/{id}/SaveVersion`, JSON body incl. `changeComment`; on success updates `version-display`, calls `clearDraft(); markClean(); showToast('Version saved')`), binding `btn-save` at ~1456; autosave module at ~2008 (`DRAFT_KEY`, `AUTOSAVE_PREF_KEY`, `saveDraft` localStorage-only, `loadDraft` banner, `setInterval(saveDraft, 60s)`); `_csrf` top-level const; `restoreVersion(this, id, num)` + `openCompareView(btn)` for history/compare.
  - `wwwroot/css/template-editor.css` (~1,356 lines), all selectors scoped `#tb-editor-host`, token system (light/dark).
- **Models** (`src/TemplateBuilder.Editor/Models/`): `SaveVersionRequest` is a **record** `(string Name, string TemplateType, string? Description, string Body, string? ChangeComment)`; `TemplateEditorViewModel` (Id/Name/TemplateType/Description/Body/CurrentVersionId/CurrentVersionNumber/AvailableViews); `TemplateListViewModel`; `DuplicateRequest(string NewName)`.
- **Controller** (`src/TemplateBuilder.Editor/Controllers/TemplatesController.cs`): `[ValidateAntiForgeryToken]` on JSON POSTs ([FromBody] — the JS sends the `RequestVerificationToken` header, which ASP.NET Core validates natively); `SaveVersion` returns `Ok(new { versionId, versionNumber })`; `Create` is a **form POST** (`Create(TemplateEditorViewModel model)` + `PublishVersionAsync` v1 when body non-empty); `RestoreVersion(id, versionId, sourceVersionNumber)` fetches body via `GetVersionBodyAsync`; `Duplicate` copies `source.CurrentVersion.Body`.
- **EF Core** (`src/TemplateBuilder.Infrastructure/`): DbContext + `Data/Configurations/*Configuration.cs` (`IEntityTypeConfiguration`, applied via `ApplyConfigurationsFromAssembly`); migrations `20260513222213_InitialCreate`, `20260601180334_AddSnippets` + snapshot; **design-time factory exists** (`TemplateBuilderDbContextFactory`, hardcoded connection string — `dotnet ef migrations add` works without a startup project); `MigrationHostedService` applies migrations on startup.
- **Tests**: `tests/TemplateBuilder.Application.Tests/Services/TemplateEngineTests.cs` (Moq + FluentAssertions; engine constructed with real `MemoryCache` + `Options.Create`); `tests/TemplateBuilder.Editor.Tests/Controllers/TemplatesControllerTests.cs` (Moq, `CreateController` helper); `tests/TemplateBuilder.Infrastructure.Tests/Repositories/TemplateRepositoryTests.cs` (**InMemory** provider, `Guid`-named DB, `ConfigureWarnings` ignoring the transaction warning).
- **e2e**: `src/TemplateBuilder.Web` sample app runs at `https://localhost:7275/`; `GET /Templates/_setup` diagnostics; `TemplateBuilder.Client` is a NuGet-consumer-style sample.
- **Build**: `TemplateBuilder.slnx` (SDK-style solution); multi-target `net8.0;net10.0` everywhere.

## Reference implementation (fork)

The fork implemented this exact feature and verified it end-to-end (commits `785aa9e`..`b2d0c1a` in `github.com/nagendra571/TemplateBuilder.MVC5`). Port by mapping stack constructs:

| Fork (MVC5/EF6/Newtonsoft) | Origin (ASP.NET Core/EF Core/System.Text.Json) |
|---|---|
| `TemplateVersion.IsActive` | same property; EF Core config in `Data/Configurations/TemplateVersionConfiguration.cs` |
| `TemplateInactiveException`/`NoActiveVersionException` (Domain.Exceptions) | copy verbatim (fork: `src/TemplateBuilder.Domain/Exceptions/`) |
| `GetLastActiveVersionAsync` (EF6 LINQ) | EF Core: `_context.TemplateVersions.Where(v => v.TemplateId == id && v.IsActive).OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync(ct)` |
| `SaveVersionRequest.IsActive` (class, default true) | **record + `bool? IsActive = null`** — see Module 3 gotcha |
| Newtonsoft camelCase `Content(...)` | System.Text.Json: `Ok(new {...})`, `[FromBody]` records, `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }` |
| EF6 `AddColumn(defaultValue: true)` | EF Core migration `AddColumn<bool>(defaultValue: true)` |
| RazorGenerator precompiled views | RCL `.cshtml` edited directly |

## Module 1 — Domain model

### `TemplateVersion`

```csharp
public bool IsActive { get; set; } = true;   // true = Active save, false = Draft save
```

### New exceptions (`src/TemplateBuilder.Domain/Exceptions/`)

```csharp
public class TemplateInactiveException : Exception
{
    public TemplateInactiveException(int templateId)
        : base($"Template {templateId} is inactive and not servable.") { }
}

public class NoActiveVersionException : Exception
{
    public NoActiveVersionException(int templateId)
        : base($"Template {templateId} has no active version to serve.") { }
}
```

### `ITemplateRepository` — two additions

```csharp
Task<TemplateVersion?> GetLastActiveVersionAsync(int templateId, CancellationToken ct = default);
Task<TemplateVersion?> GetVersionAsync(int versionId, CancellationToken ct = default);  // single version (Restore needs the source's IsActive)
```

## Module 2 — Application (render contract + cache interplay)

### Contract (both `RenderAsync` and `RenderByNameAsync`)

1. Template missing → `TemplateNotFoundException`.
2. `template.IsActive == false` → `TemplateInactiveException`. (BREAKING: previously `TemplateNotFoundException`.)
3. `GetLastActiveVersionAsync` → null → `NoActiveVersionException`.
4. Render that version's body.

`RenderBodyAsync(body, model)` unchanged (editor preview/validate).

### Cache interplay (the subtle part — read carefully)

Today the cache is keyed `tb_{templateId}` with `CacheEntry(VersionId, Body)`, and validity is `cached.VersionId == currentVersionId`. After this change the engine must resolve the **last Active version id** instead of `CurrentVersionId`:

- A **draft save** changes `Template.CurrentVersionId` but NOT the last active version id → the cache entry still matches → **the cached active body is served, no re-fetch** (and drafts are never served).
- An **active save** changes the active version id → mismatch → evict + refetch (existing path).

Implementation: replace the `GetCurrentVersionIdAsync` call in `RenderAsync`/`RenderByNameAsync` with `GetLastActiveVersionAsync(...)` and pass its `Id` to `GetBodyAsync(templateId, activeVersionId, ct)`. Keep `GetCurrentVersionIdAsync` (editor/history paths use it; its IsActive filter is unrelated). The cache key stays `tb_{templateId}` — no key change needed because the cache already stores the version id and compares.

### Core package

`TemplateBuilder.Core` (render-only, `AddTemplateBuilder`) shares `TemplateEngine` — the contract change propagates automatically. Version the Core package too (2.0.0) and note it in its README.

## Module 3 — Editor (endpoints + UI)

### `SaveVersionRequest` — record + nullable bool gotcha

The origin binds JSON to a **record** via System.Text.Json constructor deserialization. A plain `bool IsActive = true` positional parameter is **not safe**: a payload that omits `isActive` can bind to `false` (value-type default), silently creating draft versions for old clients. Use:

```csharp
public record SaveVersionRequest(
    string Name,
    string TemplateType,
    string? Description,
    string Body,
    string? ChangeComment,
    bool? IsActive = null);
```

and in the controller: `IsActive = request.IsActive ?? true`. Add a bind-behavior test (Module 5).

### `SaveVersion` endpoint

- New version `IsActive = request.IsActive ?? true`.
- Response: `Ok(new { versionId, versionNumber, isActive })` (the JS badge refresh consumes `isActive`).
- Everything else (name/type/description/source-view snapshot if present) unchanged.

### `RestoreVersion`

Source version now fetched via `GetVersionAsync(versionId)` (gives `IsActive`); new version inherits `source.IsActive` (`?? true` fallback when not found — mirrors current 404 behavior).

### `Duplicate`

New v1 inherits `source.CurrentVersion?.IsActive ?? true`.

### `Edit` GET

`TemplateEditorViewModel` gains `public bool LatestVersionIsActive { get; set; } = true;`, set from `template.CurrentVersion?.IsActive ?? true`. `Body`/`CurrentVersionNumber` unchanged (latest version, drafts included — D4).

### `Edit.cshtml`

- Keep `btn-autosave-toggle`, `tb-draft-banner`, `btn-create` (D5/D6).
- Edit mode footer becomes: `btn-preview` (secondary) · **`btn-save-draft`** (secondary, "Save Draft") · **`btn-save`** (primary, "Save Version" — id unchanged to minimize churn).
- Draft badge next to `#version-display`: `@if (!isNew && !Model.LatestVersionIsActive) { <span id="draft-version-badge" class="tb-badge tb-badge-draft">Draft version</span> }`.
- Inline script unchanged (keep `templateId`, `currentVersionNumber`).

### `_VersionHistory.cshtml`

Inside each version card header, after the Current badge: `<span class="tb-badge @(v.IsActive ? "tb-badge-live" : "tb-badge-draft")">@(v.IsActive ? "Active" : "Draft")</span>`.

### `template-editor.js`

- `saveVersion(isActive)`; body gains `isActive`; on success update `version-display` and add/remove `#draft-version-badge` from `data.isActive` (create element when absent, remove when active — mirror the fork's JS).
- Bindings: `btn-save-draft` → `saveVersion(false)`, `btn-save` → `saveVersion(true)`.
- **Autosave interplay (D5)**: the existing `clearDraft()` on success already clears the localStorage buffer after any version save — keep it for BOTH buttons (saving a draft version supersedes the unsaved buffer). `loadDraft`'s `draft.versionNumber !== currentVersionNumber` guard still works (any version save bumps the number). No other autosave changes.
- `_csrf` already sent as `RequestVerificationToken` header — no antiforgery work.

### `template-editor.css`

Reuse existing badge classes (`tb-badge-live`, `tb-badge-draft` already style Active/Inactive on the index page). Add only:

```css
#tb-editor-host #draft-version-badge { margin-left: 6px; vertical-align: middle; }
#tb-editor-host .tb-version-header .tb-badge { margin-left: 6px; }
```

## Module 4 — EF Core migration

`AddVersionIsActive` (scaffold with `dotnet ef migrations add AddVersionIsActive --project src/TemplateBuilder.Infrastructure`):

```csharp
migrationBuilder.AddColumn<bool>(
    name: "IsActive",
    table: "TemplateVersions",
    type: "bit",
    nullable: false,
    defaultValue: true);
```

Legacy rows backfill to Active (they were the served versions). No other schema change.

## Module 5 — Testing & verification

- **Application.Tests** (`TemplateEngineTests.cs`): the two existing tests that mock `GetCurrentVersionIdAsync` (`RenderAsync_ValidTemplate_FetchesBodyAndRenders`, `RenderAsync_UnknownTemplateId_ThrowsTemplateNotFoundException`) must be REWRITTEN to the new contract. New tests:
  - `RenderAsync`/`RenderByNameAsync`: inactive → `TemplateInactiveException`; no active version → `NoActiveVersionException`; latest-is-draft → renders the older active body; latest-active → renders it.
  - **Cache**: after a draft save (active version id unchanged), the engine returns the cached active body **without a repository body re-fetch** (assert `GetVersionBodyAsync` call count via Moq `Verify`); after an active save (id changed) a re-fetch happens.
- **Editor.Tests** (`TemplatesControllerTests.cs`): `SaveVersion` passes `IsActive` through (true/false/absent→true) and returns `isActive` in the payload; `RestoreVersion`/`Duplicate` inherit the flag; `Edit` GET sets `LatestVersionIsActive`. Add a JSON bind test for `SaveVersionRequest` (absent `isActive` → null → controller defaults true) using `System.Text.Json` round-trip or a controller-level test with an actual body.
- **Infrastructure.Tests** (`TemplateRepositoryTests.cs`, InMemory): `GetLastActiveVersionAsync` returns latest active skipping drafts / null when none; `GetVersionAsync` returns a single version.
- **e2e** (`TemplateBuilder.Web` at `https://localhost:7275/`): create (v1 Active) → Save Draft (v2, "Draft version" badge + history badge) → Save Version (v3 Active, badge gone) → Edit reload shows v3 → history shows v1 Active / v2 Draft / v3 Active → **developer API harness**: a small console or test referencing the packaged DLLs renders the last Active body with a newer draft present; `RenderByNameAsync` on an inactive template throws `TemplateInactiveException`; all-draft template throws `NoActiveVersionException`. Also confirm autosave still works (type, reload, "Unsaved draft found" banner restores).
- **Pack**: `dotnet pack` both packages; inspect the nupkgs (no `.cshtml` leakage — the RCL ships views intentionally; verify the DLL set and that the README's "What's New" reflects 2.0.0 — the repo's documented lesson: README must be in sync with the package).

## Versioning

- `TemplateBuilder.Editor` → **2.0.0**; `TemplateBuilder.Core` → **2.0.0** (same breaking render contract). The lifecycle-ops feature (next spec) ships as 2.1.0 if published later, or folds into 2.0.0 if released together — owner decides at release time.

## Out of scope

- Removing/changing the origin's autosave (D5) or Create-publishes-v1 behavior (D6).
- Adding an audit log (D9).
- Export/import, health check, bulk ops — the next feature (lifecycle spec).
- SampleData (1.6.0, not on `main`) — untouched.

## Port/fork deviation log

- Kept autosave + Create-publishes-v1 (fork removed/changed both) — deliberate, owner-approved.
- No audit wiring (fork audited `draft_saved`/`published`) — origin has no audit log.
- `SaveVersionRequest.IsActive` is `bool?` (System.Text.Json record binding) vs the fork's `bool = true` (Newtonsoft class binding).
- `GetVersionAsync(int versionId)` added for Restore's flag lookup (the fork scanned history instead — a full-history fetch is wasteful on the origin's untracked query path).
- New exceptions are breaking vs the origin's current `TemplateNotFoundException`-for-inactive behavior.
