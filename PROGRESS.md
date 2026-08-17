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
| 1 | `dotnet build TemplateBuilder.Mvc5.sln` | _pending_ | |
| 2 | `dotnet test tests/TemplateBuilder.Domain.Tests/` | _pending_ | |
| 3 | `dotnet test tests/TemplateBuilder.Application.Tests/` | _pending_ | |
| 4 | `dotnet build src/TemplateBuilder.Infrastructure.EF6/` + table check | _pending_ | |
| 5 | `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/` | _pending_ | |
| 6 | `dotnet build src/TemplateBuilder.Editor.Mvc5/` | _pending_ | |
| 7 | `dotnet build src/TemplateBuilder.Editor.Mvc5/` | _pending_ | |
| 8 | `dotnet build` + grep 12 routes | _pending_ | |
| 9 | `dotnet build` + grep 3 snippet routes | _pending_ | |
| 10 | `dotnet build` | _pending_ | |
| 11 | `/Spike/Hello` HTTP 200 + marker text | _pending_ | |
| 12 | build + `/Templates`, `/Templates/Create` HTTP 200 | _pending_ | |
| 13 | static CSS/JS HTTP 200 + content-type | _pending_ | |
| 14 | IIS Express + end-to-end flow | _pending_ | |
| 15 | `dotnet pack` + nupkg extraction | _pending_ | |