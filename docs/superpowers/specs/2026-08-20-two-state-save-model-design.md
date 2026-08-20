# Design: Two-state save model (Draft/Active versions, template-level IsActive)

**Date:** 2026-08-20
**Audience:** implementer in the MVC5 fork (this repo). This is a fork-scoped simplification of the governance-phase workflow; the origin .NET 8/10 repo is **not** being changed by this spec (deliberate fork decision — see Fork deviation log).

## Goal

Replace the 5-state governance workflow (Draft → Review → Approved → Published, with submit/approve/reject/cancel/publish actions, editing locks, and background auto-save) with a simple two-state save model:

1. **Only two statuses exist: Draft and Active — and they live on each version**, not on the template.
2. The editor always shows two buttons: **Save Draft** (creates a version marked Draft) and **Save Version** (creates a version marked Active). No locks, no review flow — editing is always allowed.
3. **Template-level `IsActive` stays** as the servable switch: if `IsActive == false`, the template is not servable to the developer API at all.
4. The editor shows the **latest** version (draft included). The developer render API returns the **last Active** version's content — draft saves are invisible to API consumers.

## Decisions (from brainstorming; all approved by the product owner)

| # | Decision | Rationale |
|---|---|---|
| D1 | Status lives **on each version** (`TemplateVersion.IsActive`, bool) | Version history shows which saves were drafts vs active; template's current status = latest version's status |
| D2 | Template-level `IsActive` is kept and means "not servable to the API at all" | Operational on/off switch, separate from save quality |
| D3 | Developer API (`RenderAsync`/`RenderByNameAsync`) throws typed exceptions for nothing-servable: `TemplateNotFoundException` (missing), `TemplateInactiveException` (`IsActive == false`), `NoActiveVersionException` (all versions are drafts / no versions) | Library-style contract; callers get a clear signal instead of silently rendering nothing |
| D4 | Editor shows the latest version regardless of Draft/Active, with a visible "Draft version" badge | Product owner: "if a version is draft, then in edit system shows draft version" |
| D5 | Auto-save is **removed entirely** (`DraftBody`, `/Draft` endpoint, toolbar toggle, localStorage autosave) | Drafts only exist as versions created by the Save Draft button — no hidden state |
| D6 | Create produces a template with **no version** | Nothing exists in history until the user explicitly picks Draft or Active |
| D7 | Existing workflow statuses (Review/Approved/Published) are **dropped**, including the `Template.Status` column and `TemplateStatus` enum | Single source of truth (version flag) replaces the machine |
| D8 | Restore inherits the source version's Draft/Active flag; Duplicate copies the latest version into v1 with the same flag | Restoring/duplicating preserves the save quality of the content being copied |
| D9 | Audit: Save Draft records `draft_saved`; Save Version records `published`. Legacy workflow action constants stay for old history rows but are never recorded again | Audit history remains meaningful and filterable |
| D10 | Export format bumps to `schemaVersion: 2` (per-version `isActive`, template `isActive`, no `status` string); import accepts v2 only and preserves both flags | v1 files were never published (lifecycle phase shipped in this repo only); no legacy import support needed |
| D11 | Import drops the locked-skip and status collapse | Review/Approved no longer exist; import can always update |
| D12 | `ReviewComment` column is dropped with `Status`/`DraftBody` | Review feedback no longer exists |
| D13 | Package version bumps 1.1.0 → **1.2.0** | Breaking change to data model, endpoints, and export format |

## Current state (gap analysis)

- `Template.Status` (`TemplateStatus`: Draft/Review/Approved/Published) drives editor lock mode, workflow buttons, and import skip/collapse (lifecycle-ops spec D4/D5 — superseded here).
- `TemplateWorkflowService` (Application) implements `SaveDraftAsync`, `SubmitForReviewAsync`, `ApproveAsync`, `RejectAsync`, `CancelReviewAsync`, `PublishAsync` — 14 tests in `TemplateWorkflowServiceTests.cs`.
- `TemplatesController` exposes 6 workflow endpoints: `POST /Templates/{id}/Draft`, `/SubmitForReview`, `/Approve`, `/Reject`, `/CancelReview`, `/Publish`, via a shared `RunWorkflow`/`MapWorkflowResult` helper.
- Auto-save: `Template.DraftBody` column, `SaveDraftRequest`, editor JS autosave (localStorage `tb-draft-*` + `/Draft` POST), toolbar toggle.
- `TemplateEngine.RenderAsync(id, model)` and `RenderByNameAsync(name, model)` render `CurrentVersion?.Body` (the latest version) with no `IsActive` enforcement.
- `CurrentVersionId` FK points at the latest version — editor semantics; stays.
- Edit page: status pill, workflow action group, lock banner + `setReadOnly` for Review/Approved; version history cards have no per-version status.
- Promotion (lifecycle-ops): `TemplateExportTemplate.Status` string, `CollapseStatus`, locked-skip in `ImportAsync`; `TemplateImportEntry.Skipped`/skip reason text in import UI.
- `TemplateHealthService.CheckAsync` uses `CurrentVersion?.Body` — latest version, which matches what the editor shows (no change needed).
- Audit filter UI lists action names from the constants — legacy names remain valid for old rows.

## Module 1 — Domain model

### `TemplateVersion`

```csharp
public bool IsActive { get; set; } = true;   // true = Active save, false = Draft save
```

- `true` = created by Save Version, `false` = created by Save Draft.

### `Template`

- **Remove:** `Status` (and delete `TemplateStatus` enum), `DraftBody`, `ReviewComment`.
- **Keep unchanged:** `IsActive`, `CurrentVersionId` (latest version, editor semantics), everything else.

### New exceptions (`TemplateBuilder.Domain.Exceptions`)

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

`TemplateNotFoundException` already exists — unchanged.

### `AuditActions`

- Keep constants `draft_saved`, `published`, and the legacy `submitted`/`approved`/`rejected`/`review_cancelled` (old rows + filter options), but nothing records the legacy ones anymore.

## Module 2 — Application

### Delete workflow service

- Remove `TemplateWorkflowService.cs`, `ITemplateWorkflowService.cs`, `TemplateWorkflowResult.cs` and `TemplateWorkflowServiceTests.cs` (14 tests).

### `ITemplateRepository`

```csharp
Task<TemplateVersion?> GetLastActiveVersionAsync(int templateId, CancellationToken ct = default);
```

- EF6: latest `TemplateVersion` for the template where `IsActive == true`, ordered by `VersionNumber` descending; `null` if none.

### `TemplateEngine` render contract

`RenderAsync(int templateId, ...)` and `RenderByNameAsync(string name, ...)`:

1. Template missing → throw `TemplateNotFoundException`.
2. `template.IsActive == false` → throw `TemplateInactiveException`.
3. Last Active version (via `GetLastActiveVersionAsync`) is null → throw `NoActiveVersionException`.
4. Render that version's body.

`RenderBodyAsync(body, model)` is unchanged (used by editor preview/validate).

### Promotion service (`TemplatePromotionService`)

- **Export shape (schemaVersion 2):**

```json
{
  "schemaVersion": 2,
  "exporter": { "name": "TemplateBuilder.Editor.Mvc5", "version": "1.2.0" },
  "exportedAt": "2026-08-20T12:00:00Z",
  "template": {
    "externalKey": "7f2c4b1e-...",
    "name": "Invoice v3",
    "templateType": "Email",
    "description": "...",
    "sampleData": "...",
    "isActive": true,
    "versions": [
      {
        "versionNumber": 1,
        "body": "<p>Hi {{ model.FirstName }}</p>",
        "changeComment": "Initial version",
        "createdAt": "2026-08-19T09:00:00Z",
        "createdBy": "nchinnam",
        "isActive": true
      }
    ]
  }
}
```

- `TemplateExportVersion` gains `bool IsActive`; `TemplateExportTemplate` loses `Status`; `TemplateExportDocument.SchemaVersion` defaults to 2; `ExporterInfo.Version` = "1.2.0".
- **Import:** accepts only `schemaVersion == 2`; removes `CollapseStatus` and the Review/Approved locked-skip; always updates an existing target (metadata + `IsActive`) and appends versions **preserving each version's `isActive`**. `TemplateImportResult.Skipped` remains in the DTO shape but will simply be empty (keeps the editor UI's entry rendering intact).
- Scriban parse validation of imported bodies stays.

### Health check

- Unchanged: checks `CurrentVersion?.Body` (latest — matches editor display).

## Module 3 — Editor.Mvc5

### Endpoints

- **Remove:** `POST /Templates/{id}/Draft`, `/SubmitForReview`, `/Approve`, `/Reject`, `/CancelReview`, `/Publish`, plus `RunWorkflow`/`MapWorkflowResult` helpers and request models `SaveDraftRequest`, `SubmitForReviewRequest`, `RejectRequest`.
- **`SaveVersion`** gains `isActive` in `SaveVersionRequest`:
  - `isActive: true` → new version `IsActive = true`, audit action `published`.
  - `isActive: false` → new version `IsActive = false`, audit action `draft_saved`.
  - `SourceView` snapshot refresh logic (lifecycle-ops) is unchanged.
- **`CreateTemplateJson`:** remove the initial-version block — create the template only (no `PublishVersionAsync`, no version). Edit opens empty with both save buttons.
- **`RestoreVersion`:** new version inherits the source version's `IsActive`; audit comment unchanged.
- **`Duplicate`:** copies the latest version's body into v1 with the same `IsActive`.
- `Edit` GET: `Body = template.CurrentVersion?.Body` (drop the `DraftBody ??` fallback); view model loses `Status`, gains `LatestVersionIsActive` (for the badge).
- Unity: remove `ITemplateWorkflowService` registration.

### Edit page

- Remove: status pill, workflow button group, lock banner, `window.tbStatus`, lock/`setReadOnly` logic.
- Properties panel keeps Name/Type/Description/Source View; footer becomes:
  - **Save Draft** (secondary) + **Save Version** (primary), always visible once a template exists (create mode keeps its single Create button; after creation the page navigates to edit).
- "Draft version" badge next to the version display when the latest version `IsActive == false`.
- Version history cards show a per-version badge: **Active** or **Draft** (and the existing "current" marker).
- Auto-save JS (localStorage drafts, `/Draft` POST, autosave toggle) removed.

### Index page

- Unchanged: `IsActive` Enable/Disable toggle, bulk bar, health badges, import modal (the import result still renders created/updated/skipped/error entries — skipped just never occurs).

## Module 4 — EF6 migration

`SimplifyVersionStatus`:

- `AddColumn("dbo.TemplateVersions", "IsActive", c => c.Boolean(nullable: false, defaultValue: true))` — backfill `true`: every existing version was a real published save, so legacy history reads as Active.
- `DropColumn("dbo.Templates", "ReviewComment")`, `"DraftBody"`, `"Status"`.
- No data lock-out: templates previously in Review/Approved become editable with no state change needed (their versions are Active).

## Module 5 — Testing & verification

- **Application (TDD):**
  - `TemplateEngineTests`: missing template → `TemplateNotFoundException`; inactive → `TemplateInactiveException`; all-draft → `NoActiveVersionException`; latest-draft + older-active → renders the older active body; both `RenderAsync` and `RenderByNameAsync` covered.
  - `TemplatePromotionServiceTests` / `TemplatePromotionImportTests`: schemaVersion 2 shape (per-version `isActive`, no `status`); v1 file rejected; import preserves version flags + template `isActive`; no skip behavior; update path appends with flags intact.
  - Delete `TemplateWorkflowServiceTests`.
- **EF6:**
  - `TemplateLifecycleColumnsTests` (or a new `TemplateVersionStatusColumnsTests`): new version defaults `IsActive = true`; save-draft persists `IsActive = false`; `GetLastActiveVersionAsync` returns latest active / null when none; migration drops `Status`/`DraftBody`/`ReviewComment` (sqlcmd column check).
- **Editor build:** 0 errors; no references to removed endpoints/models.
- **End-to-end (xsp4 + agent-browser, MEMORY.md recipe):** create (no v1) → Save Draft → badge shows Draft + history card Draft → Save Version → badge Active + history shows v1 Draft / v2 Active → Edit reload shows v2 (latest) → `/Preview` and `/Validate` still work; developer API smoke: `RenderByNameAsync`/`RenderAsync` via a small harness (or tests) return last Active even with a newer draft, throw when inactive/no-active. Import v2 file round-trip preserves flags. Legacy rows (pre-migration DB) render in history as Active.
- **Pack:** nupkg 1.2.0 inspected (no `.cshtml` leakage; new types present); sample host rebuilt from the package; full xsp4 regression (list/create/edit/save/history/restore/duplicate/health/bulk/import/export/audit).

## Out of scope (future work)

- Re-introducing any approval/review workflow.
- Origin-repo parity for this change (deliberate fork divergence).
- Single-template delete (bulk delete only, as before).
- Rollback/migration tooling beyond the EF6 migration.

## Fork deviation log (for commit messages + origin port)

- `Template.Status`/`TemplateStatus`/`DraftBody`/`ReviewComment` removed; `TemplateVersion.IsActive` added — fork-only simplification of the governance workflow; origin repo keeps its workflow and is unaffected.
- `ITemplateWorkflowService` deleted; `TemplateEngine` render contract tightened (typed exceptions + last-active selection); export format schemaVersion 2 is fork-specific.
