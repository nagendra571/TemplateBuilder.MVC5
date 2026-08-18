# Design: TemplateBuilder.Editor.Mvc5 v1.1 — Authoring Superpowers

**Date:** 2026-08-18
**Status:** Approved in principle (all three design sections approved by user)
**Base:** v1.0.0 (pushed to nuget.org by user; repo's `nupkg/TemplateBuilder.Editor.Mvc5.1.0.0.nupkg` is the pushable artifact)
**Target version:** 1.1.0 (semver: additive, backward-compatible feature release)

---

## 1. Problem

The v1.0.0 editor is at feature parity with the origin product
(`templatebuilder.runasp.net`). Parity is the floor, not the ceiling — this is the first
release where the Mvc5 fork deliberately exceeds the origin. The origin's authoring
experience has three weaknesses:

1. **Preview requires hand-written JSON.** The origin's preview modal defaults to `{}`
   and its "Auto-fill from template" only guesses values from `{{ }}` tokens. Sample data
   is never persisted — every session starts from scratch.
2. **The field palette is schema-blind.** SQL view columns are listed by name only —
   no type/length hints, no signal about which fields the template already uses, no
   filtering. The discovery service already returns `DataType`, `MaxLength`,
   `IsNullable` (`SqlColumnInfo`) — unused gold for authors.
3. **No Scriban reference for non-developers.** The loop/conditional wizards cover two
   constructs; everything else (date formatting, string helpers, HTML escaping,
   missing-value defaults) is tribal knowledge.

## 2. Goals / Success criteria

- One click turns an empty preview into realistic, type-aware sample data — from the
  selected SQL view, from template tokens, or both, including arrays for loop blocks.
- Sample data persists per template (`Templates.SampleData`), survives reloads, and the
  preview modal pre-fills from it.
- Palette shows type badges, marks fields already referenced in the body, and filters.
- A searchable, package-shipped Scriban reference panel with click-to-insert entries,
  each snippet verified against the Scriban 7.2.6 engine before shipping.
- **No breakage contract:** every existing v1.0.0 behavior, route, payload, view, and
  test stays green. All changes are additive. The published 1.0.0 nupkg is never
  modified or re-packed.

## 3. Approach

Server-side generation service (Approach A), chosen over client-side JS generation
(untestable under the repo's xunit discipline, duplicated logic, Scriban parsing in JS
is error-prone) and over a hybrid (split logic, worse testing story).

## 4. Server design

### 4.1 Schema — fork decision #1 (documented)

`Templates.SampleData` — `nvarchar(max)`, nullable. One EF6 Code-First migration
(`AddSampleDataToTemplates`). The client's DB gets the column automatically at first
startup after upgrade via the existing `MigrateDatabaseToLatestVersion` initializer —
no client action. Additive; existing columns/rows untouched.

### 4.2 `SampleDataGenerator` service — fork decision #2 (documented)

New service in `TemplateBuilder.Application/Services/` (deliberate fork addition; the
origin has no equivalent). DI-registered alongside the other services. Pure C#,
fully unit-testable.

**Strategy 1 — From SQL view schema.** Given `viewName`, reuses
`ISqlViewDiscoveryService.GetViewColumnsAsync` (already cached) and maps each column
`DATA_TYPE` → realistic value:

| DataType | Default | Name-aware override |
|---|---|---|
| `nvarchar`/`varchar`/`char`/`text` | `Sample {Name}` | contains `email` → `jane.doe@agency.gov`; `phone` → `(860) 555-0142`; `name` → `Jane Doe`; `address` → `450 Columbus Blvd, Hartford, CT 06103`; `city` → `Hartford`; `state` → `CT`; `zip` → `06103`; `url` → `https://example.gov` |
| `int`/`smallint`/`bigint`/`tinyint` | `42` | `qty`/`quantity`/`count` → `4` |
| `decimal`/`numeric`/`money`/`smallmoney` | `99.99` | `price`/`amount`/`total`/`cost` → `1250.00`; `rate`/`tax` → `0.06` |
| `datetime`/`datetime2`/`date`/`smalldatetime` | current date `2026-08-18` | `dob`/`birth` → `1985-03-14` |
| `bit` | `true` | — |
| `uniqueidentifier` | `guid` `3f2504e0-4f89-41d3-9a0c-0305e82c3301` | — |
| anything else | `Sample {Name}` | — |

`MaxLength` respected (string truncated to the limit). Nullable columns still get
values — preview usefulness beats null fidelity. Cap: first 50 columns.

**Strategy 2 — From template tokens.** When no view is selected: `Template.Parse` the
body, walk the AST for `model.X` member accesses; infer type by name heuristics
(suffixes `date`/`time` → date; `amount`/`price`/`total`/`rate`/`cost` → decimal;
`email`/`phone`/`name`/`city`/`state`/`zip`/`address` → string with that shape;
`id`/`code`/`qty`/`count`/`number` → int; else `Sample {X}`). Cap ~50 keys. This
replaces the origin's weak auto-fill.

**Strategy 3 — Loop-aware arrays.** Walk AST for `for X in model.Y` loops; generate
`model.Y` as a 3-item array; item fields from the loop body's `model.Y.Z` member
accesses (types per Strategy 2 heuristics). Bare loop with no inner members →
`[{"label":"Row 1"},{"label":"Row 2"},{"label":"Row 3"}]`.

Result: `Dictionary<string, object>` → JSON string, the exact shape the preview iframe
consumes today.

### 4.3 Endpoints (both JSON, `RequestVerificationToken` header, additive — no existing route changes)

- `POST Templates/Api/SampleData/Generate` — body `{ viewName?: string, templateBody?: string }` → generated JSON (schema-first when `viewName` present, else tokens + loops).
- `PUT Templates/Api/{id}/SampleData` — body `{ sampleData: string }` → persists via
  `ITemplateRepository.UpdateTemplateAsync` (property set + existing update path;
  no repository interface change). Empty string clears the saved data.

### 4.4 No other server changes

`TemplateEngine`, sanitizer, discovery, versioning, snippets — untouched. All v1.0.0
routes/payloads identical.

## 5. UI design

### 5.1 Preview modal

- Opens → saved `SampleData` pre-fills; else inline "Generate sample data" CTA.
- Toolbar: **Generate ▾** (From SQL view / From template tokens / Both) + **Save to
  template** (PUT) + existing Auto-fill kept as power-user fallback.
- "Both" merge rule: start from Strategy 1 (view columns); then add token-derived keys
  (`model.X`) not already present in the view result. When no view is selected, "From
  SQL view" falls back to Strategy 2 silently.
- Editable textarea kept; "Unsaved sample data" indicator when edited since last save.
- Autosave drafts (localStorage) snapshot sample data too, so recovered drafts restore
  their preview data.

### 5.2 Field palette

- Type badges per row from the existing `Api/Views/{viewName}/Columns` payload
  (`nvarchar(200)` / `int` / `bit` / ...) — no new endpoint.
- **Used marker:** fields referenced in the body (`{{ model.X }}`) get a tick; token
  detection reuses the existing client-side token scan (already used by Auto-fill);
  live re-scan debounced with the autosave debounce.
- Search box filters field names.

### 5.3 Scriban reference panel

- New "Scriban reference" button in the palette header → floating panel (find-replace
  pattern). Static content shipped in the package (no CDN, offline-safe).
- Groups: Dates, Strings, Numbers, Loops & Conditionals, Missing values, Whitespace.
- Click-to-insert at the SunEditor cursor (same path as loop/conditional wizards).
- **Every snippet validated against Scriban 7.2.6 in xunit before the panel ships.**

### 5.4 Files touched

- `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml`
- `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (~+250 lines)
- `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css` (~+80 lines,
  scoped under `#tb-editor-host`)
- No controller view-model changes beyond the two new endpoints; no new embedded
  resources (cheat sheet lives in the view markup).

## 6. Testing & verification

- `Application.Tests` — new `SampleDataGeneratorTests`: per-DataType mapping; MaxLength
  truncation; name-heuristic types; token fallback; loop detection (3-item arrays,
  inner members); cheat-sheet snippet validation (each entry rendered via
  `TemplateEngine.RenderBodyAsync`, asserted shape).
- `Infrastructure.EF6.Tests` — migration adds column (schema check) + sample-data
  save/load round-trip through `TemplateRepository`.
- **Full regression:** Domain 16/16, Application 22/22 + new, EF6 11/11 + new — all
  green before pack.
- Editor build 0 errors; `node --check` on JS; pack → extract → inspect nupkg (4 DLLs,
  install.ps1, README, **no `.cshtml` leakage**).
- Sample host: rebuilt from the **1.1.0** nupkg via `nuget.exe install`; xsp4 full flow:
  all v1.0.0 smoke checks (DASHBOARD.md list) + new: generate-from-view,
  generate-from-tokens, save sample data, reload → restored, one-click preview,
  palette badges/used-marks/search, reference-panel insert.
- **NuGet safety:** version bumps to 1.1.0 in csproj; `nupkg/` retains the untouched
  `1.0.0.nupkg`; pack writes a new `1.1.0.nupkg`; never re-pack or re-push 1.0.0.

## 7. Risks

| Risk | Mitigation |
|---|---|
| Cheat-sheet snippets wrong for Scriban 7.2.6 | every entry exercised in xunit pre-ship |
| Generated JSON huge for wide views | 50-key cap, short values |
| `PUT SampleData` race with concurrent edits | deferred to Phase 2 (optimistic concurrency); parity with origin baseline today |
| Palette live-scan cost | debounced with existing autosave debounce |
| Mono/xsp4 quirks with new endpoints | none expected (standard JSON endpoints); covered in smoke |
| Regression on published v1.0.0 | additive-only contract; full test suite + full smoke before pack |

## 8. Out of scope (later phases)

- Version-scoped sample-data snapshots (per-version reproducibility)
- Audit log, approval workflow, optimistic concurrency (Phase 2)
- Export/import, template health checks, bulk ops (Phase 3)
- Snippet versioning + usage tracking (Phase 2)

## 9. Fork decisions log

| # | Decision | Rationale |
|---|---|---|
| 1 | `Template.SampleData` column (nvarchar max, nullable) | origin has no persisted preview data; additive schema |
| 2 | `SampleDataGenerator` service in `Application` | origin has no generation; server-side for testability |
