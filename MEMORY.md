# MEMORY

Persistent cross-session memory for agents working in this repo. Maintained via the `memory` skill in `.opencode/skills/memory/`. Append newest entries first; never delete history.

## Memories

### 2026-08-21: Connection-string fix (v1.3.1) — EF6 runtime migrations bypass the design-time factory

- Client bug: "No connection string named 'TemplateBuilderDbContext' could be found" despite `options.ConnectionString` being set. Root cause: EF6's `DbMigrator` (inside `MigrateDatabaseToLatestVersion`) discovers `IDbContextFactory<T>` **by convention** (`{ContextName}Factory` in the context's assembly) at RUNTIME; `TemplateBuilderDbContextFactory.Create()` hardcoded `"name=TemplateBuilderDbContext"` (a design-time-only assumption). The sample host masked the bug by shipping BOTH connection strings in web.config.
- Fix: `TemplateBuilderDbContextFactory.ConnectionStringProvider` (public static `Func<string>?`), set by `RegisterTemplateBuilderEditor` to `options.ConnectionString`; `Create()` uses it when set, falls back to the named string (design time — PM console tooling unchanged). Regression tests: `ConnectionStringResolutionTests` (3 tests, `[Collection("Database")]`).
- **Rejected approach (lesson):** a `DbConfiguration`-derived class with `SetContextFactory` — DbConfiguration is a per-AppDomain singleton instantiated on FIRST EF use, so provider-set timing is order-dependent (works in isolation, fails in a full test run). A provider read at factory-`Create()` time is order-independent.
- xsp rebuild recipe corrections: `AssemblyInfo.cs.in` uses `@XSP_VERSION@` delimiters (sed to `4.6.0.0`); `AssemblyInfo4.cs.in` lives in `src/Mono.WebServer/` (plain `cp`); `SignAssembly=false` in both csprojs. First xbuild error "CS2001 AssemblyInfo4.cs" → generate it before building XSP.
- Docker `mssql-tb` can exit silently (Exited 255) — check `docker ps` before blaming test failures: `SNI_ERROR_40` (connection-level) means the server is down, not the test.

### 2026-08-21: Origin handoff batch 3 — Actor Resolver (v2.3.0)

- Handoff docs for the origin agent (in THIS repo's `docs/superpowers/`): `2026-08-21-origin-actor-resolver-design.md` (decisions R1–R13), `-implementation.md` (5 TDD tasks), `handoff/2026-08-21-origin-actor-resolver-implementation-prompt.md`. Feature: `TemplateBuilderEditorOptions.ActorResolver` (`Func<HttpContext, string?>`) → `CreatedBy`/audit `Actor`, fallback chain, no backfill, 2.3.0.
- **Origin-specific adaptations vs the fork** (the parts that differ): the origin KEEPS Create-publishes-v1 → stamping covers **4 sites** (Create, SaveVersion, RestoreVersion, Duplicate), not the fork's 3; cache lives inside `ActorResolverChain` (key `TemplateBuilder.Editor.Actor`) because the origin has no controller base; supersedes 2.2.0's hardcoded `CurrentActor` body; no snippet stamps/UI (origin has no SnippetVersion/SnippetUsage); Moq + `DefaultHttpContext` instead of NSubstitute.
- The fork's own 1.3.0 review caught a stale "Current version: 1.2.0" header inside the nupkg README — the origin plan's Task 5 pack step greps the extracted README for both the new section AND the version header.

### 2026-08-21: Actor resolver feature (v1.3.0) — custom CreatedBy for consumers

- New public API: `TemplateBuilderEditorOptions.ActorResolver` (`Func<HttpContextBase, string?>`) — consumers set it in their existing `RegisterTemplateBuilderEditor(options => ...)` call (e.g. claims: `ctx.User?.FindFirst("sub")?.Value`). Resolution chain in `ActorResolverChain` (internal, in `src/TemplateBuilder.Editor.Mvc5/ActorResolverChain.cs`): resolver → `User.Identity.Name` → `"anonymous"`, truncated to 200 chars (column max), resolver exceptions propagate. `CurrentActor` caches the result once per request in `HttpContext.Items["TemplateBuilder.Editor.Mvc5.Actor"]` (15+ call sites per request).
- **Gap fixed:** `TemplateVersion.CreatedBy` was NEVER populated before this feature (only SnippetVersion was); the editor now stamps `CreatedBy = CurrentActor` in SaveVersion/RestoreVersion/Duplicate (TemplatesController.cs:145,198,335). No backfill of legacy rows; UI renders "anonymous" for null authors (`_VersionHistory.cshtml` + `template-editor.js` snippet meta).
- **Anti-forgery header gotcha (e2e/curl):** `ValidateJsonAntiForgeryTokenAttribute` reads header `RequestVerificationToken` — NO `X-` prefix. The editor JS sends the same name. A curl POST needs: GET `/Templates/` with `-c` jar → extract hidden `__RequestVerificationToken` from THAT SAME page (cookie+token are request-paired) → POST with `-b jar` + `-H "RequestVerificationToken: <token>"`. Mixing pages/jars gives "cookie token and form field token do not match".
- First test project for the Mvc5 layer: `tests/TemplateBuilder.Editor.Mvc5.Tests` (xunit + FluentAssertions + NSubstitute, net48, `<Reference Include="System.Web" />`, `InternalsVisibleTo` in the Mvc5 csproj). Package version bumped to 1.3.0; sample host demos the resolver via an `X-TB-Actor` header (spoofable — demo-only caveat in code + docs).
- Supersedes the audit-batch memory's "CurrentActor = User?.Identity?.Name" as the identity source — it is now the resolver chain (fallback preserves the old behavior when no resolver is set).
- **Subagent-report gotcha:** a Task implementer's final report message can corrupt on delivery (garbled `</parameter>` spam) while the work is fully done in the environment — verify by inspecting the working tree/artifacts and reconstructing the report from observed evidence rather than re-dispatching blindly.

### 2026-08-21: Origin port docs batch 2 — audit log + activity drawer

- Origin next-batch handoff docs (audit + activity only; Health stays inside the lifecycle-ops docs per owner Q1): `docs/superpowers/specs/2026-08-21-origin-audit-activity-design.md` + `-implementation.md` (6 tasks, version 2.2.0).
- Audit action set verified against the origin's two-state surface: template `created/draft_saved/published/restored/duplicated/toggled_active/imported/deleted`; snippet `snippet_created/snippet_deleted` ONLY (origin has list/create/delete snippet endpoints — no update/versions → no `snippet_edited`/`snippet_restored`; no workflow → no submitted/approved/rejected/review_cancelled).
- **Supersedes lifecycle L13**: origin import + bulk delete DO record audit (`imported`/`deleted`). Spec A4 is the authority; the other agent's lifecycle plan (L13 "no audit wiring") must not be treated as final.
- Wiring gotchas baked in: `SaveVersion` audits `draft_saved` vs `published` by `version.IsActive`; `CurrentActor` = `User?.Identity?.Name ?? "anonymous"` as a protected property on Templates/Snippets controllers (origin has no controller base); controllers carry BOTH `IAuditService _auditService` and `IAuditRepository _auditRepository` (distinct fields — timeline endpoint uses the repository, wiring uses the service).
- Fork reference shapes for the port: AuditLog entity max lengths (EntityType 20, Action 40, Actor 200, Before/AfterState 4000, Comment 1000), AuditQuery/AuditFiltering semantics (To-exclusive `< To.Date.AddDays(1)`, From-inclusive, Search across Action/Actor/Comment), AuditStats (30-day window, sequential queries — EF Core shares the no-concurrent-async rule), CSV (8 columns, quoting, UTF-8 BOM), audit page + drawer element ids, JS helpers fmtRelative/fmtDayLabel/actionKind. Fork anchors: template-editor.js `initAuditPage` ~2671, drawer ~2176; Edit.cshtml drawer markup in fork Section 34 CSS.

### 2026-08-21: Origin port docs (TemplateBuilder.Editor) — two-state + lifecycle specs/plans

- Origin repo `github.com/nagendra571/TemplateBuilder` is **PRIVATE**; `main` is the only branch, at ~1.5.2-era (commit 194cf15). **NuGet 1.6.0 (SampleData endpoints) is published but NOT on main** — docs target main and flag this. User's fine-grained PAT `github_pat_11AH...` exists for API access (treated as exposed — rotate after use).
- Origin stack (differs from fork in ways the port docs must respect): net8.0+net10.0 multi-target Razor RCL, **System.Text.Json (no Newtonsoft)**, EF Core 8/10 (configurations in `Infrastructure/Data/Configurations/`, design-time factory `TemplateBuilderDbContextFactory` exists, `MigrationHostedService`), `TemplateBuilder.Core` render-only package shares `TemplateEngine`, `GetAllAsync`/`GetCurrentVersionIdAsync` **filter IsActive**, `TemplateEngine` has IMemoryCache body caching keyed `tb_{id}` by version id + CaseInsensitiveScriptObject (origin has what the fork lacks), tests use Moq + InMemory EF, e2e host `TemplateBuilder.Web` at localhost:7275.
- 4 handoff docs written to THIS repo's `docs/superpowers/`: `2026-08-21-origin-two-state-save-design.md` + `-implementation.md` (5 tasks), `2026-08-21-origin-lifecycle-ops-design.md` + `-implementation.md` (8 tasks) — for another agent working in the origin.
- User decisions for the origin port (differ from fork deliberately): KEEP origin autosave (localStorage) and Create-publishes-v1; typed exceptions `TemplateInactiveException`/`NoActiveVersionException` (breaking → origin 2.0.0); `SaveVersionRequest.IsActive` is **`bool?` + `?? true`** (System.Text.Json record binding does not honor positional defaults — a plain `bool` would silently draft-save for old clients); export `schemaVersion: 2` mirrors fork v2 **minus sampleData** (origin main lacks the column); import transport = multipart `IFormFile`; no audit wiring anywhere (origin has no audit log); lifecycle DI only in `AddTemplateBuilderEditor` (Core unaffected).
- Origin quirks baked into the plans: Restore uses new `GetVersionAsync(versionId)` (single-row, not history scan); `DeleteAsync` deletes versions before template (FK `NoAction` on TemplateId, `SetNull` on CurrentVersionId — no null-first step needed); bulk IDs bind natively as `List<int>`; new `GetAllIncludingInactiveAsync` needed (health/bulk see all templates); migration recipe `dotnet ef migrations add <Name> --project src/TemplateBuilder.Infrastructure` + hand-added NEWID() backfill for ExternalKey.

### 2026-08-21: Two-state save model (v1.2.0) — Draft/Active versions, no workflow

- `TemplateVersion.IsActive` semantics: true = **Active** save (served by the render API), false = **Draft** save. `SaveVersionRequest.IsActive` defaults to true, so clients that don't send the flag keep the old publish behavior (audit `published` vs `draftSaved`).
- Render API contract: `RenderAsync`/`RenderByNameAsync` throw `TemplateNotFoundException` (missing), `TemplateInactiveException(templateId)` (template `IsActive` false — "not servable"), `NoActiveVersionException(templateId)` ("no active version to serve") — and serve the **last Active** version, never the latest draft.
- Export JSON is `schemaVersion: 2`: import accepts ONLY 2 (v1 rejected with "schemaVersion … not supported"), per-version `isActive` + template `isActive` flags preserved exactly, no skip/collapse on import. The bulk-zip `_summary.json` manifest is also `schemaVersion: 2` (fixed 2026-08-21).
- Create produces NO version (the initial-version publish was removed); Duplicate and Restore inherit `IsActive` from the source (`source.CurrentVersion?.IsActive ?? true` / `source?.IsActive ?? true`, null-safe for version-less templates).
- Migrations: `AddVersionIsActive` (202608210016543, column NOT NULL default ((1))) and `SimplifyTemplateStatus` (202608210121111, drops Status/DraftBody/ReviewComment). `DropCreateDatabaseAlways` test fixtures bypass `__MigrationHistory` — validate migration chains with a `DbMigrator` probe against a scratch DB instead.
- "Save draft = version": no autosave, no DraftBody column — a draft IS a version with `IsActive=false`. Editor: `btn-save-draft` → `saveVersion(false)`, `btn-save-version` → `saveVersion(true)`; the draft badge is server-rendered from `Model.LatestVersionIsActive` and refreshed client-side from the response `data.isActive`.

### 2026-08-20: Lifecycle & Ops phase (export/import, health check, bulk ops)

- Promotion format: export JSON `schemaVersion: 1`, camelCase, includes full version history;
  `ExternalKey` is the stable cross-environment identity (unique index, NEWID backfill in
  `AddLifecycleOps`). Import matches by ExternalKey (update appends versions from max+1; new
  key creates with original numbers); Review/Approved targets are SKIPPED (never clobbered);
  exported Review/Approved status collapses to Draft. `SourceView`/`SourceViewSnapshot` are
  environment-local and deliberately NOT exported.
- Health check: token extraction walks the Scriban AST for `model.*` member chains (leaf
  filtering removes intermediate chain nodes); snapshot JSON shape is `{ takenAt, columns }`
  (from `BuildSnapshotJsonAsync` — not a bare array); `SqlViewDiscoveryService.GetViewColumnsAsync`
  returns an EMPTY list for a missing view (never throws) → `live.Count == 0` is the
  view_missing signal. Severity precedence: column_missing/view_missing Critical,
  drift findings Warning, worst = max.
- `BulkIdsRequest.Ids` must be `List<int>` (or another mutable collection), NOT `int[]` —
  MVC5's DefaultModelBinder `ReplaceCollectionImpl` calls `Clear()` on the model instance and
  `Array.Empty`-initialized arrays throw "Collection is read-only" (500 on every bulk POST).
- `Views/Templates/Index.cshtml` must NOT declare `const _csrf` inline: the package-wide
  `template-editor.js` declares it at top level, and the duplicate global declaration throws a
  SyntaxError that silently kills the ENTIRE external script on the list page (badges/bulk bar/
  import modal all stop working; symptoms = `typeof showToast === 'undefined'`). Keep the
  inline script dependency-free on _csrf.
- Modals in the editor CSS are shown with the `.open` class (`.modal-overlay` is
  `display:none` by default) — the `hidden`-attribute pattern does NOT work for overlays.
- The mono/xsp4 "session crash" (2026-08-20 logout): `dotnet test` for net48 test projects
  spawns a mono test host that can hard-crash the session (mono_crash.*.json dumps in the test
  bin dir; symptoms: "Test host process crashed", then the whole environment dies). It is
  transient — the same suite passes on re-run. If a run crashes, re-run before debugging.
- Scriban quirks verified by probe: `Template.Parse("{{ model.Name")` (unterminated) reports
  NO error (HasErrors=False); `{{ end }}`, `{{ model. }}`, `{{ x | }}` are real parse errors.
  Liquid-style `{% for %}`/`{% endif %}` parses as RAW TEXT in this fork — Scriban-native
  `{{ for ... }}`/`{{ if ... }}`/`{{ end }}` is the only block syntax. `{{ ""literal model.Nope"" }}`
  (doubled quotes) makes a string literal, excluded from token extraction.
- xsp source rebuild recipe (crashed sessions wipe /tmp/opencode): clone mono/xsp, checkout
  `72b24c0`, generate `AssemblyInfo.cs` from `.in` (XSP_VERSION → 4.6.0.0), cp
  AssemblyInfo2/4.cs.in → .cs, set `SignAssembly=false` in both csprojs, `xbuild
  src/Mono.WebServer.XSP/Mono.WebServer.XSP.csproj /p:Configuration=Debug`. Run: `MONO_PATH=$XSP_BIN
  setsid mono $XSP_BIN/Mono.WebServer.XSP.exe --applications /:<host-root> --port 8081 --nonstop`.
  Kill by PID from `ss -ltnp | grep :8081` (never pkill -f the name).
- The `&&` chain with `grep -cE " error "` is a trap: `grep -c` exits 1 when the count is 0,
  silently aborting the chain after a successful build — check the exit code separately.
- sqlcmd in container `mssql-tb` is at `/opt/mssql-tools18/bin/sqlcmd` and needs `-C` (trust
  server cert) + `-d <db>` (defaults to master otherwise).

### 2026-08-20: Audit page redesign + Activity drawer (Edit page)

- Edit-page activity timeline moved OUT of `.tb-editor-grid` (was a 4th grid child auto-placing
  into an implicit row that squeezed the 3-panel workspace). Now a right-edge slide-in drawer:
  vertical "ACTIVITY" tab (with count badge) + full-height drawer, absolutely positioned inside
  the grid (grid has `position:relative`) — grid flow can never be affected. Escape/X/tab toggles;
  timeline grouped by day with action-kind colored dots.
- Audit page (`/Audit`) fully redesigned: 5 stat chips (total/templates/snippets/actors/range),
  30-day SVG bar chart (no CDN, inline SVG from JS), filter card (search/type/action-select/
  actor/date range + Clear), color-coded action badges, per-row expandable Before/After state
  (JSON diff highlight + plain-string diff), windowed pagination, empty state, 30s live-poll pill
  ("N new — Refresh"). Server-side initial render + `GET /Audit/Stats` JSON for polling.
- New `IAuditStatsRepository`/`AuditStatsRepository` + `AuditFiltering` helper live in
  **Infrastructure.EF6** (fork-owned layer) — Domain/Application untouched (verbatim-port rule).
  EF6 forbids concurrent async ops on one DbContext; all stats queries run sequentially.
  `AuditRepository.ApplyFilters` now shares `AuditFiltering.Apply` (single source of truth).
- xsp4 restart recipe gotchas: launch with `MONO_PATH=<xsp bin dir>` AND `setsid ... < /dev/null`
  (nohup backgrounding under this harness left a process that logged "Listening" but bound no
  socket); `pkill -f Mono.WebServer.XSP` self-kills the shell because the pattern matches the
  shell's own cmdline — capture the PID from `pgrep` and `kill` that.
- Sample host and EF6 tests share the SAME database (`TemplateBuilderMvc5Tests`) — a freshly
  booted xsp4 keeps a pooled connection so `DropCreateDatabaseAlways` tests fail with "Cannot
  drop database ... in use". Stop xsp4 before running the EF6 suite, restart after.
- Rebuild-verification cycle for asset/view changes: `dotnet build` (regenerates obj/CodeGen) →
  `dotnet pack -c Release -o /tmp/opencode/nupkg-test` → delete + reinstall
  `TemplateBuilder.Editor.Mvc5.1.1.0` in `samples/.../packages` (nuget.exe, local source) →
  `xbuild` sample host → restart xsp4. First request after a fresh boot can 500 (EF migration
  init race) — retry once before debugging.
- `agent-browser` (v0.34.0, global CLI) is the web-verification tool; use `agent-browser set
  viewport W H` (not `resize`), `eval` with single-quoted JSON.stringify wrapped in an IIFE
  (bash eats `$(` in double-quoted scripts), `snapshot -i` for a11y-tree refs. Screenshots go to
  /tmp/opencode/*.png (this model can't read images — verify layout via computed-style eval).

### 2026-08-20: Memory layer installed

- Project-scoped memory layer installed: `MEMORY.md` (this file) + the `memory` skill in `.opencode/skills/memory/`. Chosen over MCP/vector options because the user wanted zero external dependencies and project-only scope.
- The repo builds with dotnet/xsp4 tooling on Linux.
