# PROGRESS — TemplateBuilder.Editor.Mvc5 unattended build

Run started: 2026-08-17 ~04:00 UTC
Environment: Linux (Debian 11 bullseye), Docker available, nuget.org reachable, **no Windows tooling**
(no Visual Studio, no IIS Express, no LocalDB). dotnet CLI installed during this run.

Documented environment adaptations (see BLOCKERS.md for full detail):
- Origin repo (`C:\Users\nchinnam\source\repos\TemplateBuilder`) does not exist in this environment;
  Domain/Application are reconstructed from the plan-embedded signatures (the plan's designated fallback).
- EF6 tests run against SQL Server in Docker (plan-sanctioned: "adjust the connection string in the
  test to a reachable SQL Server instance") instead of LocalDB.
- net48 xunit tests cannot run under `dotnet test` on Linux (no .NET Framework runtime in vstest);
  run via xunit.console under Mono, verified by exit code + test summary. Substitution documented
  at each affected gate.
- IIS Express hosting gates (Tasks 11/14) cannot run on Linux; attempted with Mono's xsp4 host
  if feasible, otherwise reported as environment gaps.

| Task | Gate command | Actual output summary | Status |
|---|---|---|---|
| 1 | `dotnet build TemplateBuilder.Mvc5.sln` | PASS: `Build succeeded. 0 Warning(s) 0 Error(s)`; 7 projects in sln. Adaptation: RazorGenerator.Mvc pinned 2.4.9 (2.5.0 doesn't exist on nuget.org); `<LangVersion>latest</LangVersion>` added to all csprojs (net48 defaults to C# 7.3; plan's `Nullable enable` needs 8+). Commit `0fe226f`. | ✅ PASS |
| 2 | `dotnet test tests/TemplateBuilder.Domain.Tests/` | PASS: 16/16 passed (`Failed: 0, Passed: 16`). Reconstructed from plan-embedded signatures (origin repo absent — BLOCKERS #1). `dotnet test` works for net48 on Linux with Test.Sdk 18.9.0 (BLOCKERS #2 superseded). Commit after fix. | ✅ PASS |
| 3 | `dotnet test tests/TemplateBuilder.Application.Tests/` | PASS: 22/22 (`Failed: 0, Passed: 22`). Reconstructed from plan-embedded signatures (BLOCKERS #1). DB-backed MDS tests moved out of suite (mono host can't run MDS — BLOCKERS #6); `SqlViewDiscoveryService`/`SchemaVersionValidator` verified against Docker SQL Server via .NET 8 harness: view names `[v_TestContacts]`, columns Id/FirstName/Email w/ MaxLength 100/200, schema-version view null→1. Fixed `ReadSchemaVersionAsync` SqlException-208→null bug found by harness. Commit after run. | ✅ PASS |
| 4 | `dotnet build src/TemplateBuilder.Infrastructure.EF6/` + table check | PASS: build `0 Error(s)`; table check via sqlcmd on docker SQL: `Templates`, `TemplateVersions`, `Snippets`, `__MigrationHistory` — exactly 4 rows, nothing else. Adaptations: plan's `Entity<T>(Action)` is EF-Core syntax (BLOCKERS #7); SUCCESS_CRITERIA's hand-written migration had wrong index names + context-not-constructible — regenerated via EF6 `MigrationScaffolder` (headless Add-Migration) + added `TemplateBuilderDbContextFactory` (BLOCKERS #8). | ✅ PASS |
| 5 | `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/` | PASS: 11/11 (`Failed: 0, Passed: 11`) against real SQL Server in Docker (LocalDB is Windows-only — BLOCKERS #3). xunit `[Collection("Database")]` added to serialize the two DB test classes (parallel drop/create race). Commits `e6e5c86`, `b30b1b0`. | ✅ PASS |
| 6 | `dotnet build src/TemplateBuilder.Editor.Mvc5/` | PASS (2026-08-17 ~08:20 UTC): `Build succeeded. 0 Warning(s) 0 Error(s)`. Options/Auth/Unity registration. Commit `549e9b9`. | ✅ PASS |
| 7 | `dotnet build src/TemplateBuilder.Editor.Mvc5/` | PASS: build clean. `HttpRequestJsonExtensions` + `TemplateBuilderControllerBase` (files existed pre-session, committed now). | ✅ PASS |
| 8 | `dotnet build` + grep 12 routes | PASS: build clean; TemplatesController has 10 `[Route(...)]` attributes matching the plan's Task-8 code verbatim (Edit, SaveVersion, Versions, Versions/{id}/Body, Restore, Api/Views/{name}/Columns, Preview, ToggleActive, Validate, Duplicate) + `Index` and `Create` as conventional routes (GET `/Templates`, GET+POST `/Templates/Create`) = the 12-route surface. Commit `e818a33` (+ csproj/views now). | ✅ PASS |
| 9 | `dotnet build` + grep 3 snippet routes | PASS: `[Route("Templates/Api/Snippets")]` ×2 (GET all / POST create) + `[Route("Templates/Api/Snippets/{id:int}")]` (DELETE) — all 3 present. Commit `341a2be`. | ✅ PASS |
| 10 | `dotnet build` | PASS: build clean; SetupController routes `Templates/_setup` + `Templates/_setup/layout-probe` present (plan Task 10). Commit `341a2be`. | ✅ PASS |
| 11 | `/Spike/Hello` HTTP 200 + marker text | PASS (2026-08-17 08:33 UTC): `curl http://localhost:8081/Spike/Hello` → `HTTP 200`, body contains `RazorGenerator works: 2026-08-17T08:33:04Z`. Hosted with mono xsp4 built from source (Debian's 4.2-2.2 xsp4 crashes on mono 6.8 — BLOCKERS #11). RazorGenerator pipeline validated end-to-end (codegen → compile → `PageVirtualPathAttribute` → `PrecompiledMvcEngine` → MVC5 render). | ✅ PASS |
| 12 | build + `/Templates`, `/Templates/Create` HTTP 200 | PASS (2026-08-17 ~14:20 UTC): editor build `0 Error(s)` (2 pre-existing nullable warnings). Sample host + xsp4: `/Templates` 200 (`<h1>Templates</h1>`, filter bar, type chips, empty-state "No templates found"), `/Templates/Create` 200 (Create Template form: Name/TemplateType/Description/Body textareas, `tb-*` ids, type select, find/replace + preview toolbar, Render Preview button), POST create → 302 `/Templates/Edit/1` → 200 (edit form, body pre-filled), `/Templates/GetVersionHistory/1` → 200 (`_VersionHistory` tuple-model partial: "Initial version" + View/Restore). SQL view discovery returns 4 views (live Docker SQL). Blocked twice en route (BLOCKERS #12): mono `HttpContext.Current` null on async continuations → `MonoFlowActionInvoker` shim; MDS 7.0.2 unusable on mono/Linux → `System.Data.SqlClient` fork deviation in `Application`; sample host `Web.config` + `connectionStrings` entry for the EF6 initializer factory. | ✅ PASS | |
| 13 | static CSS/JS HTTP 200 + content-type | PASS (2026-08-17 ~15:00 UTC): `curl http://localhost:8081/TemplateBuilderEditor/css/template-editor.css` → 200 `text/css` (7790 B); `/js/template-editor.js` → 200 `application/javascript` (11659 B); unknown path → 404. Assets reconstructed (origin wwwroot absent — BLOCKERS #1 pattern), embedded with explicit `LogicalName`s (verified via `GetManifestResourceNames`), served by `TemplateBuilderStaticAssetsRouteHandler`; `TemplateBuilderEditorRouteConfig.RegisterRoutes` now also enables `MapMvcAttributeRoutes()` in the sample host (Edit/Versions/_setup/layout-probe all 200). Commit after Task 12. | ✅ PASS | |
| 14 | IIS Express + end-to-end flow | PASS (2026-08-17 ~16:00 UTC) on mono xsp4 (IIS Express is Windows-only — BLOCKERS #9; sample host runs under xsp4): full flow verified via curl — Home renders with editor links; `/Templates` list (shows updated name); form Create → 302 → `/Templates/4/Edit` (stock `[ValidateAntiForgeryToken]`); Edit GET shows `Invoice v3` + version label; SaveVersion JSON POST (cookie + `RequestVerificationToken` header) → `{"versionId":4,"versionNumber":3}`; Versions page lists v1–v4 with View/Restore; Restore/1/1 → new version; VersionBody; ToggleActive; Validate (400 empty / 200 valid); Preview (rendered HTML); Duplicate → `/Templates/3/Edit` 200; Snippets list/create/delete (204); `/_setup` 3× PASS; assets 200 with correct content types; sample host now has `_ViewStart` + `_Layout` wiring the editor CSS/JS (consumer pattern mirrored from origin RCL). Three package-level fixes made while proving the flow (all BLOCKERS #13/#14/#15): custom `ValidateJsonAntiForgeryTokenAttribute` (stock MVC5 antiforgery is form-only — header support is ASP.NET Core-only, would have failed on the client's Windows IIS too); mono stream-drain fix (`TemplateBuilderControllerBase.OnActionExecuting` drops the Form value provider for `application/json` — mono's `Request.Form` consumes the body during parameter binding); static-asset route no longer matches URL generation. Probe controller used for diagnosis removed before commit. | ✅ PASS | |
| 15 | `dotnet pack` + nupkg extraction | PASS (2026-08-17 ~16:10 UTC): `dotnet pack -c Release -o ./nupkg` → `TemplateBuilder.Editor.Mvc5.1.0.0.nupkg`. Extracted and inspected: `lib/net48/` contains all 4 DLLs (Editor + Domain + Application + Infrastructure.EF6 via `BundleInternalAssemblies` target); `tools/install.ps1` (binding-redirect guidance for Newtonsoft.Json 13 + EF6 6.5.1) and root `README.md` present; NO `.cshtml` leakage; static assets confirmed embedded in the release DLL (`GetManifestResourceNames` → both `StaticAssets.*` names) alongside `TemplateBuilderStaticAssetsRoute` + `ValidateJsonAntiForgeryTokenAttribute` (MONO_PATH resolution check). nuspec dependencies correct (Mvc 5.3.0, Newtonsoft 13.0.3, RazorGenerator.Mvc 2.4.9, Unity 5.11.10, Unity.Mvc5 1.4.0). | ✅ PASS | |
## 2026-08-20 — Audit page redesign + Activity drawer (Edit page)

Design via brainstorming (visual companion mockups, 3 placement options) → user picked the
slide-in drawer. Design approved before implementation.

| # | Task | Evidence | Status |
|---|---|---|---|
| 1 | `IAuditStatsRepository` + `AuditStatsRepository` (daily buckets, by-type counts, unique actors) + shared `AuditFiltering` — TDD | RED (type missing) → GREEN 5/5 new tests; full EF6 suite 30/30 vs Docker SQL | ✅ PASS |
| 2 | `GET /Audit/Stats` + `AuditIndexViewModel` extension + Unity registration | `/Audit/Stats` → `{"total":2,...,"buckets":[{date,count}×30]}`; build 0 errors (only pre-existing warnings) | ✅ PASS |
| 3 | CSS: drawer section + full `.tb-audit-*` page (2006-line file) | Assets serve updated CSS (grep markers) | ✅ PASS |
| 4 | JS: drawer (open/close/Esc/grouped timeline) + audit module (chart SVG, 30s poll, expand rows, relative time, JSON+string diff highlight) | `node --check` OK; agent-browser DOM assertions | ✅ PASS |
| 5 | Views: Edit.cshtml drawer markup; Audit/Index.cshtml rewrite | RazorGenerator codegen regenerated (obj/CodeGen contains both views); sample host renders | ✅ PASS |
| 6 | Layout regression (the original bug): 5-event drawer open vs closed | Grid 1264×591 unchanged; center panel 704×591; canvas intact; drawer overlays right edge 361×591 | ✅ PASS |
| 7 | Audit page end-to-end | 5 stat chips, 30 bars + axis, badges per action, detail rows (JSON diff + "Draft"→"Review" string diff both show `.chg`), pagination "Showing 1–5", empty state on filtered-out query, 768px/375px: 3/2-col stats, no page h-scroll | ✅ PASS |
| 8 | Repack → sample host reinstall → xbuild → xsp4 smoke | Fresh package install, xbuild clean, `/Audit` 200 (first boot 500 = EF init race, retried), `/Templates` 200, create→submit→approve flow via agent-browser | ✅ PASS |

Environment notes learned this session (see MEMORY.md for detail): xsp4 restart needs
`MONO_PATH` + `setsid`; sample host shares `TemplateBuilderMvc5Tests` with the EF6 suite (stop
xsp4 before `dotnet test`); `pkill -f Mono.WebServer.XSP` self-kills the shell.

## 2026-08-20 — Lifecycle & Ops (export/import, health check, bulk ops) — Tasks 1–11

Session recovered mid-plan after an environment crash: Tasks 1–3 were already implemented but
unverified; Tasks 4–11 executed in this session. All gates below run against the fresh state.

| # | Task | Evidence | Status |
|---|---|---|---|
| 1 | Domain foundation (ExternalKey/SourceView/Snapshot, Imported, DeleteAsync, AddLifecycleOps migration + NEWID backfill) | EF6 suite incl. `TemplateLifecycleColumnsTests` 4/4; sqlcmd shows ExternalKey/SourceView/SourceViewSnapshot columns + unique index `IX_Templates_ExternalKey` in `TemplateBuilderMvc5Tests` | ✅ PASS |
| 2 | ITemplatePromotionRepository + EF6 impl (AddWithVersions, AppendVersions, UpdateFromImport, GetByExternalKey) | EF6 `TemplatePromotionRepositoryTests` 3/3; full suite 37/37 | ✅ PASS |
| 3 | Export document builder (camelCase JSON, SanitizeFileName) | Application `TemplatePromotionServiceTests` 3/3 | ✅ PASS |
| 4 | Import orchestration (schema check, Scriban validation, locked-skip, status collapse, audit) | Application `TemplatePromotionImportTests` 9/9. Scriban probe: `Template.Parse` is lenient for unterminated `{{` (HasErrors=False) — the invalid-body test uses `{{ end }}` (real parse error) | ✅ PASS |
| 5 | Bulk ZIP packaging (per-template files + `_summary.json`) | Application `TemplatePromotionBulkZipTests` 1/1; added `<Reference Include="System.IO.Compression" />` to Application + test csproj (net48 needs it) | ✅ PASS |
| 6 | Health check engine (Scriban AST token extraction + snapshot drift) | Application `TemplateHealthServiceTests` 5/5. Adaptations: snapshot JSON shape is `{ takenAt, columns }` (BuildSnapshotJson shape, not a bare array); missing view = empty column list (SqlViewDiscoveryService never throws); this fork's Scriban-native `{{ for }}`/`{{ if }}` syntax (liquid `{% %}` parses as raw text) | ✅ PASS |
| 7 | Controllers/endpoints/Unity (Export, Import, Bulk*, Health, HealthController, registration) | Editor build 0 errors; `AuditActions.Deleted` added (was absent). Smoke fix: `BulkIdsRequest.Ids` is `List<int>` — `int[]` = `Array.Empty` made MVC's DefaultModelBinder `ReplaceCollectionImpl` throw "Collection is read-only" on POST | ✅ PASS |
| 8 | Views + CSS (bulk bar, health page, import modal, source-view select, health panel) | Codegen regenerated (`obj/CodeGen/Views/Health/Index.cshtml.cs` + `tb-bulk-bar`/`tb-health-panel` in Index/Edit codegen). Fix: import modal uses `.modal-overlay.open` class (the `hidden`-attribute plan variant would never display — overlay CSS is `.open`-driven) | ✅ PASS |
| 9 | Editor JS modules (bulk, badges, import modal, editor health) | `node --check` OK. Fix: **Index.cshtml had an inline `const _csrf`** that collided with the external `template-editor.js` `const _csrf` — duplicate global declaration threw a SyntaxError that silently killed the whole external script on the list page (first page where external-JS features run); removed the inline declaration | ✅ PASS |
| 10 | End-to-end (pack → sample host → xsp4 → curl + agent-browser) | Suites: Domain 20/20, Application 86/86, EF6 37/37. xsp4: create → `Export/1` attachment (camelCase JSON incl. versions) → `/Templates/1/Health` (tokens+findings) → `/Health` page (4 chips, finding rows) → `Summaries?ids=1` → BulkActivate/Deactivate `{"succeeded":[1]}` → BulkExport ZIP (`Lifecycle_Smoke.template.json` + `_summary.json`) → Import multipart: `updated ... versionsAppended:1`, then `skipped: Target is Review (locked)` after SubmitForReview → bound template to `v_HealthProbe` (SaveVersion + sourceView) → `ALTER VIEW` dropped Email + widened FirstName → health shows `column_missing` critical + `column_length_changed` warning → BulkDelete removes rows, `/Audit` shows deleted/toggled_active/imported records. agent-browser: health badges populate (2 issues / 1 warning), bulk toolbar count, deactivate→rows Inactive, activate→Active, import modal open/close, editor Source View select + Health button panel, screenshots `/tmp/opencode/lifecycle-*.png`. xsp source was wiped by the crash — rebuilt from mono/xsp@72b24c0 (AssemblyInfo from `.in`, SignAssembly=false) | ✅ PASS |
| 11 | Docs + memory | README features table + new **Lifecycle & Ops** section; PROGRESS gate table; MEMORY.md entries | ✅ PASS |

Plan deviations worth knowing (also in MEMORY.md):
- Mono test-host crash (the "sudden logout"): `dotnet test` for net48 spawns a mono test host
  that can hard-crash the whole session (`mono_crash.*.json` dumps). A re-run passes; the
  previous session died on exactly this during Task 3 verification — no work was lost.
- The plan's liquid-style `{% for %}` test bodies were switched to Scriban-native `{{ for }}` —
  this fork (like the origin editor) uses Scriban-native block syntax; `{% %}` parses as raw text.
- Commits: skipped per repo rule (only commit when explicitly asked). All lifecycle-ops files
  are uncommitted; see `git status` for the full list.

## Final verification checklist (2026-08-17)

- [x] `dotnet build TemplateBuilder.Mvc5.sln` — zero errors
- [x] `dotnet test` Domain 16/16, Application 22/22, Infrastructure.EF6 11/11 (one transient DB-race failure, clean on re-run)
- [x] `dotnet pack` → nupkg with `lib/net48/` containing all 4 DLLs; contents extracted and inspected
- [x] Sample host end-to-end create → edit → save version → history → restore → duplicate → toggle → validate → preview → snippets on xsp4 (IIS Express is Windows-only — BLOCKERS #9)
- [x] `/Templates/_setup` all checks PASS
- [ ] Binding-redirect guidance in `install.ps1` validated against a real `packages.config`-style install — **deferred to client integration** (needs the client's actual dependency versions)
- [ ] Bootstrap 3.3.7 / jQuery 3.7.1 / IgniteUI CSS collision checked against a real Bootstrap-v3 host page — **deferred to client integration** (sample host by design loads no Bootstrap)
## Post-build: package consumption validated in the sample host (2026-08-17)

- `mono nuget.exe install TemplateBuilder.Editor.Mvc5 -Version 1.0.0 -Source ./nupkg` into
  `samples/TemplateBuilder.SampleMvc5Host/packages/` (packages.config-style layout) pulled the
  package + full dependency tree.
- Found + fixed a packaging bug: the nuspec was missing the runtime deps of the bundled
  netstandard2.0 assemblies (Scriban 7.2.6 needs `System.Text.Json` at render time; its own
  netstandard2.0 dependency group is not honored for net48 consumers — BLOCKERS #16). Editor
  csproj now declares `System.Text.Json 10.0.8` explicitly → nuspec carries it.
- Sample csproj rewritten: all references point at `packages\` lib paths (no editor-bin
  dependency); `CopyPackageAssemblies` target ships the runtime-only netstandard2.0/net462
  closure (Scriban, HtmlSanitizer, AngleSharp(+Css), Microsoft.Extensions.*, System.Text.Json,
  System.IO.Pipelines, shims, WebPages/Razor 3.0.0.0 dlls — mono xbuild resolves the WebPages
  family from the GAC, BLOCKERS #16). Clean xbuild: 0 errors, 41 dlls.
- Full flow re-verified against the package-consumed build on xsp4: list, form create (302 →
  Edit), JSON SaveVersion (`{"versionId":6,"versionNumber":2}`), duplicate-name rejection
  (VALIDATION_ERROR), Preview with modelJson (200), Validate, Versions (v2), Restore, Snippets,
  assets 200 with correct content-types, `/_setup` 3× PASS.
- `packages/` dir is gitignored — restore with the `nuget.exe install` command above
  (`-ConfigFile /tmp/opencode/nuget-cfg.txt` is machine-local; on Windows use VS Package
  Manager with the local `nupkg/` folder as source).

## 2026-08-17 — Editor UI/UX parity with .NET 8/10 origin (Tasks 1–10 of the parity plan)

- Gap analysis: server surface already 1:1 (all routes/payloads). The entire gap was
  frontend — closed by porting the origin's own assets + markup (approach A, see spec
  `docs/superpowers/specs/2026-08-17-parity-editor-ui-design.md`).
- SunEditor 2.47.10 embedded as embedded resources (no CDN — corporate client):
  `suneditor.min.js` (~2.5 MB) + `suneditor.min.css`; served by
  `TemplateBuilderStaticAssetsRouteHandler`; verified in the DLL via
  `GetManifestResourceNames`.
- `template-editor.css` = origin 1356 lines + 57-line compat block (setup/probe page
  classes live inside `#tb-editor-host`); `template-editor.js` = origin 2098 lines with one
  adaptation: `SUNEDITOR.create` init guarded by `#template-body` presence so the JS is
  safe to load on any page (Index/Setup).
- Views rewritten: `Index.cshtml` (stats sidebar, type/status badges, search, duplicate
  modal, inline toggle), `Edit.cshtml` (3-panel: field palette + blocks + snippets / SunEditor
  canvas + draft banner + validate + word count / properties + version row + save),
  `_VersionHistory.cshtml` (cards + Compare/Restore buttons with data-* contract for the
  compare modal). Create mode: `templateId = 0`, no version UI, form posts to `Create`.
- nupkg verified: 4 DLLs + install.ps1 + README, no `.cshtml` leakage. Sample host rebuilt
  from package (0 errors), full xsp4 smoke green (see DASHBOARD.md for the marker list).
- Mono/xsp4 quirk (test-tooling only): curl bodies containing a *nested JSON object*
  (`"modelJson":{...}`) fail the JSON anti-forgery header check with HttpAntiForgeryException
  — mono's form parser chokes on nested objects. The real client always sends string values
  (form fields/textarea), so this is unreachable in production; validated the exact JS
  payloads pass.

## 2026-08-19 — v1.1.0 sample-data authoring (Tasks 1-7 of the authoring-superpowers plan)

- Version bump `1.0.0 -> 1.1.0` in `TemplateBuilder.Editor.Mvc5.csproj`. The published
  `nupkg/TemplateBuilder.Editor.Mvc5.1.0.0.nupkg` is untouched (not modified, not re-packed,
  nothing pushed — publish requires explicit confirmation).
- `dotnet pack -c Release -o ./nupkg` -> `nupkg/TemplateBuilder.Editor.Mvc5.1.1.0.nupkg`
  (778152 B) alongside the 1.0.0 artifact. Extracted and inspected: file set identical to
  1.0.0 (only the psmdcp metadata guid differs) — `lib/net48/` all 4 DLLs,
  `tools/install.ps1`, root `README.md`, NO `.cshtml` anywhere. New types verified present in
  the packaged Application.dll via mono reflection (monodis absent from this box):
  `ISampleDataGenerator`, `SampleDataGenerator`, `ScribanReferenceEntry`,
  `ScribanReferenceCatalog`.
- Sample host upgraded from the package: `mono nuget.exe install TemplateBuilder.Editor.Mvc5
  -Version 1.1.0 -Source ./nupkg -ConfigFile /tmp/opencode/nuget-cfg.txt` into
  `samples/TemplateBuilder.SampleMvc5Host/packages/`; old `TemplateBuilder.Editor.Mvc5.1.0.0`
  package dir deleted; 4 HintPaths bumped to `1.1.0`; xbuild 0 errors (4 pre-existing
  CS1701/1702 warnings). **Environment gap found during smoke:** the host bin lacked
  `System.Web.Helpers.dll` (the editor's views call `Html.AntiForgeryToken()`; the previous
  session's server must have resolved it from an editor-bin `MONO_PATH` — unreproducible).
  Real NuGet installs of the WebPages 3.3.0 closure supply it, so one line was added to the
  host's `CopyPackageAssemblies` target (the only deviation from the plan's "4 HintPaths
  only"); fresh `xbuild` then reproduces a working bin (43 dlls incl. System.Web.Helpers).
- DB migration: `Templates.SampleData` column present before first request (baseline from the
  1.0.0 boot) and still present after first 1.1.0 boot — `AddSampleDataToTemplates`
  (202608182306535) applies via the existing `MigrateDatabaseToLatestVersion` initializer.
- xsp4 regression smoke (fresh server on `http://localhost:8081`, cookie jar + matching
  `__RequestVerificationToken` from the same page fetch): `/` 200; `/Templates` 200 (stats
  sidebar + type/status badges + `duplicate-modal`); `/Templates/Create` 200 (`btn-create-submit`,
  no version UI); form POST Create (token + cookie) -> 302 -> template id=1; `/Templates/1/Edit`
  200 (3-panel grid, SunEditor, `palette-search`, `ref-panel`); assets 200 with correct
  content-types (`application/javascript` / `text/css`); SaveVersion JSON ->
  `{"versionId":2,"versionNumber":2}`; Versions partial 200 (1 card, is-current);
  VersionBody -> `{"body":"Hi {{model.FirstName}} v1"}`; Validate 400 (empty body) / 200
  `{"valid":false,"message":"...nope_filter was not found"}` (broken template) / 200
  `{"valid":true}`; ToggleActive 200; Duplicate -> `{"id":2}`; Restore -> `{"versionId":4,
  "versionNumber":3}` (route needs the token header — bare POST 500s on antiforgery);
  Snippets GET 200 / POST -> `{"id":2,...}` / DELETE 204; `/_setup` 3x PASS.
- New-feature smoke (same token/cookie): `POST /Templates/Api/SampleData/Generate`
  `{"viewName":"v_TestContacts"}` -> 200 `{"sampleData":{"Id":42,"FirstName":"Jane Doe",
  "Email":"jane.doe@agency.gov"}}` (view was created in the fresh DB first — the v1.0.0 probe
  pattern); `{"templateBody":"Hi {{model.RecipientName}} — {{model.Amount}}"}` -> 200
  `{"sampleData":{"RecipientName":"Jane Doe","Amount":1250.00}}`; create-mode (templateBody
  only) -> 200; `PUT /Templates/1/SampleData` `{"sampleData":"{\"a\":1}"}` -> 200
  `{"saved":true}`; Edit reload contains `savedSampleData = "{\"a\":1}"`; Edit page HTML
  contains `palette-search`, `btn-ref-open`, `ref-groups` (7 `tb-ref-group` sections (15 items), 15
  `tb-ref-item` with code/label/output spans); JS 200 and contains `SampleData/Generate`,
  `btn-ref-open` open-handler, `palette-search` input handler, draft autosave (localStorage
  `tb-draft-*`); CSS 200 and contains `.tb-ref-panel`; Preview POST (body + modelJson) ->
  200 `{"html":"Hi Alice"}` (auto-fill/preview reuses savedSampleData per JS);
  draft save/restore and auto-fill code unchanged and present.
- Final gates: `dotnet build TemplateBuilder.Mvc5.sln` 0 errors (9 pre-existing warnings);
  Domain 16/16; Application 50/50 (baseline 22 + Task 1-2 suite incl. SampleDataGenerator 9 +
  ScribanReferenceCatalog 2 + new TemplateEngine cases); Infrastructure.EF6 13/13
  (baseline 11 + SampleData round-trip tests (migration validity came from the sqlcmd column check + host boot)); `node --check template-editor.js` OK;
  sample host xbuild 0 errors.
- Packaging follow-up (post-smoke): the fresh host boot exposed that the editor's views call
  `Html.AntiForgeryToken()` (System.Web.Helpers), which no package dependency supplied — real
  consumers would hit a missing-assembly error on the editor pages. Fixed by adding
  `Microsoft.AspNet.WebHelpers 3.3.0` to the Editor csproj (nuspec now declares it; latest
  published WebHelpers line — System.Web.Helpers stays at assembly v3.0.0.0 on MVC5).
  Repacked 1.1.0 and re-inspected the nuspec: dependency present. Sample host retains the
  local CopyPackageAssemblies addition for System.Web.Helpers.dll (packages.config hosts do
  not consume nuspec dependencies).

## 2026-08-21 — v1.2.0 two-state save model (Tasks 1-8 of the two-state-save plan)

Gate table — actual command outputs from the task reports; Task 8 not yet run.

| # | Gate | Command | Result (from task report) |
|---|---|---|---|
| 1 | Domain suite | `dotnet test tests/TemplateBuilder.Domain.Tests` | `Passed! - Failed: 0, Passed: 20, Total: 20` |
| 1 | EF6 suite | `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests` | `Passed! - Failed: 0, Passed: 40, Total: 40` (37 + 3 new) |
| 1 | Migration apply | sqlcmd on `mssql-tb` (AddVersionIsActive) | `IsActive NO ((1))`; `202608210016543_AddVersionIsActive` in `__MigrationHistory` |
| 2 | Engine contract | `dotnet test tests/TemplateBuilder.Application.Tests --filter "FullyQualifiedName~TemplateEngineTests"` | RED 13/19 → GREEN `Passed! - Failed: 0, Passed: 19, Total: 19` |
| 2 | Application suite | same, no filter | `Passed! - Failed: 0, Passed: 91, Total: 91` |
| 3 | Promotion suite | `--filter "FullyQualifiedName~TemplatePromotion"` | RED CS1061/CS0117 → GREEN `Passed! - Failed: 0, Passed: 9, Total: 9` |
| 3 | Application suite | same, no filter | `Passed! - Failed: 0, Passed: 86, Total: 86` (one transient mono host crash, re-run passed) |
| 4 | Solution build | `dotnet build TemplateBuilder.Mvc5.sln --nologo` | 0 errors, 24 warnings (all pre-existing) |
| 4 | Domain / Application / EF6 | dotnet test per project | 19/19, 72/72, 41/41 |
| 4 | Migration from empty | `DbMigrator` probe on scratch DB (`TemplateBuilderMvc5MigProbe`) | 6/6 migrations applied, ends `202608210121111_SimplifyTemplateStatus`; Templates has none of the 3 workflow columns |
| 5 | Editor build | `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Debug --nologo` | `Build succeeded. 0 Error(s), 6 Warning(s)` (pre-existing; RazorGenerator re-ran, 7 views regenerated) |
| 6 | JS syntax | `node --check src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` | exit 0, no output |
| 6 | Editor build | as Task 5 | 0 Errors, 21 warnings (all pre-existing); codegen contains `btn-save-draft` / `tb-badge-live` |
| 7 | Version + docs | csproj `<Version>` + README/PROGRESS/MEMORY | `1.2.0` (this task, verified by grep); docs updated |
| 8 | End-to-end smoke | Task 8 gate commands | executed in Task 8 |
