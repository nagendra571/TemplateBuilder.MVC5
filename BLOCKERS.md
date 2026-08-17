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

## 6. No IIS Express on Linux — hosting gates (Tasks 11/14) cannot run their exact commands

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