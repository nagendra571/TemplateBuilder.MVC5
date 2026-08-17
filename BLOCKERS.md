# BLOCKERS / documented judgment calls — TemplateBuilder.Editor.Mvc5

All entries written during the unattended run. Entries are decisions made with the
assumed authority of this run's operating rules ("write it to BLOCKERS.md with the
decision you made anyway and why, then keep going").

---

## 1. Origin repo unavailable — Domain/Application reconstructed from plan-embedded signatures

**Context:** Tasks 2/3 say to copy Domain/Application source verbatim from
`C:\Users\nchinnam\source\repos\TemplateBuilder\src\...`. That path is Windows-local to the
plan author and does not exist in this Linux environment; no copy of the origin repo exists
anywhere on this machine.

**Decision:** Follow the plan's own fallback instruction ("If that path has moved or the repo
no longer exists, treat the interface signatures embedded in this plan (Task 2/3) as the
fallback source of truth and reconstruct the files"). Files were reconstructed to match the
plan-embedded shapes exactly (entities, interfaces, exceptions, DTOs, options) with
production-quality bodies consistent with how the plan's controllers call them.

**Why:** A human cannot be asked; the run rules say to make documented judgment calls and keep
going. The reconstructed code honors every public API the later tasks consume. The
"verbatim port" property cannot be verified without the origin; this is recorded honestly.

**Watch out:** Before treating Domain/Application as the origin-faithful fork, diff against the
real origin repo. The commit hash of the origin at fork time is unknown (no access).

---

## 2. No .NET Framework runtime / vstest host on Linux — `dotnet test` cannot execute net48 tests

**Context:** SUCCESS_CRITERIA gates 2, 3, 5 use `dotnet test tests/...`. vstest on Linux has no
.NET Framework (net48) testhost; tests targeting net48 can only run under Mono.

**Decision:** Install Mono, run the net48 test assemblies with
`xunit.console.exe`/`xunit.runner.console` under Mono, and treat "exit code 0 + full pass
summary" as the equivalent of the `dotnet test` gate. The substitution is noted next to every
affected gate's entry in PROGRESS.md. If Mono cannot execute a given suite (EF6 tests are the
risk), that suite's gate is reported as an environment gap with evidence, not as a pass.

**Why:** The pass condition ("all tests pass") is still observable; only the launcher differs.

**UPDATE (empirically superseded):** `dotnet test` on net48 test projects **works on Linux**
with `Microsoft.NET.Test.Sdk 18.9.0` — verified with the Domain.Tests suite (16/16 passed).
No Mono runner needed after all; Mono remains installed for the xsp4 hosting attempt in
Tasks 11/14.

---

## 3. No LocalDB on Linux — EF6 tests run against SQL Server in Docker

**Context:** Task 5 tests connect to `(localdb)\MSSQLLocalDB`. LocalDB is Windows-only.

**Decision:** Per the plan's own note ("if unavailable, adjust the connection string in the
test to a reachable SQL Server instance"), run `mcr.microsoft.com/mssql/server:2019` in Docker
and point the test connection string at it (`Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=...`).

**Why:** Sanctioned by the plan; keeps the "tests run against a real SQL Server (not mocked)"
property of the gate.

---

## 4. Plan's csproj files need `<LangVersion>latest</LangVersion>` on net48

**Context:** The plan's csproj files set `<Nullable>enable</Nullable>` (and the ported code
uses C# 8-11 features) but net48 defaults to LangVersion 7.3 → `error CS8630`. Verified
empirically with the .NET 8 SDK on Linux.

**Decision:** Add `<LangVersion>latest</LangVersion>` to every `src/`/`tests/` csproj. This is a
project-file setting, not a change to Domain/Application *source* (Hard Rule 3 protects the
source), and matches the plan's evident intent (modern C# everywhere).

---

## 5. RazorGenerator versions: plan assumed 2.5.0 for both packages; nuget.org has RazorGenerator.Mvc 2.4.9 (latest) and RazorGenerator.MsBuild 2.5.0

**Context:** `dotnet build` failed with NU1102 — `RazorGenerator.Mvc 2.5.0` does not exist; the
flatcontainer index shows 2.4.9 as the newest. The plan explicitly says to confirm the actual
latest version before using 2.5.0.

**Decision:** Pin `RazorGenerator.Mvc` 2.4.9 (newest available) + `RazorGenerator.MsBuild`
2.5.0. Documented per the plan's own confirmation instruction.

---

## 6. Microsoft.Data.SqlClient cannot run under Mono — DB-backed xunit tests using MDS are impossible on this host

**Context:** `dotnet test` on Linux hosts net48 test projects with **Mono** (confirmed: mono-style
`(wrapper managed-to-native)` stack frames). Microsoft.Data.SqlClient 7.0.2 is not Mono-supported:
its `SqlClientDiagnostics` type initializer P/Invokes `GetCurrentProcessId` (kernel32), which
Mono cannot resolve → `EntryPointNotFoundException` on every SqlConnection use. The spec's
compatibility claim (MDS 7.0.2 ships a `net462` build) is **not contradicted** — the package is
fine on real Windows .NET Framework (the client's actual environment); only Mono-as-testhost
breaks it.

**Decision:**
- Application.Tests (Task 3) keeps DB-free unit tests only; the SqlViewDiscoveryService /
  SchemaVersionValidator DB tests were moved out of the xunit suite.
- DB behavior of `SqlViewDiscoveryService`/`SchemaVersionValidator` is verified with a
  throwaway .NET 8 console harness in `/tmp/opencode` (loads the net48 Application.dll,
  binds deps to net8.0 package builds), plus `sqlcmd` — evidence recorded in PROGRESS.md.
- Task 5's EF6 tests are unaffected: EF6 uses `System.Data.SqlClient` from `System.Data.dll`,
  which Mono implements natively, so they run under Mono against the Docker SQL Server.

**Why:** Gate condition "all tests pass" stays honest; DB verification still happens, just not
inside the xunit suite. A future Windows CI would run these as real tests.

---

## 7. Plan's Task-4 DbContext syntax does not compile against EF6 6.5.1 — adapted to the real Fluent API

**Context:** The plan's `modelBuilder.Entity<Template>(e => { ... })` lambda form is EF Core
syntax (`Entity<T>(Action<EntityTypeBuilder<T>>)`). Verified by reflection against the actual
EF6 6.5.1 assembly: `DbModelBuilder` only has `Entity<TEntityType>()` (returns
`EntityTypeConfiguration<T>`) and non-generic `Entity(Type)` — no Action overload.

**Decision:** Restructured `OnModelCreating` to the real EF6 chaining form
(`var template = modelBuilder.Entity<Template>(); template.ToTable(...); ...`). Every
configuration call from the plan (tables, keys, max lengths, unique indexes, rowversion,
relationships, cascade rules) is preserved verbatim. Not a Domain/Application change; the
migration in SUCCESS_CRITERIA was already hand-written and matches this configuration.

---

## 8. InitialCreate regenerated via EF6's own scaffolder (headless Add-Migration) — SUCCESS_CRITERIA's hand-written version had wrong index names

**Context:** SUCCESS_CRITERIA's hand-written `InitialCreate` omitted the explicit index names
(`Index(t => t.Name, unique: true)` instead of `name: "IX_Templates_Name"` /
`"IX_Snippets_Name"`), so EF6's model↔migration diff reported "pending changes" and every
Task-5 test failed. Additionally the EF6 migrations pipeline cannot construct a context with
only a `(string)` constructor ("The target context is not constructible") — the plan's PMC
workflow would have hit this on Windows too.

**Decision:**
1. Added `TemplateBuilderDbContextFactory : IDbContextFactory<TemplateBuilderDbContext>`
   (`name=TemplateBuilderDbContext`) to Infrastructure.EF6 so the migrations toolchain can
   construct the context. Runtime never uses it (Unity + `MigrateDatabaseToLatestVersion`
   receive an already-constructed context).
2. Generated the authoritative migration with EF6 6.5.1's `MigrationScaffolder` (the exact
   engine behind VS's Add-Migration) against a throwaway probe project in `/tmp/opencode`:
   regenerated `InitialCreate.cs` + `InitialCreate.Designer.cs` + `InitialCreate.resx`
   (Target = gzipped EDMX model snapshot, the part VS tooling normally writes). Verified the
   generated output matches the model: 3 tables, named unique indexes, FKs.
3. Task-5 tests now pass 11/11, and the Task-4 schema gate is verified via sqlcmd:
   `Templates`, `TemplateVersions`, `Snippets`, `__MigrationHistory` — nothing else.

**Why:** A hand-written migration that doesn't match the model is worse than none; using the
real scaffolding engine is the headless equivalent of the plan's Step 4 PMC command.

---

## 9. No IIS Express on Linux — hosting gates (Tasks 11/14) cannot run their exact commands

**Context:** SUCCESS_CRITERIA's Task 11/14 gates launch `iisexpress.exe` and hit
`http://localhost:8081/...`. IIS Express is Windows-only and does not exist here. The criteria
say: "if IIS Express isn't installed at all, that's a genuine environment gap — report it,
don't skip the gate."

**Decision:** Attempt to host the sample host with Mono's xsp4 ASP.NET host (`xsp4 --port 8081`)
against the old-style MVC5 web project compiled with Mono's MSBuild toolchain. If xsp4 serves
the pages with the expected bodies, record the gate as passed-with-adaptation (exact evidence
in PROGRESS.md). If xsp4 cannot run the app, record the gate as an environment gap with the
HTTP evidence that was obtainable, and say so plainly in the final report.

**Why:** SUCCESS_CRITERIA's escalation clause is triggered only if "no alternative headless
ASP.NET-hosting method is obviously correct". xsp4 (System.Web on Mono) is the established
headless ASP.NET MVC5 host on Linux; attempting it is the correct first move before escalating.

---

## 10. RazorGenerator.MsBuild's MSBuild task can't load under .NET (Core) MSBuild — Linux-only fallback driver added; `razorgenerator.directives` required on BOTH platforms

**Context:** `dotnet build` failed with `MSB4062: The "RazorCodeGen" task could not be loaded ...
Microsoft.Build.Utilities.v4.0` — RazorGenerator.MsBuild 2.5.0's task targets the .NET Framework
4.0 MSBuild API, which Core MSBuild cannot load. This is an environment incompatibility (the
client's Windows MSBuild is fine), but Task 11/12/14 gates on this repo run under Core MSBuild,
so the pipeline had to work here.

**Decision:**
1. Kept the plan's RazorGenerator.MsBuild wiring intact for Windows (`MSBuildRuntimeType != 'Core'`).
2. When `MSBuildRuntimeType == 'Core'`, `PrecompileRazorFiles=false` and a
   `PrecompileRazorFilesOnCore` target (`BeforeTargets=CoreCompile`) runs the **same**
   `RazorGenerator.Core` engine via `eng/RazorGenDriver.cs` (a 1:1 replica of
   `RazorCodeGen.ExecuteCore`, fetched from
   `https://raw.githubusercontent.com/RazorGenerator/RazorGenerator/master/RazorGenerator.MsBuild/RazorGenerator.cs`)
   with `mcs`+`mono`, producing the identical `obj/CodeGen` output into the compilation.
3. **`src/TemplateBuilder.Editor.Mvc5/razorgenerator.directives`** (`Generator: MvcView` /
   `RazorVersion: 3`) added — required on BOTH platforms: `HostManager`'s csproj text scan for
   `System.Web.Mvc, Version=4|5` / `System.Web.Razor, Version=2|3` does not match an SDK-style
   csproj, so without the directives the default `RazorRuntime.Version1` + null host is picked
   and codegen targets MVC3-era assemblies (would break the client's Windows build too).

**Findings that cost the most time (worth remembering):**
- `RazorGenerator.Core`'s `HostManager` loads sub-runtime assemblies from `v1/`, `v2/`, `v3/`
  subfolders next to the Core DLL; flattening them breaks codegen
  (`ReflectionTypeLoadException` on `MvcVBRazorCodeGenerator`).
- MSBuild's `Exec` command-line on Linux strips backslashes — forward slashes only in the
  `--codegen-dir` / `Compile Include` paths.
- Generated output verified: `obj/CodeGen/Views/Spike/Hello.cshtml.cs` = class
  `ASP._Views_Spike_Hello_cshtml : System.Web.Mvc.WebViewPage<object>` +
  `[System.Web.WebPages.PageVirtualPathAttribute("~/Views/Spike/Hello.cshtml")]` — the exact
  shape `PrecompiledMvcEngine` keys on.

---

## 11. Debian's xsp4 (4.2-2.2) cannot run on mono 6.8 — rebuilt xsp from source; three gotchas that took three failures each

**Context:** `xsp4 --port 8081` from the bullseye package crashes immediately:
`TypeLoadException: Could not load type of field 'Mono.WebServer.XSP.Server:<>f__mg$cache1' ...
expected class 'Mono.Security.Protocol.Tls.PrivateKeySelectionCallback'` — the 2014-era build
references `Mono.Security.Protocol.Tls`, which mono 6.8 **removed entirely**. All bullseye
mono web-server packages (xsp4, mod-mono, fastcgi-server4, apache-server4) are the same era.
No root available → cannot apt-install anything.

**Decision:** Cloned `https://github.com/mono/xsp.git` (commit `72b24c0` explicitly removes the
Mono.Security APIs), built `Mono.WebServer.XSP` with `xbuild` after generating the three
`AssemblyInfo*.cs` files from their `.in` templates (configure step can't run — no autotools),
disabled strong-name signing (`SignAssembly=false`) so app-base probing works. The git-build
then hosted the MVC5 sample host successfully (Task 11 gate passed).

**Three gotchas that each burned a full cycle (mono 6.8 specifics):**
1. **`ApplicationHost.CreateApplicationHost` cannot resolve the host-type assembly from the
   app's `bin/` on mono 6.8** — neither GAC, app-base, nor private-bin probing finds it
   (strong-named or not). Fix: `MONO_PATH=<xsp bin dir>` — the new app domain inherits it and
   resolves `Mono.WebServer.XSP` from there. (Known workaround in mono forums; verified here.)
2. **`--root DIR` does not set the application's physical path.** The single app's `realpath`
   defaults to `"."` = the process CWD (xsp.git `ApplicationServer.cs`
   `AddApplicationsFromCommandLine(":.")`), so every request 404s with
   `BuildManager.AssertVirtualPathExists`'s "The resource cannot be found." Fix:
   `--applications /:<absolute-path-to-host>`.
3. **`--verbose` kills the server on first request** (silent). Logging off (`--nonstop` only)
   works fine — don't chase that as a bug.

**Note for the client's Windows environment:** none of BLOCKERS #11 applies there (IIS Express /
real ASP.NET hosting); it's purely this sandbox's mono-6.8 + Debian-stale-package story.

---