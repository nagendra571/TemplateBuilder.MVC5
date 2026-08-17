# Success Criteria — TemplateBuilder.Editor.Mvc5

Feed this alongside `CLAUDE.md`, `docs/superpowers/specs/2026-08-16-net48-mvc5-editor-design.md`, and `docs/superpowers/plans/2026-08-16-net48-mvc5-editor-implementation.md`. Those documents are the *what* and *how*; this document is the *how do I know I'm done* — every gate below is something you run and observe, not something you judge subjectively. Work through the plan's 15 tasks in order; treat each gate here as a checkpoint you must pass before moving to the next task, not a final exam at the end.

## Hard rules (never violate these while looping)

1. **Never claim a gate passed without running its exact command and reading the actual output.** "This should work" is not evidence. Paste/log the real command output before checking a box.
2. **Never publish to NuGet, push to a remote git repo, or touch anything outside this working directory.** Packing (`dotnet pack`) is fine; pushing (`dotnet nuget push`) is not — that's a human decision, every time.
3. **Never modify `Domain`/`Application` source to "make something easier."** They're verbatim ports (see `CLAUDE.md`) — if a dependency genuinely won't compile on `net48`, stop and report it; don't quietly change the port to fix a build error, since that's evidence the spec's compatibility claim was wrong and needs a human to re-decide, not a workaround.
4. **Circuit breaker: if the same gate fails 3 times in a row with the same root cause, stop looping on it.** Write a short diagnosis (what you tried, what error, your best hypothesis) and move to a different task if one is unblocked, or stop entirely if everything is downstream of the failure. Grinding a 4th, 5th, 6th identical attempt burns budget without new information.
5. **Commit after every task**, per the plan's own commit steps — small, reviewable commits, not one giant commit at the end. If you have to stop mid-loop, the git history should show exactly how far you got.
6. **Every task's steps in the plan already contain real, complete code** — copy it faithfully rather than re-deriving it from scratch, except where the plan explicitly says "port from origin" or "adapt" (view conversion in Task 12, EF6 migration in Task 4 — see below).

## Headless-environment adaptations (the plan assumes a human at Visual Studio for two steps — you don't have that)

### Task 4, Step 4 — generating the EF6 migration without Package Manager Console

`Add-Migration` is a Visual Studio PMC command with no standalone CLI equivalent for scaffolding. Instead, **hand-write** `Migrations/Configuration.cs` and `Migrations/InitialCreate.cs` directly, matching the `OnModelCreating` configuration from Task 4 Step 1:

```csharp
// Migrations/Configuration.cs
namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System.Data.Entity.Migrations;

    internal sealed class Configuration : DbMigrationsConfiguration<Data.TemplateBuilderDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
            MigrationsDirectory = @"Migrations";
        }
    }
}
```

```csharp
// Migrations/InitialCreate.cs
namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class InitialCreate : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Templates",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    Name = c.String(nullable: false, maxLength: 200),
                    TemplateType = c.String(nullable: false, maxLength: 50),
                    Description = c.String(maxLength: 500),
                    CurrentVersionId = c.Int(),
                    IsActive = c.Boolean(nullable: false),
                    CreatedAt = c.DateTime(nullable: false),
                    UpdatedAt = c.DateTime(nullable: false),
                    RowVersion = c.Binary(nullable: false, fixedLength: true, timestamp: true, storeType: "rowversion"),
                })
                .PrimaryKey(t => t.Id)
                .Index(t => t.Name, unique: true);

            CreateTable(
                "dbo.TemplateVersions",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    TemplateId = c.Int(nullable: false),
                    VersionNumber = c.Int(nullable: false),
                    Body = c.String(nullable: false),
                    ChangeComment = c.String(maxLength: 500),
                    CreatedAt = c.DateTime(nullable: false),
                    CreatedBy = c.String(maxLength: 200),
                })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Templates", t => t.TemplateId)
                .Index(t => t.TemplateId);

            CreateTable(
                "dbo.Snippets",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    Name = c.String(nullable: false, maxLength: 200),
                    Description = c.String(maxLength: 500),
                    Body = c.String(nullable: false),
                    CreatedAt = c.DateTime(nullable: false),
                    UpdatedAt = c.DateTime(nullable: false),
                })
                .PrimaryKey(t => t.Id)
                .Index(t => t.Name, unique: true);

            AddForeignKey("dbo.Templates", "CurrentVersionId", "dbo.TemplateVersions", "Id");
            CreateIndex("dbo.Templates", "CurrentVersionId");
        }

        public override void Down()
        {
            DropForeignKey("dbo.Templates", "CurrentVersionId", "dbo.TemplateVersions");
            DropForeignKey("dbo.TemplateVersions", "TemplateId", "dbo.Templates");
            DropIndex("dbo.TemplateVersions", new[] { "TemplateId" });
            DropIndex("dbo.Templates", new[] { "CurrentVersionId" });
            DropIndex("dbo.Snippets", new[] { "Name" });
            DropIndex("dbo.Templates", new[] { "Name" });
            DropTable("dbo.Snippets");
            DropTable("dbo.TemplateVersions");
            DropTable("dbo.Templates");
        }
    }
}
```

Gate: `dotnet build src/TemplateBuilder.Infrastructure.EF6/` succeeds with these files present, and a LocalDB database created via `Database.SetInitializer` + first `SaveChangesAsync` call in the Task 5 tests produces exactly these three tables (verify with `sqlcmd` or `Invoke-Sqlcmd -Query "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES" -Database TemplateBuilderMvc5Tests` — expect `Templates`, `TemplateVersions`, `Snippets`, nothing else, plus EF6's own `__MigrationHistory` table).

### Task 11 Step 5 / Task 14 Step 7 — running the sample host without Visual Studio's F5

Use IIS Express directly from the command line instead of Visual Studio:

```powershell
& "C:\Program Files\IIS Express\iisexpress.exe" /path:"<full-path-to>\samples\TemplateBuilder.SampleMvc5Host" /port:8081
```

Then verify with HTTP calls instead of eyeballing a browser:

```powershell
Invoke-WebRequest http://localhost:8081/Spike/Hello -UseBasicParsing        # Task 11 gate
Invoke-WebRequest http://localhost:8081/Templates -UseBasicParsing          # Task 14 gate
Invoke-WebRequest http://localhost:8081/TemplateBuilderEditor/css/template-editor.css -UseBasicParsing   # static asset gate
Invoke-WebRequest http://localhost:8081/Templates/_setup -UseBasicParsing   # diagnostic page gate
```
Expect `StatusCode 200` and, for the CSS/JS routes, a body that isn't empty and a `Content-Type` matching what Task 13's route handler sets. For `/Templates` and `/Templates/_setup`, grep the response body for expected markers (e.g. `<h1>` text, or a known CSS class from the ported view) rather than just checking the status code — a 200 with an empty or error-page body is not a pass.

If `iisexpress.exe` isn't found at that path, search `C:\Program Files (x86)\IIS Express\` too; if IIS Express isn't installed at all, that's a genuine environment gap — report it, don't skip the gate.

## Definition of Done — gate checklist

Work top to bottom. Each row: the command that proves it, and the exact pass condition.

| # | Task | Command | Pass condition |
|---|---|---|---|
| 1 | Scaffold (Task 1) | `dotnet build TemplateBuilder.Mvc5.sln` | `Build succeeded.`, `0 Error(s)`, all 4 `src/` + 3 `tests/` projects present in the `.sln` |
| 2 | Domain port (Task 2) | `dotnet test tests/TemplateBuilder.Domain.Tests/` | All tests pass; diff `src/TemplateBuilder.Domain/` against the origin path in `CLAUDE.md` line-by-line — zero unintended differences beyond the `.csproj` |
| 3 | Application port (Task 3) | `dotnet test tests/TemplateBuilder.Application.Tests/` | All tests pass (expect the same count as the origin repo's suite) |
| 4 | EF6 data model (Task 4) | `dotnet build src/TemplateBuilder.Infrastructure.EF6/` + LocalDB table check (above) | Build succeeds; exactly 3 app tables + `__MigrationHistory` created |
| 5 | EF6 repositories (Task 5) | `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/` | All tests pass against a real LocalDB instance (not mocked) |
| 6 | Options/auth/Unity registration (Task 6) | `dotnet build src/TemplateBuilder.Editor.Mvc5/` | Build succeeds |
| 7 | JSON helper + base controller (Task 7) | `dotnet build src/TemplateBuilder.Editor.Mvc5/` | Build succeeds |
| 8 | TemplatesController (Task 8) | `dotnet build src/TemplateBuilder.Editor.Mvc5/` | Build succeeds; every route from the design spec's route table exists as a `[Route(...)]` attribute on this controller — grep and confirm all 12 |
| 9 | SnippetsController (Task 9) | `dotnet build` + grep | All 3 snippet routes present |
| 10 | SetupController (Task 10) | `dotnet build` | Build succeeds |
| 11 | RazorGenerator spike (Task 11) | `Invoke-WebRequest http://localhost:8081/Spike/Hello` | 200, body contains "RazorGenerator works" — **do not proceed to Task 12 until this specific gate passes**; it's the highest-risk piece in the whole plan |
| 12 | Real views ported (Task 12) | `dotnet build` + `Invoke-WebRequest` against `/Templates`, `/Templates/Create` | Build succeeds; both routes return 200 with non-empty, non-error-page bodies |
| 13 | Static assets + routing (Task 13) | `Invoke-WebRequest .../TemplateBuilderEditor/css/template-editor.css` and `.../js/template-editor.js` | Both 200, `Content-Type` correct, body matches the byte count of the original file in the origin repo (`Get-Item` both, compare `Length`) |
| 14 | Sample host end-to-end (Task 14) | IIS Express + scripted HTTP flow (below) | See "End-to-end flow script" |
| 15 | Packaging (Task 15) | `dotnet pack src/TemplateBuilder.Editor.Mvc5/ -c Release -o ./nupkg` then extract | `.nupkg` exists; `lib/net48/` contains exactly `TemplateBuilder.Editor.Mvc5.dll`, `TemplateBuilder.Domain.dll`, `TemplateBuilder.Application.dll`, `TemplateBuilder.Infrastructure.EF6.dll`; `tools/install.ps1` and root `README.md` present in the extracted package |

### End-to-end flow script (Task 14 gate — the real proof this works, not just "it compiles")

Since there's no browser to click through, script the HTTP flow and assert on responses:

1. `POST /Templates/Create` with form-encoded `Name`, `TemplateType`, `Body` → expect `302` redirect to `/Templates/{id}/Edit`.
2. Extract `{id}` from the `Location` header. `GET /Templates/{id}/Edit` → `200`, body contains the template name.
3. `POST /Templates/{id}/SaveVersion` with a JSON body (`Name`, `TemplateType`, `Body`, matching `SaveVersionRequest`) and the antiforgery token from step 2's page → `200`, JSON body contains `versionNumber: 2`.
4. `GET /Templates/{id}/Versions` → `200`, body lists both versions.
5. `GET /Templates/_setup` → `200`, all checks show `Passed: true` (grep for `Passed`/`False` in the rendered HTML — if the view renders check names/status as text, confirm no `False`/`failed` marker appears for the DB or routing checks).

If antiforgery tokens make scripting painful without a real browser, note that as a known limitation in your final report rather than skipping the flow test entirely — at minimum, prove steps 1, 2, 4, and 5 (the GET-only parts) pass via HTTP, and describe (don't fabricate) whether the POST flow was verified.

## Escalate to a human instead of continuing to loop, when:

- A dependency's actual net48 compatibility contradicts the spec's compatibility table (see Hard Rule 3).
- `RazorGenerator.MsBuild`'s SDK-style integration doesn't work as Task 11 assumes, after genuinely trying the package's own current documentation (not guessing at flags).
- IIS Express isn't available in this environment at all, and no alternative headless ASP.NET-hosting method is obviously correct.
- Anything in "Hard rules" #2 would otherwise be required to make a gate pass (e.g., a gate seems to need a real NuGet publish, or pushing to a remote) — that means the gate or the plan needs re-scoping, not that the action should happen anyway.
- You've hit the circuit breaker (Hard Rule 4) on a gate that blocks all remaining tasks.

## Final report format

When every row in the Definition of Done table is checked, produce a short report: each gate, the command run, and the actual observed output (not a restatement of "pass conditions" — the real output). If any gate is not met, say so plainly and explain what's blocking it — a partial, honestly-reported result is more useful than a claimed full pass that didn't happen.
