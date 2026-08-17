# TemplateBuilder.Editor.Mvc5 — UI/UX Parity with .NET 8/10 Editor — Design

**Date:** 2026-08-17
**Status:** Approved-in-principle (user: "compare, plan, then implement; dashboard for progress")
**Scope:** Bring the Mvc5 fork's editor UI/UX to parity with the origin product
(`http://templatebuilder.runasp.net/`), which runs on .NET 8/10.

## 1. Problem

The Mvc5 fork is server-complete but UI-primitive:

| Dimension | Mvc5 fork (current) | .NET 8/10 origin |
|---|---|---|
| Editor canvas | single `<textarea>` (Scriban source) | SunEditor WYSIWYG + source view |
| Editor layout | single-column form | 3-panel (field palette / canvas / properties) |
| Field palette | none (only "Load Columns" button fills a textarea) | SQL view selector + draggable fields + insert buttons |
| Blocks | none | Loop block, Grid block (draggable) + Loop wizard + Conditional wizard |
| Snippets | server API exists; no UI in editor | palette list, insert/delete, "save selection as snippet" modal |
| Version history | inline partial on page + separate route | modal, card list, side-by-side compare modal (iframe) |
| Preview | inline div on page | modal with sample-data JSON, auto-fill-from-template, iframe |
| Drafts/autosave | none | localStorage autosave toggle + draft recovery banner |
| Find & replace | two inputs + Replace All | floating panel (prev/next, case, whole-word) |
| Special chars | none | searchable floating panel |
| Theme | toggle (classic) | dark default + light opt-in, persisted |
| Word count | none | words / chars / no-spaces bar |
| Toasts | inline message div | toast notifications |
| Index page | plain table + count chips | search/filter, badges, stats sidebar, inline toggle, duplicate modal |
| Asset size | 388-line CSS / 345-line JS | 1356-line CSS / 2098-line JS |

**Server-side gap: none.** Every endpoint the origin UI calls exists in Mvc5 with the
same route and payload shape (verified by grepping the origin's JS):
`Preview`, `Restore/{versionId}/{sourceVersionNumber}`, `SaveVersion`, `Validate`,
`Versions` (HTML partial), `Versions/{versionId}/Body`, `Api/Snippets` (GET/POST),
`Api/Snippets/{id}` (DELETE), `Api/Views/{viewName}/Columns`, `ToggleActive`,
`Duplicate`. Anti-forgery already uses the `RequestVerificationToken` header
(BLOCKERS #13). So this work is **100% frontend** (views, CSS, JS, embedded assets).

## 2. Approach Options

- **A. Faithful port of origin assets (RECOMMENDED).** Copy the origin's
  `template-editor.js` (2098 lines) + `template-editor.css` (1356 lines) into the Mvc5
  StaticAssets, adapt the few integration points, rewrite the 3 views to the origin's
  markup, embed SunEditor locally. Lowest risk, guaranteed parity, minimal rework —
  the origin UI already matches the Mvc5 route surface 1:1.
- **B. Rebuild from scratch with a bespoke Mvc5 design.** More code, higher drift risk,
  no benefit for the client (same visual language desired).
- **C. Cosmetic CSS/JS polish of the current form.** Fails the stated goal.

**Decision: A.**

## 3. Design

### 3.1 SunEditor, self-hosted (no CDN)

The origin loads `suneditor@2.47.10` from jsdelivr. The Mvc5 package must not depend on
a CDN (corporate client, offline-capable, "package contains everything" property).
Download `suneditor.min.js` + `suneditor.min.css` once, commit them into
`StaticAssets/`, embed via the existing EmbeddedResource pipeline, and serve through
the existing `/TemplateBuilderEditor/{*path}` static route
(`TemplateBuilderStaticAssetsRoute`, BLOCKERS #15). Size impact on the nupkg ≈ 350 KB —
acceptable. SunEditor's CSS ships its icon font as a data URI, so no extra font files.

### 3.2 Ported origin assets (adaptation points)

`template-editor.css` — ported verbatim except:
- keep the existing `#tb-editor-host` scoping (Bootstrap-3 client collision safety, risk #3);
- no changes to tokens (dark default + `.tb-theme-light` opt-in, Linear/Vercel style).

`template-editor.js` — ported verbatim except:
- `_csrf` already matches (header-based antiforgery, BLOCKERS #13);
- URLs already match — no route changes;
- SunEditor global comes from the self-hosted script tag (same global name `window.SunEditor`);
- theme default: origin JS reads `tb-theme` with `|| 'light'` and adds `.tb-theme-light`
  to the host — kept as-is;
- `view-selector` options are server-rendered in the Mvc5 Edit view (from
  `Model.AvailableViews`, SQL view discovery) — the origin leaves the select empty when
  the host page supplies no views; identical behavior;
- `templateId` / `currentVersionNumber` injected via inline `<script>` in the view,
  exactly like the origin's `_Layout` pattern.

### 3.3 Views (rewritten to origin markup)

- **`Index.cshtml`** — page header (+ New Template), search row (search + type filter +
  Filter), list table (`tb-list-table`: Name / Type badge / Version / Updated / Status
  badge / Actions: Edit + Duplicate + Disable|Enable), Quick Stats sidebar (total +
  per-type rows), Duplicate modal (`duplicate-modal`, name prefilled "Copy of X"),
  inline `toggleActive()`/`openDuplicateModal()`/`confirmDuplicate()` script block
  (same as origin's Index page inline script).
- **`Edit.cshtml`** — the full 3-panel grid inside `#tb-editor-host`:
  - LEFT: FIELD PALETTE (view selector server-rendered, `#field-palette`, BLOCKS section
    with draggable Loop/Grid, SNIPPETS section with `#snippet-list` + save-selection button)
  - CENTER: CANVAS (autosave toggle + theme toggle in heading, draft banner, validate
    panel, `#template-body` textarea that SunEditor replaces, word-count bar)
  - RIGHT: PROPERTIES (name/type/description/version+History/save-note, footer: Preview +
    Save Version buttons)
  - Modals: version-history, compare, preview (with `preview-json` + auto-fill),
    loop wizard, conditional wizard, save-snippet; floating panels: find-replace,
    special-chars. All markup ported from the origin Edit page (fetched from the live
    site, ids/elements match the origin JS exactly).
  - `__RequestVerificationToken` hidden field inside the form (kept for header reads).
- **`_VersionHistory.cshtml`** — origin card markup contract:
  - cards `.tb-version-card` (+ `.is-current`), `.tb-version-num`, `.tb-version-badge`
    ("Current"), `.tb-version-comment`, `.tb-version-meta`;
  - buttons: "Restore" → `onclick="restoreVersion(this, versionId, sourceVersionNumber)"`,
    "Compare" → `onclick="openCompareView(this)"` with `data-version-id`,
    `data-version-num`, `data-comment`, `data-created-at`;
  - the route `Templates/{id}/Versions` continues to return this partial (no layout) —
    no controller change.
- Old `GetVersionHistory` inline section in the old Edit page and the old find/replace
  toolbar are removed with the rewrite.

### 3.4 Server changes

**None required.** (Verified: all routes/payloads/shapes match.) No `Domain`/`Application`
changes; no controller changes.

### 3.5 Sample host wiring

`Views/Shared/_Layout.cshtml`: replace CDN references with the package's self-hosted
assets: `/TemplateBuilderEditor/js/suneditor.min.js`,
`/TemplateBuilderEditor/css/suneditor.min.css` + `/TemplateBuilderEditor/css/template-editor.css`
+ `/TemplateBuilderEditor/js/template-editor.js`. Index page script block moves into the
Index view (as in the origin).

### 3.6 Packaging & verification

- Build → confirm embedded resources (suneditor + editor assets) via
  `GetManifestResourceNames`; `dotnet pack` → extract nupkg → confirm all assets present,
  no `.cshtml` leakage (RazorGenerator precompiles views; the two view rewrites must not
  add physical cshtml to the package).
- xsp4 smoke: list page (stats/badges/modal), edit page (3-panel, suneditor assets 200
  with right content-types), SaveVersion, Preview, Validate, Versions partial, compare
  endpoint chain, snippets CRUD, duplicate, toggle, `/_setup` PASS.
- JS syntax gate: `node --check` on the ported JS before commit.

## 4. Out of Scope

- IE11 support (origin already uses modern JS; same bar).
- Demo seed templates (snippets/loop/grid demo content) — sample host data only.
- Server-side draft persistence (origin drafts are localStorage-only).
- Accessibility audit beyond the origin's own level (port keeps origin's aria/role markup).

## 5. Risks

| Risk | Mitigation |
|---|---|
| SunEditor embed bloat | ~350 KB; verified acceptable; no external font files |
| Port drift from origin | Port verbatim; adaptation points enumerated above; future origin changes re-ported |
| Bootstrap 3.3.7 CSS collision on client | CSS stays scoped under `#tb-editor-host`; SunEditor markup lives inside the host |
| mono/xsp4 quirks with new JS | None expected (server surface unchanged); verify via smoke flow |
| localStorage blocked (older hosts) | try/catch already present in origin JS (storage unavailable path) |