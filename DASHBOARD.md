# TemplateBuilder.Editor.Mvc5 — Parity Dashboard

**Mission:** bring the Mvc5 fork's editor UI/UX to parity with the .NET 8/10 product
(`http://templatebuilder.runasp.net/`). Server surface already matches 1:1 — the work is
frontend (views, CSS, JS, embedded assets).

**Spec:** `docs/superpowers/specs/2026-08-17-parity-editor-ui-design.md`
**Plan:** `docs/superpowers/plans/2026-08-17-parity-editor-ui-implementation.md`

## Gap summary (Mvc5 fork vs .NET 8/10 origin)

| Capability | Mvc5 (before) | Mvc5 (now) | Origin (.NET 8/10) |
|---|---|---|---|
| WYSIWYG canvas (SunEditor) | textarea only | ✅ | ✅ |
| 3-panel layout | single-column form | ✅ | ✅ |
| Field palette (SQL views, drag/insert) | none | ✅ | ✅ |
| Loop / Grid / Conditional blocks | none | ✅ wizards | ✅ wizards |
| Snippets UI in editor | API only | ✅ list + save-selection | ✅ |
| Autosave drafts + recovery | none | ✅ localStorage | ✅ |
| Version compare modal | none | ✅ side-by-side iframes | ✅ |
| Preview modal + auto-fill sample | inline div | ✅ | ✅ |
| Find & Replace panel | 2 inputs | ✅ floating panel | ✅ |
| Special characters | none | ✅ panel | ✅ |
| Theme (dark/light, persisted) | basic toggle | ✅ | ✅ |
| Word count bar | none | ✅ | ✅ |
| Toasts | inline msg | ✅ | ✅ |
| Index: stats sidebar, badges, duplicate modal, inline toggle | plain table | ✅ | ✅ |

## Task status

| # | Task | Status |
|---|---|---|
| 1 | Embed SunEditor 2.47.10 (self-hosted) | ✅ done |
| 2 | Port origin CSS (1356 lines) + compat block | ✅ done |
| 3 | Port origin JS (2098 lines) + node-check + guarded init | ✅ done |
| 4 | Rewrite `Index.cshtml` (stats/badges/duplicate modal) | ✅ done |
| 5 | Rewrite `Edit.cshtml` (3-panel + modals) | ✅ done |
| 6 | Rewrite `_VersionHistory.cshtml` (card + compare contract) | ✅ done |
| 7 | Sample host `_Layout`: self-hosted assets | ✅ done |
| 8 | Build + embedded-resource + pack verification | ✅ done |
| 9 | xsp4 end-to-end smoke | ✅ done |
| 10 | Commit + finalize dashboard | ✅ done |

## Verification evidence (Task 8–9, 2026-08-17)

- Editor build: 0 errors (3 pre-existing warnings).
- `GetManifestResourceNames` (mono): all 4 assets embedded
  (`suneditor.min.css|js`, `template-editor.css|js`).
- nupkg contents: 4 DLLs + `tools/install.ps1` + README — **no `.cshtml` leakage**.
- Sample host: clean `xbuild` (0 errors, 42 bin files), package reinstalled.
- xsp4 smoke (all 200 unless noted): `/`, `/Templates`, `/Templates/2/Edit`,
  `/Templates/Create`, 4 asset URLs (correct content-types), `/Templates/_setup`,
  `/Templates/_setup/layout-probe`.
- Endpoint flow (antiforgery header): SaveVersion → `{"versionId":8,"versionNumber":4}`,
  Versions partial (4 cards, 1 `is-current`, Compare+Restore buttons), VersionBody,
  Preview (200, html), Validate (`valid:false` for bad template — correct), Snippets
  POST/GET/DELETE (204), Duplicate → `{"id":3}`, ToggleActive 200, Restore 200.
- Page markers: Index (stats sidebar, badges, duplicate modal, search, theme-light),
  Edit (3-panel grid, palette, snippets, all 8 modals/panels, view-selector, templateId),
  Create (btn-create-submit, token, **no** version UI).

## Notes for consumers / testers

- **Antiforgery:** all JSON endpoints need `RequestVerificationToken` header — the JS
  sends it (`_csrf`). Mono quirk: curl requests with a *nested JSON object* in the body
  (e.g. `"modelJson":{...}`) trip mono's form parser and the header check fails with
  HttpAntiForgeryException — **the real client never sends nested objects** (every value
  is a string from form fields/textarea), so this is a test-tooling artifact only.
- **Create mode:** `templateId = 0`, no Save Version / Preview / History / save-comment —
  form posts to `Create` (stock `[ValidateAntiForgeryToken]`, form-encoded).
- **SunEditor init is guarded** (`if (_canvasEl)`) — the JS can be loaded on any page
  (Index/Setup included) without crashing.

## How to run / verify

- Editor build: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo`
- Sample host: `xbuild samples/TemplateBuilder.SampleMvc5Host/TemplateBuilder.SampleMvc5Host.csproj /p:Configuration=Debug`
  (errors print `error :` — grep that, not just `error CS`; mono xbuild resolves
  WebPages from GAC — the csproj copy target handles it, BLOCKERS #16)
- Serve: xsp4 on `http://localhost:8081` (see BLOCKERS #11 recipe); `/Templates/_setup` for diagnostics
- Editor UI markers: `/Templates` (stats sidebar, badges), `/Templates/{id}/Edit` (3-panel,
  SunEditor), `/TemplateBuilderEditor/js/suneditor.min.js` (200)