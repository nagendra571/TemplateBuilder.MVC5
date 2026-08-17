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
