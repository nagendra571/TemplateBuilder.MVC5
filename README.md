# TemplateBuilder.Editor.Mvc5

A NuGet package bringing `TemplateBuilder.Editor`'s full template management UI (create/edit, version history, compare, live preview, restore, reusable snippets, configurable authorization) to **ASP.NET MVC 5 / .NET Framework 4.8** projects.

This is a companion project to [`TemplateBuilder`](https://www.nuget.org/packages/TemplateBuilder.Editor) (ASP.NET Core, net8.0/net10.0) — built as a standalone fork rather than a shared codebase, so its toolchain (`packages.config`, RazorGenerator, Unity, EF6) stays fully isolated from the actively-published origin project.

**Status:** design complete, implementation not yet started. See `docs/superpowers/plans/2026-08-16-net48-mvc5-editor-implementation.md` for the task-by-task build plan.

## Structure

```
src/
├── TemplateBuilder.Domain/               entities/interfaces
├── TemplateBuilder.Application/          Scriban rendering, HTML sanitization, SQL view discovery
├── TemplateBuilder.Infrastructure.EF6/   EF6 data access
└── TemplateBuilder.Editor.Mvc5/          MVC5 controllers, precompiled Razor views, Unity DI registration
tests/                                    mirrors src/
samples/TemplateBuilder.SampleMvc5Host/   local dev/test host (real IIS-Express-hostable MVC5 app)
docs/superpowers/specs/                   design spec
docs/superpowers/plans/                   implementation plan
```

## Getting started

1. Read `CLAUDE.md` for architecture/conventions.
2. Read `docs/superpowers/specs/2026-08-16-net48-mvc5-editor-design.md` for the full design rationale.
3. Execute `docs/superpowers/plans/2026-08-16-net48-mvc5-editor-implementation.md` task-by-task (via the `superpowers:subagent-driven-development` or `superpowers:executing-plans` skill).

## Requirements

- .NET Framework 4.8 (target)
- .NET SDK (for `dotnet build`/`test` against the SDK-style `src/`/`tests/` projects)
- Visual Studio (for the EF6 Package Manager Console migration commands, and for building/running `samples/TemplateBuilder.SampleMvc5Host`, which is an old-style MVC5 Web Application project — not buildable via plain `dotnet build`)
- SQL Server / LocalDB (for `Infrastructure.EF6.Tests` and the sample host)

## Customizing the author identity

Consumers supply their own user identity (claims, user id, etc.) via
`options.ActorResolver` in `RegisterTemplateBuilderEditor` — see the package README's
"Author Identity (CreatedBy)" section. Values flow to `TemplateVersion.CreatedBy`,
`SnippetVersion.CreatedBy`, snippet usage, and audit rows; the fallback chain is
resolver → `User.Identity.Name` → `"anonymous"`.
