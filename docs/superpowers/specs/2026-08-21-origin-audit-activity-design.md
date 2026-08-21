# Design: Audit log + Activity drawer — TemplateBuilder.Editor (origin)

**Date:** 2026-08-21
**Audience:** implementer in the origin repo `github.com/nagendra571/TemplateBuilder` (private). Ports the fork's audit log and edit-page activity drawer (already implemented and verified in `TemplateBuilder.Mvc5`) with deliberate adaptations. Requires the two-state save model (2.0.0) and lifecycle & ops (2.1.0) — the audit action set is defined for the two-state model, and this spec **supersedes lifecycle decision L13** ("no audit wiring").

## Goal

1. **Audit log** — an append-only record of every meaningful mutation (template and snippet actions), with a full-featured global page: filters, stat chips, 30-day chart, action badges, expandable before/after state, pagination, CSV export, and a live-poll pill.
2. **Activity log + sidebar** — a right-edge slide-in drawer on the Edit page showing the selected template's timeline, day-grouped with action-colored dots and a count badge, fed by the same audit data.

## Decisions (approved by the product owner)

| # | Decision | Rationale |
|---|---|---|
| A1 | `AuditLog` entity: append-only — rows are never updated or deleted | History must survive hard deletes; `EntityId` is a plain int (no FK) |
| A2 | `AuditService.RecordAsync` records mutations only; reads/checks are never audited | Matches the fork's audit pattern |
| A3 | **Action set (double-checked against the two-state model):** Template — `created`, `draft_saved` (Save Draft), `published` (Save Version = Active save), `restored`, `duplicated`, `toggled_active`, `imported` (lifecycle), `deleted` (bulk delete). Snippet — `snippet_created`, `snippet_deleted`. **Excluded** (verified): `submitted`/`approved`/`rejected`/`review_cancelled` (the origin has no workflow — two-state only); `snippet_edited`/`snippet_restored` (the origin has list/create/delete snippet endpoints only — verified in `SnippetsController.cs`); usage tracking (no `SnippetUsage` table) | The origin's surface defines what can happen |
| A4 | **Supersedes lifecycle L13**: lifecycle's `ImportAsync` records `imported`, bulk delete records `deleted` (per template) | The other agent's lifecycle plan says "no audit wiring"; this feature overrides that for exactly these two actions |
| A5 | Global audit page = **full fork parity**: filter card (search/entity-type/action/actor/date-range + Clear), 5 stat chips (total/templates/snippets/actors/range), 30-day SVG bar chart (inline JS — no CDN), color-coded action badges, per-row expandable Before/After state (JSON + plain-string diff highlight), windowed pagination, CSV export, 30s live-poll pill ("N new — Refresh") | Proven UI; ports directly |
| A6 | Activity drawer = Edit-page right-edge slide-in: vertical "ACTIVITY" tab with count badge, full-height drawer absolutely positioned inside the grid (grid flow never affected), day-grouped timeline with action-colored dots, Esc / X / tab toggles | Fork parity (the fork's original layout bug — timeline as a grid child — is avoided by the absolute-positioning design) |
| A7 | `GET /Audit/Stats` (same filters as the page) powers chips + chart + poll; `GET /Templates/{id}/Audit` feeds the drawer | Same data, two projections |
| A8 | CSV export `GET /Audit/Export`: columns `OccurredAt,EntityType,EntityId,Action,Actor,Comment,BeforeState,AfterState`, UTF-8 with BOM, quoted fields | Fork parity |
| A9 | Version: **2.2.0** (after 2.0.0 two-state and 2.1.0 lifecycle) | SemVer: additive |
| A10 | Prerequisite: two-state + lifecycle merged; if they are not yet merged when this plan starts, build Tasks 1 and 3–5 (data layer + audit page + drawer are independent) and gate Task 2 (action wiring) on the endpoints existing | Sequencing safety |
| A11 | Authorization: new routes (`Audit`, `Audit/Stats`, `Audit/Export`, `Templates/{id}/Audit`) are covered automatically by the existing `TemplateBuilderControllerConvention` | No extra work |
| A12 | Audit services registered in `AddTemplateBuilderEditor` only (Editor) — the render-only Core package is untouched | Audit is editor/ops surface |

## Current state (origin — verified against `main`, commit 194cf15; assumes two-state + lifecycle landed)

- No audit anywhere: no `AuditLog` table, no `AuditActions`, no `AuditService`. (The fork's governance/audit phases are fork-native.)
- Lifecycle (in flight, other agent): `ImportAsync`, `BulkDelete`, `ToggleActive`, `Create`/`SaveVersion`/`RestoreVersion`/`Duplicate` shapes as in the two-state plan — `SaveVersion` publishes `IsActive = request.IsActive ?? true`.
- Snippets: `SnippetsController` = `GET/POST/DELETE /Templates/Api/Snippets` only.
- Controllers are ASP.NET Core attribute-routed; JSON via `Ok(...)`/`[FromBody]`; `[ValidateAntiForgeryToken]` + `RequestVerificationToken` header; EF Core + `dotnet ef migrations add`; RCL `.cshtml` edited directly; tests: Moq + FluentAssertions + InMemory EF.
- The Edit page has no activity drawer; the index page has no audit link.

## Reference implementation (fork)

The fork implemented both features and verified them end-to-end (commits `0886916`..`c5f0dae` in `github.com/nagendra571/TemplateBuilder.MVC5`, private). Exact shapes to port (verified from the fork source):

| Fork piece | Shape |
|---|---|
| `AuditLog` entity | `Id, EntityType (20), EntityId (int, no FK), Action (40), Actor (200), OccurredAt, BeforeState (4000), AfterState (4000), Comment (1000)` |
| `IAuditService` | `RecordAsync(string entityType, int entityId, string action, string actor, string? beforeState = null, string? afterState = null, string? comment = null, CancellationToken ct = default)` |
| `AuditQuery` (Domain) | `EntityType?, EntityId?, Action?, Actor?, From?, To?, Search?` (matches Action/Actor/Comment) `, Page = 1, PageSize = 25` |
| `IAuditRepository` | `AddAsync(AuditLog)`, `GetLastOccurrenceAsync(entityType, entityId, action)` (throttle helper), `QueryAsync(AuditQuery)` (filtered, desc, paged), `CountAsync(AuditQuery)` |
| `AuditFiltering` (shared) | `Apply(IQueryable<AuditLog>, AuditQuery)` — exact-match EntityType/EntityId/Action, `Contains` Actor, From inclusive, To exclusive (`< To.Date.AddDays(1)`), Search across Action/Actor/Comment |
| `AuditStats`/`AuditDailyBucket` | `{ Total, TemplateCount, SnippetCount, UniqueActors, FirstOccurrence?, LastOccurrence?, DailyBuckets: [{ Date, Count }] }` — 30-day window default, or From/To-driven |
| Timeline endpoint | `GET /Templates/{id}/Audit` → `Ok(rows.Select(a => new { id = a.Id, action = a.Action, actor = a.Actor, occurredAt = a.OccurredAt.ToString("o"), comment = a.Comment }))` (100 rows, desc) |
| CSV | `OccurredAt,EntityType,EntityId,Action,Actor,Comment,BeforeState,AfterState`, quoted when containing `" , \n \r`, UTF-8 BOM, `template-builder-audit.csv` |
| Audit page ids | `tb-audit-total`, `tb-stat-templates`, `tb-stat-snippets`, `tb-stat-actors`, `tb-stat-range`, `tb-audit-chart-svg`, `tb-audit-chart-axis`, `tb-audit-initial-stats`, `tb-live-pill`, `audit-expand-{id}`, `audit-state-{id}`, `audit-detail-{id}` |
| Drawer ids | `tb-activity-tab`, `tb-activity-count`, `tb-activity-drawer`, `btn-activity-close`, `tb-timeline` |
| JS helpers | `fmtRelative(iso)`, `fmtDayLabel(iso)`, `actionKind(action)` (success/danger/warning/info) — day grouping + dots |

## Module 1 — Domain/Infrastructure

- `AuditLog` entity + `AuditActions` static constants (A3 list) in `TemplateBuilder.Domain`.
- `AuditQuery` + `IAuditRepository` in `Domain/Interfaces`; `IAuditService` in `Application/Services`.
- `AuditFiltering` (internal static), `AuditRepository`, `IAuditStatsRepository`/`AuditStatsRepository`/`AuditDailyBucket`/`AuditStats` in `Infrastructure` (repo + `Repositories/`).
- EF Core configuration `AuditLogConfiguration` (max lengths above; indexes: `(EntityType, EntityId, OccurredAt)` and `OccurredAt`).
- Migration `AddAuditLog` (scaffolded — no hand-edits expected beyond defaults).
- `AuditService` (Application): `RecordAsync` sets `OccurredAt = DateTime.UtcNow` and delegates to `IAuditRepository.AddAsync`.
- DI (Editor only, A12): `AddScoped<IAuditRepository, AuditRepository>()`, `AddScoped<IAuditStatsRepository, AuditStatsRepository>()`, `AddScoped<IAuditService, AuditService>()`.
- EF Core note: a DbContext does not support concurrent async operations (same rule as EF6) — keep the stats queries sequential (the fork's pattern).

## Module 2 — Action wiring (supersedes lifecycle L13)

| Endpoint | Action | State |
|---|---|---|
| `POST /Templates/Create` | `created` | afterState `{ name }` |
| `POST /Templates/{id}/SaveVersion` | `draft_saved` when `IsActive == false`, `published` when `true` | afterState `{ versionNumber, versionId, isActive }` |
| `POST /Templates/{id}/Restore/{versionId}/{sourceVersionNumber}` | `restored` | comment `Restored from v{sourceVersionNumber}` |
| `POST /Templates/{id}/Duplicate` | `duplicated` | comment `Duplicated from template {sourceId}` |
| `POST /Templates/{id}/ToggleActive` | `toggled_active` | afterState `{ isActive }` |
| `POST /Templates/Import` | `imported` (once per imported template) | afterState `{ file, externalKey, versionsImported }` |
| `POST /Templates/BulkDelete` | `deleted` (per deleted template) | beforeState `{ name }` |
| `POST /Templates/Api/Snippets` (create) | `snippet_created` | afterState `{ name }` |
| `DELETE /Templates/Api/Snippets/{id}` | `snippet_deleted` | beforeState `{ name }` |

Actor = `User.Identity?.Name ?? "anonymous"` (the origin's controllers use `HttpContext.User`; the fork's `CurrentActor` helper becomes a small protected property on `TemplatesController`/`SnippetsController` — or a shared base; the origin has no shared controller base — add `protected string CurrentActor => User?.Identity?.Name ?? "anonymous";` to both controllers).

State JSON: `JsonSerializer.Serialize(new { ... })` (System.Text.Json default — PascalCase keys are fine for stored state text; it is only displayed, never parsed programmatically).

## Module 3 — Audit page (server + client)

- `AuditController` (Editor): `[Route("Audit")]` Index (filters → view model with paged rows + `AuditStats` + `KnownActions` from `AuditActions` constants via reflection); `[Route("Audit/Stats")]` Stats (same filters → `Ok(stats)` — System.Text.Json camelCase via MVC); `[Route("Audit/Export")]` CSV (A8).
- `AuditIndexViewModel`: `Rows, Total, Page, PageSize, Search, EntityType, Action, Actor, From, To, Stats, KnownActions`.
- View `Views/Audit/Index.cshtml` (RCL): page header + CSV link + live pill, stat chips, chart svg (empty container — JS draws), filter card, table with action badges + expandable state rows, windowed pagination, empty state.
- Client (append to `wwwroot/js/template-editor.js`): `initAuditPage()` guarded by `#tb-editor-host.tb-audit-page` — relative timestamps, expand rows (JSON diff + plain-string diff highlight), 30-day SVG chart from `dailyBuckets`, filter form wiring + Clear, windowed pagination, 30s poll of `/Audit/Stats` comparing totals ("N new — Refresh" pill). Helpers `fmtRelative`/`fmtDayLabel`/`actionKind` shared with the drawer.
- CSS: append the fork's audit-page section, mapped to the origin's tokens (`--surface`, `--border`, `--accent`, `--success-*`, `--warning-*`, `--danger-*`, `--radius-*` — verify names in the origin's token block).

## Module 4 — Activity drawer (Edit page)

- Timeline endpoint: `GET /Templates/{id}/Audit` on `TemplatesController` (100 rows, desc) — shape in the reference table.
- `Edit.cshtml`: inside `.tb-editor-grid` (which gains `position: relative`), add the tab button + drawer (ids above) — absolutely positioned, never grid flow.
- Client: `initActivityDrawer()` — open/close (tab, X, Esc, Tab focus trap), load timeline on first open (and refresh count on each open), day-grouped list (`fmtDayLabel` headers, `fmtRelative` times, `actionKind` dot colors), count badge, empty state.
- CSS: fork's drawer section mapped to origin tokens.

## Module 5 — Testing & verification

- **Application.Tests**: `AuditServiceTests` — `RecordAsync` writes `OccurredAt` and delegates (Moq `IAuditRepository`); state/comment pass-through.
- **Infrastructure.Tests** (InMemory): `AuditRepositoryTests` — `QueryAsync` filters (entity type/action/actor/search/from-inclusive/to-exclusive), pagination, desc order; `CountAsync`; `GetLastOccurrenceAsync`; `AuditStatsRepositoryTests` — totals/unique actors/range, 30-day buckets (a known 2-day fixture), From/To window override; `AuditFiltering` covered via the repo tests.
- **Editor.Tests**: `AuditControllerTests` (Index view model shape incl. stats + known actions; Stats `OkObjectResult`; Export CSV content/columns/quotes/BOM) and `TemplatesControllerTests` timeline endpoint (shape, 100-row cap, desc). Action wiring tests: `SaveVersion` records `draft_saved` vs `published` by `IsActive` (Moq `IAuditService` `Verify`), plus `created`/`restored`/`duplicated`/`toggled_active`/`snippet_created`/`snippet_deleted`/`imported`/`deleted`.
- **e2e** (Web at `https://localhost:7275/`): create → Save Draft → Save Version → toggle → import → bulk delete; audit page shows all rows with correct badges/actions; filters (action=published, date range, search) narrow correctly; chips + chart render; CSV download parses (8 columns, BOM); 30s poll pill; drawer on the Edit page: opens, count badge, day groups, dots, Esc/X close, per-action colors; `GET /Templates/_setup` green.
- **Pack**: nupkgs inspected; README What's New → 2.2.0 (README-sync lesson).

## Versioning

`TemplateBuilder.Editor` → **2.2.0** (Core unchanged — no render-contract impact).

## Out of scope (future work)

- Workflow audit actions (no workflow in the origin).
- Snippet versions/usage/restore audit (no snippet governance in the origin).
- Audit retention/archival policies.
- Auditing reads/renders.

## Port/fork deviation log

- Action set reduced to the two-state surface (A3): no `submitted`/`approved`/`rejected`/`review_cancelled`, no `snippet_edited`/`snippet_restored`.
- **Supersedes lifecycle L13** (import + bulk delete DO record audit).
- `CurrentActor` added as a small protected property on the two controllers (the origin has no shared controller base; the fork's `TemplateBuilderControllerBase` is MVC5-specific).
- State JSON via System.Text.Json (PascalCase keys in stored text — display-only), vs the fork's Newtonsoft.
- EF Core translations (e.g. `GroupBy(a => a.OccurredAt.Date)` for daily buckets) vs the fork's EF6.
