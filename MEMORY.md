# MEMORY

Persistent cross-session memory for agents working in this repo. Maintained via the `memory` skill in `.opencode/skills/memory/`. Append newest entries first; never delete history.

## Memories

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
