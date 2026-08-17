# Parity Editor UI — Implementation Plan

**Date:** 2026-08-17 · **Spec:** `docs/superpowers/specs/2026-08-17-parity-editor-ui-design.md`
**Goal:** Bring the Mvc5 editor UI/UX to parity with the .NET 8/10 origin
(`http://templatebuilder.runasp.net/`). Server surface is already 1:1 — this is 100% frontend.

## Task 1 — Embed SunEditor (self-hosted)

- Download `suneditor@2.47.10` `dist/suneditor.min.js` + `dist/css/suneditor.min.css`
  (jsdelivr) into `src/TemplateBuilder.Editor.Mvc5/StaticAssets/`.
- Verify the editor csproj's embedded-resource glob covers them (check existing
  `StaticAssets` wiring); add explicit `EmbeddedResource` entries if needed.
- `node --check` is N/A (minified) — verify via build + served 200 + content-type.

## Task 2 — Port origin CSS

- Replace `StaticAssets/template-editor.css` with the origin's 1356-line stylesheet
  (fetched copy in `/tmp/opencode/origin-editor.css`), scoped under `#tb-editor-host`.
- Diff for Mvc5-only classes currently used (e.g. `tb-version-label`) — drop dead ones,
  keep the origin's token set verbatim otherwise.

## Task 3 — Port origin JS

- Replace `StaticAssets/template-editor.js` with the origin's 2098-line script
  (`/tmp/opencode/origin-editor.js`), adapting:
  1. keep `_csrf` header antiforgery (already matches);
  2. URLs already match — no edits;
  3. no CDN references (SunEditor global comes from the self-hosted script tag);
  4. `view-selector` options server-rendered (already the Mvc5 Edit pattern).
- `node --check` the result.

## Task 4 — Rewrite `Index.cshtml`

- Origin list page markup: page header + "+ New Template", search row (search/type/Filter),
  `tb-list-table` with badges, version, updated (date only), status + Edit/Duplicate/Disable
  actions, Quick Stats sidebar (`tb-stats-sidebar`), Duplicate modal, inline script block
  (`toggleActive`, `openDuplicateModal`, `confirmDuplicate`) with `_csrf` read from the
  hidden token field.
- `Model` already exposes `Search`, `TypeFilter`, `CountByType`, `Templates`,
  `CurrentVersion` (`VersionNumber`), `IsActive`, `UpdatedAt`, `TemplateType`.
- Render dates as `dd MMM yyyy` to match origin's list.

## Task 5 — Rewrite `Edit.cshtml`

- Origin 3-panel markup (fetched live copy in `/tmp/opencode/` — the Edit page HTML):
  - left panel: view selector (server-rendered options from `Model.AvailableViews`),
    `#field-palette`, BLOCKS (Loop/Grid draggable), SNIPPETS (`#snippet-list`,
    `#btn-save-snippet`);
  - center: CANVAS heading (autosave + theme toggles), draft banner, validate panel,
    `#template-body` textarea (value = `Model.Body`, HTML-escaped), word-count bar;
  - right: PROPERTIES (Name/Type/Description/Version+History/save-note), footer
    (Preview, Save Version);
  - modals: version-history, compare (with `compare-panels` + iframes), preview
    (sample JSON + auto-fill + iframe), loop wizard, conditional wizard, save-snippet;
  - floating: find-replace panel, special-chars panel.
- Keep `<input name="__RequestVerificationToken" type="hidden" ...>` (also render
  `@Html.AntiForgeryToken()` inside the form for the form-post Create path).
- Inline `<script>const templateId = ...; const currentVersionNumber = ...;</script>`
  before `template-editor.js`.
- Create-mode differences: no Version/History row, no Save Version (form posts to
  `Create`); keep `#template-body` + palette functional (fields/blocks work pre-save;
  SaveVersion/snippets guarded by `templateId` in JS as in origin).

## Task 6 — Rewrite `_VersionHistory.cshtml`

- Origin card contract: `.tb-version-card` (+ `.is-current`), `.tb-version-num`,
  `.tb-version-badge` "Current", `.tb-version-comment`, `.tb-version-meta`
  (`dd MMM yyyy · comment`), buttons Restore (`restoreVersion(this, versionId, sourceVersionNumber)`)
  and Compare (`openCompareView(this)` + `data-version-id|num|comment|created-at`).
- No layout (already `PartialView`). No controller change.

## Task 7 — Sample host layout + wiring

- `samples/TemplateBuilder.SampleMvc5Host/Views/Shared/_Layout.cshtml`: load
  `/TemplateBuilderEditor/css/suneditor.min.css` + `template-editor.css`,
  `/TemplateBuilderEditor/js/suneditor.min.js` + `template-editor.js`; drop CDN.
- Home page links unchanged.

## Task 8 — Build, embed check, package

- `dotnet build` editor; verify embedded resources list contains suneditor + editor assets.
- `dotnet pack -c Release -o ./nupkg`; extract; verify assets present, no cshtml leakage.
- Update package README wiring snippet if the asset list changed (it didn't — same route).

## Task 9 — xsp4 end-to-end smoke

- Rebuild sample host, clean-restart xsp4.
- Curl: list (new markup markers: `tb-stats-sidebar`, `duplicate-modal`, badges),
  edit (markers: `tb-editor-grid`, `field-palette`, modals), suneditor css/js 200 with
  correct content-types, SaveVersion 200, Preview 200, Validate, Versions partial
  (tb-version-card), VersionBody, snippets list/create/delete, duplicate, toggle,
  `/_setup` 3× PASS. Node-check ported JS first.

## Task 10 — Commit + dashboard

- Update `DASHBOARD.md` (repo root): task statuses + how-to-run.
- Commit (conventional message), BLOCKERS/PROGRESS notes if any adaptation discovered.

## Verification checklist

- [ ] `node --check` on ported JS
- [ ] editor + sample build clean
- [ ] embedded resources contain suneditor.min.js/css
- [ ] nupkg extract: assets present, no cshtml leakage
- [ ] smoke: list/edit markup markers, all endpoints 200, `/_setup` 3× PASS
- [ ] DASHBOARD.md reflects final state