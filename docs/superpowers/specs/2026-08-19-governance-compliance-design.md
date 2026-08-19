# Design: Governance & Compliance — workflow, audit log, concurrency, snippet versioning

**Date:** 2026-08-19
**Audience:** implementer in the MVC5 fork (this repo); a port of this spec will be handed to an implementer in the origin .NET 8/10 repo (`TemplateBuilder`). Platform-specific notes are called out where EF6 vs EF Core differ.

## Goal

Add a governance layer to the template editor:

1. **Workflow** — draft → review → approve → publish state machine with server-side draft bodies and edit locking.
2. **Audit log** — append-only record of who did what and when, surfaced as a per-template timeline and a filterable global view with CSV export.
3. **Concurrency hardening** — fix the publish version-number race; add concurrency tokens where missing; conflict-check workflow transitions.
4. **Snippet governance** — version history for snippets + usage tracking (which templates use which snippet).

Single-role flow: any user with editor access can move a template through all stages; the workflow is a visible process trail, not an access-control barrier. (Role gating remains possible later via the existing configurable authorization without schema changes — the audit log records every transition actor.)

## Current state (gap analysis)

- `Template` already has `RowVersion` (EF6 concurrency token) and `SaveVersion`/`RestoreVersion` already map `DbUpdateConcurrencyException` → 409. `IsActive` exists as a live/retire toggle.
- **Race:** `PublishVersionAsync` computes `MAX(VersionNumber)+1` in one query and inserts in the next (TemplateRepository.cs:39-74) — two concurrent publishes produce a duplicate version number. There is no unique constraint on `(TemplateId, VersionNumber)`.
- **No identity:** `TemplateVersion.CreatedBy` exists but is never populated; the controllers never read `User.Identity`. There is no actor plumbing.
- **Drafts are browser-local:** autosave is localStorage-only (`template-editor.js:2240`) — a reviewer on another machine cannot see a submitted draft.
- `Snippet` has no versions, no concurrency token, no usage data. Snippet insertion is copy-paste (`insertHTML`) — no persistent reference token.
- No audit entity exists anywhere.

## Module 1 — Template workflow

### State machine

| From | Event | To | Guard | Notes |
|---|---|---|---|---|
| (create) | Create | Draft | — | New templates start in Draft |
| Published | Edit begins | Draft | — | Auto-transition on first server-side draft save with changed body |
| Draft | Submit for review | Review | body non-empty | Server saves `DraftBody`, locks editing |
| Review | Approve | Approved | — | Still locked; body = approved body |
| Approved | Publish | Published | — | Creates `TemplateVersion` from `DraftBody`, sets `CurrentVersionId`, clears `DraftBody`/`ReviewComment` |
| Review | Reject | Draft | comment optional | Sets `ReviewComment`, unlocks editing |
| Review / Approved | Cancel review | Draft | — | Explicit unlock; audited as `review_cancelled` |
| any | Delete | (gone) | — | Hard delete; audit rows persist (see Module 2) |

Rules:

- `Draft` and `Published` allow editing; `Review` and `Approved` are locked (read-only body in editor + banner).
- `IsActive` is independent of workflow status and keeps its current semantics.
- Editing a `Published` template moves it to `Draft` on the first server-side draft save that differs from the current body. Opening the editor without changing anything does not change status.
- Editing a `Review`/`Approved` template requires an explicit "cancel review" (→ Draft) action before the body becomes editable again; the timeline records the cancellation.
- `Publish` is a separate step after `Approve` (per design decision) — the four named stages are distinct states.
- Duplicate produces a new template in `Draft`; ToggleActive changes `IsActive` only.

### Server-side draft

- New column `Template.DraftBody` (nullable). While in Draft/Review/Approved, the editor loads `DraftBody` when present, else the current version body.
- Debounced autosave POSTs the body to the server (replacing the localStorage-only flow; localStorage remains as a crash-recovery cache only, always overwritten by the server draft on load). See the "editor UX" section for the exact endpoints.
- On `Submit for review`, the current editor body is POSTed as `DraftBody` atomically with the status transition.
- On `Publish`, the version is created from `DraftBody` (never from the current version body), then `DraftBody` is cleared.
- `SampleData` stays a template-level convenience (not part of the reviewed body).

### Concurrency on transitions

Every transition endpoint takes the template's expected status and/or `RowVersion`; a mismatch returns 409 `CONFLICT` (same shape as the existing SaveVersion conflict). A submit races a publish on the same template → exactly one wins; the loser gets 409 with the standard "refresh and try again" message.

### Migration / back-compat

- `Template.Status` defaults to `Published` for existing rows (current behavior — every existing template renders as before). `DraftBody`/`ReviewComment` null.
- Workflow is opt-in per template by editing it (or creating new).

## Module 2 — Audit log

### Entity: `AuditLog` (new table, append-only)

| Column | Notes |
|---|---|
| Id | identity |
| EntityType | `Template` \| `Snippet` |
| EntityId | int (no FK — rows survive hard deletes) |
| Action | string enum-ish value (list below) |
| Actor | string — from `User.Identity?.Name` (falls back to `"anonymous"`) |
| OccurredAt | UTC |
| BeforeState | string? JSON snapshot |
| AfterState | string? JSON snapshot |
| Comment | string? (rejection feedback, change comments, restore notes) |

Indexes: `(EntityType, EntityId, OccurredAt)`; `(OccurredAt)`.

### Events recorded (mutations only — never preview/render)

Templates: `created`, `draft_saved` (debounced autosave, throttled — see note), `submitted`, `approved`, `rejected`, `review_cancelled`, `published` (with version number in AfterState), `edited` (metadata: name/type/description via SaveVersion), `restored`, `duplicated`, `toggled_active`, `deleted`.

Snippets: `snippet_created`, `snippet_edited`, `snippet_deleted`, `snippet_restored` (version restore).

Throttling: `draft_saved` fires at most once per 5 minutes per template regardless of autosave frequency (uniqueness on (EntityType, EntityId, Action, window) or simple last-written-time check in the audit service).

### Actor plumbing

`TemplateBuilderControllerBase` gains a protected `CurrentActor` helper (`User.Identity?.Name ?? "anonymous"`). All audit writes go through an `IAuditService` (Application layer) so controllers never touch the table directly; the service also populates `TemplateVersion.CreatedBy` (fixing the never-populated column) and `SnippetVersion.CreatedBy`.

### Surfacing

1. **Per-template timeline** — in the editor, a timeline panel listing that template's audit rows (newest first). Same UI for snippets in the snippet list.
2. **Global audit view** — new page (`/Audit`) with filters: entity type, action, actor, date range, free-text search. Paginated (25/page).
3. **CSV export** — `/Audit/Export?<same filters>` streams the current filtered set as CSV (RFC 4180: quoted fields, escaped quotes, CRLF) with a `Content-Disposition` attachment header.

Authorization: same configurable editor policy as the rest of the editor (single-role; no separate viewer role in scope).

## Module 3 — Concurrency hardening

1. **Atomic version numbering (fix the race):** unique index on `TemplateVersions(TemplateId, VersionNumber)` (EF6 migration; EF Core: `HasIndex(...).IsUnique()` + migration). `PublishVersionAsync` computes the next number and inserts inside a single transaction, retrying (max 3 attempts) on the unique-violation `DbUpdateException`; a final failure surfaces as 409. This fixes SaveVersion, RestoreVersion, and the workflow Publish path in one place.
2. **Snippet RowVersion:** `Snippet` gains a `RowVersion` concurrency token; concurrent edits → `DbUpdateConcurrencyException` → 409 (same controller pattern as templates).
3. **Transition conflicts:** covered in Module 1 (expected-status + RowVersion on transition endpoints).

## Module 4 — Snippet versioning + usage

### `SnippetVersion` (new table)

| Column | Notes |
|---|---|
| Id, SnippetId, VersionNumber | unique (SnippetId, VersionNumber) — same atomic-insert approach as templates |
| Body, ChangeComment, CreatedAt, CreatedBy | |

- Every snippet save that changes the body creates a new version; `Snippet.Body` remains the current-content snapshot (consistent with the Template/TemplateVersion pattern).
- Snippet list gains a version-history view + "restore version" (restore creates a new version with the restored body, mirroring template restore semantics; audited as `snippet_restored`).
- Migration backfills v1 for existing snippets from current `Body`.

### `SnippetUsage` (new table)

| Column | Notes |
|---|---|
| Id, SnippetId, TemplateId, UsedAt, UsedBy | |

- Recorded when the editor inserts a snippet into a template (insert handler already knows both ids — POST to a new endpoint).
- Surfaces in the snippet list: usage count, distinct-template count, last used.
- **Known limitation (documented):** snippet insertion is copy-paste — manually pasted snippet content or snippet content embedded in templates created outside the editor is not trackable. Usage is "insert events," not "content contains."

## Editor UX changes (summary)

- Editor loads with a status pill (Draft/Review/Approved/Published).
- Review/Approved: read-only body, banner explaining the lock, actions available: (Review) Approve / Reject-with-feedback; (Approved) Publish. Draft state adds "Submit for review" + (Published/Draft) "Cancel review" when in Review/Approved.
- Submit opens a small confirm; Reject opens a feedback textarea (required comment optional — decision: optional).
- Timeline panel toggled in the editor; snippet history/usage shown in the snippet list.
- Global audit page + export link in the editor's setup/admin area (alongside the existing setup page).

## Non-goals (explicitly out of scope)

- No role-gated approvals (single-role by design; extensible later).
- No soft deletes — templates/snippets are hard-deleted; audit rows persist without FK.
- No render/preview/read tracking in the audit log.
- No approval email/notification integration.
- No snippet content-containment analysis (insert-event tracking only).
- No changes to `IsActive` semantics.
- No undo of publish (restore remains the mechanism).

## Known risks

1. **Draft body semantics** — "edit moves Published → Draft" changes the meaning of opening the editor: users who merely look at a published template must not change its status. The transition fires only on a *changed-body* draft save, not on load.
2. **Autosave → server writes** — new server write volume; mitigated by the 5-minute audit throttle, but DraftBody itself is written on every debounced autosave. Acceptable for editor-scale usage; noted as a monitoring point.
3. **Publish race fix touches the hottest path** — the unique-index + retry must be exercised by the existing SaveVersion/Restore tests, not just new workflow tests.
4. **Audit row growth** — no retention policy in scope; flagged for ops (CSV export covers archival needs).
5. **EF Core port:** `DbUpdateConcurrencyException` and `DbUpdateException` live in different namespaces than EF6 (`Microsoft.EntityFrameworkCore` vs `System.Data.Entity`); the origin port must not mix them up. Unique-index retry logic is identical otherwise.

## Origin port notes (for the .NET 8/10 agent)

- Everything here ports as-is; only the exception namespaces, migration tooling (EF Core `MigrateAsync`), and `DbContext` index configuration differ.
- The origin already has `Template.RowVersion`-equivalent behavior? — verify; the fork inherited it from the origin's EF6-era design. The fork's SaveVersion 409 handling was ported from the origin; confirm the origin's `PublishVersionAsync` has the same MAX+1 race (it does per code read) and port the unique-index fix.
- The origin's editor JS already sends the `RequestVerificationToken` header; the new endpoints follow the same pattern.
- Actor resolution in ASP.NET Core uses `User.Identity?.Name` — identical.
- Snippet insertion in the origin is also copy-paste — the usage-tracking limitation applies there equally.
