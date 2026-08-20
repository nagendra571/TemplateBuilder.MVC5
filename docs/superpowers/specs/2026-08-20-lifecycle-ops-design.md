# Design: Lifecycle & Ops — export/import promotion, template health check, bulk operations

**Date:** 2026-08-20
**Audience:** implementer in the MVC5 fork (this repo) first; a port of this spec is handed to an implementer in the origin .NET 8/10 repo (`TemplateBuilder`). Platform-specific notes are called out where EF6 vs EF Core, MVC5 vs ASP.NET Core, and net48 vs net8 differ.

## Goal

Add the third phase of the product roadmap — lifecycle & operations tooling — on top of the existing editor, workflow, and audit features:

1. **Export/import (JSON, incl. version history)** — promote templates from one environment (dev) to another (prod) via a versioned JSON file format, matched by a stable external key.
2. **Template health check** — detect field drift between template bodies and the live SQL view schema, severity-classified, surfaced in the editor, on the index page, and on a global Health page.
3. **Bulk operations** — checkbox multi-select on the index page with Activate / Deactivate / Export (ZIP) / Delete.

## Decisions (from brainstorming; all approved by the product owner)

| # | Decision | Rationale |
|---|---|---|
| D1 | Single-template export files; bulk export = ZIP of those files | Per-file granularity for promotion; ZIP is the natural multi-file transport |
| D2 | `Template.ExternalKey` (Guid) as the cross-environment identity | Survives renames; unambiguous dev→prod link |
| D3 | Import upsert: match by key → update-in-place (append versions); no match → create | Promotion is a repeatable refresh operation |
| D4 | Locked targets (Review/Approved) are **skipped** by import | Import can never clobber a template mid-approval |
| D5 | Status collapse on import: Draft→Draft, Published→Published, Review/Approved→Draft | Prod always runs its own approval gate |
| D6 | Health check parses template bodies with the **Scriban AST** (not regex) | No false positives from string literals/comments; nested paths and loop collections covered |
| D7 | `Template.SourceView` records which SQL view a template is built against | Drift comparison needs a declared "source of truth" per template |
| D8 | Import transport = multipart file upload (v1) | MVC5 model-binder friendly; format stays API-friendly for a later JSON endpoint |
| D9 | Bulk ops: Activate / Deactivate / Export / Delete only | Housekeeping story; no risky bulk edits |

## Current state (gap analysis)

- `Template` has `RowVersion`, `Status` (Draft/Review/Approved/Published), `SampleData`, `IsActive`, version history via `TemplateVersion` (governance spec). `Snippet` has version history + usage tracking.
- **No template delete exists at all** — the governance spec listed a `deleted` audit action but neither `ITemplateRepository.DeleteAsync` nor a delete route was implemented. Bulk Delete (and a future single delete) must add it.
- No export/import, no stable identity (integer Ids are environment-local), no view binding on templates, no health/schema checking anywhere.
- JSON endpoints use `Content(JsonConvert.SerializeObject(...), "application/json")` (Newtonsoft) — new endpoints follow that convention.
- RazorGenerator precompiles views (codegen regenerates on build — BLOCKERS #10); new views must be created like existing ones.
- The audit pattern (governance spec Module 2) records mutations only; reads/checks are never audited.

## Module 1 — Export / Import (promotion)

### File format (schemaVersion 1)

```json
{
  "schemaVersion": 1,
  "exporter": { "name": "TemplateBuilder.Editor", "version": "1.1.0" },
  "exportedAt": "2026-08-20T12:00:00Z",
  "template": {
    "externalKey": "7f2c4b1e-...",
    "name": "Invoice v3",
    "templateType": "Email",
    "description": "...",
    "sampleData": "{ \"FirstName\": \"Jane\" }",
    "isActive": true,
    "status": "Published",
    "versions": [
      {
        "versionNumber": 1,
        "body": "<p>Hi {{ model.FirstName }}</p>",
        "changeComment": "Initial version",
        "createdAt": "2026-08-19T09:00:00Z",
        "createdBy": "nchinnam"
      }
    ]
  }
}
```

Rules:

- `versions` is ordered by `versionNumber` ascending and must be non-empty (a template always has ≥ 1 version).
- **Audit log rows never travel** — they are environment-specific records.
- **`DraftBody` and `ReviewComment` are not exported** — work-in-progress state stays environment-local; only published version bodies travel.
- **Snippets are not included** in v1 — templates have no persistent snippet references (insertion is copy-paste); documented as a future extension.
- The format is additive-by-design: importers ignore unknown top-level/template fields they don't understand (forward compatibility), but reject `schemaVersion` > their supported version.
- File naming: `{sanitized-name}.template.json` (sanitize `[^\w\-\.]` → `_`, trim, cap 80 chars).

### Identity: `Template.ExternalKey`

- New column `ExternalKey` `uniqueidentifier NOT NULL`, unique index. Assigned automatically on template create; **never** regenerated, **never** editable from the UI, **never** part of Duplicate (duplicate gets a new key — it's a new template).
- Migration backfills existing rows: `UPDATE Templates SET ExternalKey = NEWID()` (EF6: `Sql(...)` inside the migration; EF Core: `migrationBuilder.Sql(...)` — identical SQL).
- EF6 fluent: `Property(x => x.ExternalKey).IsRequired().HasColumnAnnotation("Index", new IndexAnnotation(new IndexAttribute { IsUnique = true }))` or `HasIndex(x => x.ExternalKey).IsUnique()` (EF 6.1+). EF Core: `HasIndex(x => x.ExternalKey).IsUnique()`.

### Endpoints

| Route | Method | Body/params | Response |
|---|---|---|---|
| `Templates/Export/{id:int}` | GET | — | File attachment `{name}.template.json`, `application/json` |
| `Templates/Import` | POST | multipart form field `file` | `ImportResult` JSON |
| `Templates/BulkExport` | POST | JSON `{ ids: [int] }` (antiforgery header) | ZIP attachment `template-builder-export.zip` |

### Import semantics (single file, whole-file transaction)

1. Parse + validate shape: `schemaVersion` present and ≤ 1; `template` present; `name`/`templateType` non-empty; `versions` non-empty. Violations → whole file rejected with a user-readable message.
2. Scriban parse-check **every** version body (`Template.Parse(body).HasErrors` → reject whole file; report the failing version number). Note: bodies are **not** sanitized at import — storage has never sanitized; sanitization is a render-time concern (same trust model as typing in the editor).
3. Look up target by `ExternalKey`.
   - **No match** → create a new `Template` with the file's external key, metadata, `isActive`, collapsed status, `SampleData`, and all versions preserving their original `versionNumber`s (1..N — the sequence is empty so no conflicts). `CreatedAt`/`UpdatedAt` = import time; version `CreatedAt`/`CreatedBy` preserved from the file.
   - **Match, target status ∈ {Draft, Published}** → update metadata (name/type/description/sampleData/isActive), status = collapsed imported status, and **append** the imported versions as new history entries: version numbers continue from the target's `MAX(VersionNumber)+1` sequence; bodies/change comments/`createdBy` from the file (suffixed change comment: `"Imported from {exporter.name} ({exportedAt})"`); `CreatedAt` = import time. Target's existing history is untouched (prod history is never lost).
   - **Match, target status ∈ {Review, Approved}** → **skip** with reason `"Target is {status} (locked)"`. No writes.
4. Each mutation records an audit row: action `imported` (new constant), `AfterState` JSON `{ file, externalKey, versionsImported }`, actor = current user. Skips are reported, not audited.
5. Result report (returned to the UI, not stored):

```json
{
  "created": [ { "name": "Invoice v3", "externalKey": "7f2c…" } ],
  "updated": [ { "name": "Payment Notice", "versionsAppended": 2 } ],
  "skipped": [ { "name": "Quarterly Report", "reason": "Target is Review (locked)" } ],
  "errors": [ { "fileName": "...", "reason": "schemaVersion 99 not supported" } ]
}
```

The import endpoint accepts a single file (`file` field); per-file validation errors are reported in `ImportResult.errors`.

### Status collapse rule (D5)

| Exported status | Imported status (create) | Imported status (update) |
|---|---|---|
| Draft | Draft | Draft |
| Published | Published | Published |
| Review | Draft | Draft |
| Approved | Draft | Draft |

### Service / repository placement (fork decision, mirrored by origin)

- `Application`: `ITemplatePromotionService` + `TemplatePromotionService` (export document building, import validation + orchestration, status collapse, ZIP packaging for bulk — ZIP via `System.IO.Compression`, available on both net48 and net8). DTOs: `TemplateExportDocument`, `TemplateImportResult`.
- `Domain`: new `ITemplatePromotionRepository` interface + `Template.ExternalKey` property + `AuditActions.Imported`. Methods: `GetByExternalKeyAsync(string)`, `AddWithVersionsAsync(Template, IReadOnlyList<TemplateVersion>)` (atomic), `AppendVersionsAsync(int templateId, IReadOnlyList<TemplateVersion>)` (atomic, returns the assigned numbers), `GetByIdWithVersionsAsync(int)` (for export — or reuse existing `GetByIdAsync` + `GetVersionHistoryAsync`).
- `Infrastructure.EF6`: `TemplatePromotionRepository` implementation; EF Core: equivalent implementation over the origin's DbContext.
- `Editor.Mvc5`: `PromotionController`-routed endpoints on `TemplatesController` (keeps the existing route prefix), Import UI.
- Unity registration: `ITemplatePromotionRepository` → per-request; `ITemplatePromotionService` → per-request.

## Module 2 — Health check (field drift)

### View binding: `Template.SourceView`

- New nullable column `SourceView` `nvarchar(200)`. Set automatically whenever sample data is generated from a view (existing `POST Templates/Api/SampleData/Generate` sets it on the template), and editable via a Properties-panel dropdown populated from the view-discovery service (empty option = unbound).
- No FK to a view catalog (views live outside the app DB); just a name string.

### Token extraction (D6 — Scriban AST walk)

- `Scriban.Template.Parse(body)` (already the engine's API). Walk the statement tree via node `.Children` recursively.
- Record every **leaf member-access path rooted at the `model` global**: a `ScriptMemberExpression` whose target chain bottoms out in `model`, that is not itself the target of another member expression. `{{ model.FirstName }}` → `FirstName`; `{{ model.User.Name }}` → `User.Name`; `for item in model.Items` → `Items`; `{{ model.Total | math.round }}` → `Total`.
- String literals, comments, and HTML text are structurally excluded (no regex scanning).
- Case-insensitive column matching (SQL identifier semantics); findings report the token's original casing.

### Findings taxonomy

| Severity | Code | Condition |
|---|---|---|
| Critical | `view_missing` | `SourceView` set but the view no longer exists in `INFORMATION_SCHEMA.VIEWS` |
| Critical | `column_missing` | Token leaf path has no matching column in the bound view (case-insensitive). A dotted token like `User.Name` matches a column literally named `User.Name`; otherwise it is reported as missing — templates with nested object models should bind columns through flat tokens |
| Warning | `column_type_changed` | Column exists but `DATA_TYPE` differs from the recorded snapshot ("expectations" below). A type change also suppresses the redundant `column_length_changed` for the same column |
| Warning | `column_length_changed` | `CHARACTER_MAXIMUM_LENGTH` differs from a recorded expectation |
| Warning | `column_nullability_changed` | `IS_NULLABLE` differs from a recorded expectation |
| Warning | `unbound_tokens` | `SourceView` is null but the body uses `model.*` tokens |
| Info | `unbound_no_tokens` | `SourceView` is null and no tokens — template not schema-checkable |
| Info | `view_extra_columns` | Bound view has columns the template never references (informational, not noise-critical) |

**Expectations:** v1 records type/length/nullability expectations in the template only if we snapshot them. Simplest honest v1: type/length/nullability comparisons are computed against **snapshots captured at export/sample-generation time** — store a `SourceViewSnapshot` JSON string on `Template` (nullable) refreshed automatically whenever `SourceView` is set (from the already-cached discovery data, same `SqlColumnInfo` shape: name/dataType/maxLength/isNullable). The check diffs live schema vs this snapshot: `column_type_changed`, `column_length_changed`, `column_nullability_changed` all fall out of the diff, and `column_missing`/`view_missing` come from live lookup alone. The snapshot is invisible in the UI (shown as "last synced with schema" timestamp) and travels with export? **No** — snapshots are environment-local (prod's schema may legitimately differ); they are NOT exported. The health check in prod re-syncs its own snapshot when a view is (re)bound.

### Endpoints

| Route | Method | Response |
|---|---|---|
| `Templates/{id:int}/Health` | GET | `TemplateHealthReport` JSON (findings list, severity counts, bound view, snapshot age) |
| `Health` | GET | Health page (server-rendered, scans all templates; view metadata is cached by the discovery service so this is cheap) |
| `Health/Summaries` | GET `?ids=1,2,3` | per-template `{ id, severity: healthy|warning|critical, findingCount }` JSON for index badges |

Health checks are reads — never audited. No persistence of results (computed on demand).

### UI

- **Editor**: "Health" button beside Preview/Save Version in Properties; inline findings panel (reuse the validate-panel pattern): severity-colored finding lines with line numbers where the parser reports them (line numbers come from node spans — v1 may omit line numbers if spans prove unreliable, showing token names instead). Properties gains the "Source SQL View" select (auto-set note shown when bound via sample-data generation).
- **Index page**: per-row health badge (Healthy green / N warnings amber / N issues red / — unbound gray; severity precedence critical > warning > healthy), fed by `Health/Summaries` for the current page's ids.
- **Health page** (`/Health`, nav link next to Audit): summary chips (Healthy/Warnings/Critical/Unbound), table rows (template, source view, severity chip, finding summary, Re-check button), expandable finding details.

## Module 3 — Bulk operations

### Index page mechanics

- Checkbox column + header select-all (selects the current page's rows only — selection is page-local, stated in the toolbar).
- Contextual toolbar appears when selection > 0: "N selected · Activate · Deactivate · Export ZIP · Delete · Clear". Delete is a confirm dialog showing the count.
- Selection survives nothing — page navigation clears it (v1 simplicity).

### Endpoints (all JSON-in, JSON-out, antiforgery header pattern from the editor JS)

| Route | Method | Body | Semantics |
|---|---|---|---|
| `Templates/BulkActivate` | POST | `{ ids: [] }` | Set `IsActive=true`; already-active items are no-ops (succeeded) |
| `Templates/BulkDeactivate` | POST | `{ ids: [] }` | Set `IsActive=false`; already-inactive are no-ops |
| `Templates/BulkExport` | POST | `{ ids: [] }` | ZIP stream `template-builder-export.zip` of per-template export files (Module 1 format). The ZIP includes a `_summary.json` manifest listing which ids exported successfully and which failed validation (missing/deleted ids) |
| `Templates/BulkDelete` | POST | `{ ids: [] }` | Hard delete each template + its versions. Requires the **new** `ITemplateRepository.DeleteAsync` (see gap analysis). Allowed from any workflow status (single-role model per governance spec). Audit action `deleted` per item (already in `AuditActions`) |

- Each endpoint processes items independently: per-item try/catch → `{ succeeded: [ids], failed: [{ id, reason }] }`. One bad id never aborts the batch.
- Mutations are audited per item with existing actions (`toggled_active`, `deleted`).
- Concurrency v1: no `RowVersion` sent with bulk calls (the user just loaded the page; the window is small). `DbUpdateConcurrencyException` per item → reported in `failed` with reason `CONFLICT`. Documented limitation; single-row actions retain full concurrency control.
- `Deactivate`/`Delete` on locked (Review/Approved) templates: allowed (governance spec permits delete from any state; deactivation is orthogonal to workflow).

## Module 4 — Data model & migration summary

| Change | Type | Notes |
|---|---|---|
| `Template.ExternalKey` | `uniqueidentifier NOT NULL`, unique index | Backfilled with `NEWID()` in migration; assigned in `TemplateRepository.CreateAsync` (and the promotion create path) |
| `Template.SourceView` | `nvarchar(200) NULL` | Set by sample-data generation + editor dropdown |
| `Template.SourceViewSnapshot` | `nvarchar(max) NULL` | JSON array of `SqlColumnInfo` (name/dataType/maxLength/isNullable) + capture timestamp; refreshed when `SourceView` changes |
| `AuditActions.Imported` | constant `"imported"` | New audit action |
| `ITemplateRepository.DeleteAsync` | method | Completes the governance-spec gap |
| `ITemplatePromotionRepository` | new interface | Domain (fork deviation, documented) + EF6 impl |

Single EF6 migration `AddLifecycleOps` (or two: `AddTemplateExternalKey` + `AddTemplateSourceView`); EF Core equivalents in the origin. Fork deviations logged (see final section).

## Module 5 — UI details (approved mockups 2026-08-20)

1. **Index**: checkbox column; toolbar with count + 4 actions + Clear; health badge column; Export row action (single).
2. **Health page**: chips row, table with Re-check per row, expandable severity-colored findings.
3. **Import modal** (opened from the index header "Import" button): file input, helper text stating the match/lock/status rules, Import button; result report rendered in the same modal (created/updated/skipped with reasons), errors per file.
4. **Editor Properties**: Source SQL View select + binding note; Health button; inline findings panel.

All styles stay inside `#tb-editor-host` design tokens (both themes); the editor JS gains guarded modules (element-presence checks) as in the audit-page work.

## Module 6 — Testing & verification

### Unit tests (Application.Tests, no DB)

- Export document: shape, version ordering, sanitized file naming, `schemaVersion`/`exporter` fields.
- Import validation: unknown `schemaVersion`, missing name, empty versions, Scriban-invalid body → rejection with the right message.
- Status collapse table (all 4 rows).
- Locked-target skip decision.
- AST token extraction: nested paths, loop collections, filters, string literals containing `model.X` (must NOT be extracted), conditionals.
- Finding classification: each severity code with synthetic view metadata.
- Bulk result aggregation (succeeded/failed partition).

### EF6 integration tests (Infrastructure.EF6.Tests, Docker SQL)

- Migration applies on an existing DB with rows → every row has a non-null, unique `ExternalKey`.
- Promotion repository: create-with-original-version-numbers; append-versions numbering continues from MAX+1; unique-key constraint on duplicate insert.
- `DeleteAsync` removes template + versions (and audit rows survive — no FK).

### End-to-end (sample host, xsp4 + agent-browser)

- Export single → attachment with correct filename/content-type; inspect JSON against the format.
- Import the exported file → created report; re-import → updated report with appended versions; import into a locked template → skipped.
- Health page renders findings for a view whose schema was mutated between binds (drop a column, change a type in the Docker DB, re-check → critical/warning rows).
- Bulk: select 2 → Deactivate (per-item results + audit rows), Activate, Export ZIP (download + open the zip + validate contents), Delete (confirm → gone; audit rows remain).
- Editor: Source View binding + Health button + inline findings; index health badges via `Health/Summaries`.

## Out of scope (future work)

- Snippet export/import (needs persistent template→snippet references first).
- API (non-file) JSON import endpoint for CI-driven promotion.
- Bulk type change / bulk duplicate.
- Scheduled/background drift monitoring (check is on-demand only).
- Template deletion UI beyond bulk (single delete from the index row — trivial follow-on using the same endpoint with one id).
- Line-number spans in health findings if Scriban spans prove unreliable (token names only).
- `ExternalKey` migration for the client's existing production DB must be validated during client integration (like the binding-redirect guidance).

## Fork deviation log (for commit messages + origin port)

1. `Template.ExternalKey`, `Template.SourceView`, `Template.SourceViewSnapshot` — new columns (deliberate: cross-environment identity + drift detection are fork/product features).
2. `AuditActions.Imported` — new constant.
3. `ITemplateRepository.DeleteAsync` — completes a governance-spec item that was never implemented.
4. `ITemplatePromotionRepository` — new Domain interface; EF6 implementation in the fork layer.
5. `Application` gains `ITemplatePromotionService`, `ITemplateHealthService` (+ DTOs) — same pattern as the governance spec's `IAuditService`/`TemplateWorkflowService` additions.
6. The origin port applies the identical changes to its Domain/Application/Infrastructure layers (EF Core migrations, ASP.NET Core endpoints with `IFormFile`).
