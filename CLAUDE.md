# TemplateBuilder.Editor.Mvc5

## What this is

A NuGet package (`TemplateBuilder.Editor.Mvc5`) that gives ASP.NET MVC 5 / .NET Framework 4.8 consumers the same template management UI (create/edit, version history, compare, live preview, restore, reusable snippets, configurable authorization) that `TemplateBuilder.Editor` gives ASP.NET Core (net8.0/net10.0) consumers.

**This is a standalone fork, not a shared codebase.** `Domain` and `Application` were duplicated (copied verbatim) from the origin `TemplateBuilder` repo rather than shared via a multi-targeted package — that was a deliberate tradeoff (see the design spec's "Decision" section) to keep this repo's unfamiliar toolchain (`packages.config`, RazorGenerator, Unity, non-SDK-style web hosting) fully isolated from the actively-published origin repo. **Do not assume changes here propagate to `TemplateBuilder`, or vice versa.** If you fix a bug in `Domain`/`Application` logic here, check whether the same bug exists in the origin repo and needs a separate fix there.

## Read these first

- `docs/superpowers/specs/2026-08-16-net48-mvc5-editor-design.md` — the design spec: why this exists, what was rejected (sidecar hosting, shared code), the client's actual environment (MVC5, EF6, Unity, OWIN/claims auth, Bootstrap 3.3.7), and the architecture.
- `docs/superpowers/plans/2026-08-16-net48-mvc5-editor-implementation.md` — the task-by-task implementation plan. If this repo is freshly created from the handoff bundle, **this plan has not been executed yet** — start here.

## Architecture at a glance

```
src/
├── TemplateBuilder.Domain/               net48 — entities/interfaces, ported verbatim, no logic
├── TemplateBuilder.Application/          net48 — Scriban engine, sanitizer, SQL discovery, ported verbatim
├── TemplateBuilder.Infrastructure.EF6/   net48 — EF6 DbContext, Code-First migrations, repositories (new)
└── TemplateBuilder.Editor.Mvc5/          net48 — MVC5 controllers/views/RazorGenerator/Unity registration (new)
tests/        mirrors src/, xunit
samples/TemplateBuilder.SampleMvc5Host/   real ASP.NET MVC5 Web Application project (old-style csproj,
                                           IIS-Express-hostable) — the only non-SDK-style project in the repo,
                                           because System.Web.Mvc requires IIS/System.Web hosting
```

## Non-negotiable constraints (see spec for full rationale)

- **Unity namespace is `Unity`**, not `Microsoft.Practices.Unity` — the client is on the modern Unity 5.x line.
- **EF6, not EF Core** — EF Core dropped .NET Framework support after 3.1. `System.Data.Entity.Infrastructure.DbUpdateConcurrencyException` is a different type from EF Core's equivalent; don't mix up namespaces when porting exception handling.
- **`Domain`/`Application` are verbatim ports** — if you need to change their behavior, treat it as a deliberate fork decision, not a routine edit. Document why in the commit message.
- **RazorGenerator.Mvc precompiles views into the DLL** — consumers never see `.cshtml` files. This is the least-precedented piece of the whole design; if you're touching the view-compilation pipeline, re-read the plan's Task 11 (the RazorGenerator spike) before changing it.
- **Package ID is `TemplateBuilder.Editor.Mvc5`**, deliberately distinct from `TemplateBuilder.Editor` (different implementation, would be a support/versioning hazard under the same ID).

## Conventions carried over from the origin repo

- xunit, `FluentAssertions`, one test project per `src/` project, mirroring names (`X.Tests`).
- Conventional commit-style messages (`feat:`, `fix:`, `chore:`).
- Before claiming a build/test/pack "works," actually run it and show the output — don't assert success you didn't verify. This bit the origin repo once already (a NuGet package README fix that was made locally but never repacked, so `nuget.org` still served the stale version) — extract and inspect the actual `.nupkg` contents before handing off a publish command, every time.
- Only commit when explicitly asked. Never push/publish to NuGet without explicit confirmation — it's an external, effectively-irreversible action (see spec's packaging section on why `1.5.1`→`1.5.2`-style re-publishes happened in the origin repo).

## Known open risks (from the spec — verify these, don't assume they're resolved)

1. **RESOLVED (2026-08-17, Tasks 11–14):** RazorGenerator precompiled-views pipeline — validated
   end-to-end: spike view plus all 5 real views render on xsp4 with no physical `.cshtml` files.
   Two things learned: the Linux/Core-MSBuild codegen fallback lives in `eng/RazorGenDriver.cs`
   (BLOCKERS #10 — `obj/CodeGen` must be regenerated when views change), and RazorGenerator views
   DO honor a consumer's `Views/_ViewStart.cshtml` + `_Layout.cshtml` (used by the sample host to
   wire the editor CSS/JS, mirroring the origin RCL `_content` consumer pattern).
2. Assembly binding redirects on the client's actual `packages.config` solution — `tools/install.ps1`
   (Task 15) ships guidance (Newtonsoft.Json 13, EntityFramework 6.5.1), not a fully automated fix;
   must be validated against the real client solution before calling this "done."
3. Bootstrap 3.3.7 / jQuery 3.7.1 / IgniteUI CSS collision — the editor's `#tb-editor-host` CSS
   scoping is designed to prevent this but has never been tested against a real Bootstrap v3 host
   page. The sample host doesn't load Bootstrap, so this specifically needs checking against the
   real client app.
4. Two package-level behaviors were introduced while proving Task 14 that the client WILL hit on
   Windows too (they're not mono-specific): MVC5 has no header-based anti-forgery (stock
   `[ValidateAntiForgeryToken]` is form-only), so JSON endpoints use the package's
   `ValidateJsonAntiForgeryTokenAttribute` — the editor JS must send the `RequestVerificationToken`
   header (it does); and `TemplateBuilderControllerBase.OnActionExecuting` deliberately excludes the
   Form value provider for `application/json` requests (necessary under mono — see BLOCKERS #13/#14).
