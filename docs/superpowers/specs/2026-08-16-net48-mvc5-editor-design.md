# Design: TemplateBuilder.Editor.Mvc5 — .NET Framework 4.8 / MVC 5 Port

**Date:** 2026-08-16
**Status:** Approved (design phase) — targets a new, separate repository
**Origin repo:** `TemplateBuilder` (this document was authored there and is meant to be copied into the new repo)

---

## Goal

Give a client running **ASP.NET MVC 5 on .NET Framework 4.8** a self-contained NuGet package — `TemplateBuilder.Editor.Mvc5` — that reproduces full feature parity with the existing `TemplateBuilder.Editor` (ASP.NET Core, net8.0/net10.0) package: create/edit templates, version history, compare, live preview, restore, reusable snippets, and configurable authorization.

`TemplateBuilder.Editor` (ASP.NET Core Razor Class Library) cannot be consumed by .NET Framework 4.8 projects, for two independent, compounding reasons:

1. **NuGet TFM incompatibility.** The package only ships `net8.0`/`net10.0` assets. There is no compatibility path from `net48` to those TFMs — restore fails outright.
2. **Architectural incompatibility.** `TemplateBuilder.Editor` depends on `FrameworkReference Microsoft.AspNetCore.App` and the ASP.NET Core hosting/DI/middleware/Razor pipeline. Classic ASP.NET MVC 5 runs on `System.Web` — a different, incompatible hosting model with no equivalent of `FrameworkReference`. Additionally, `TemplateBuilder.Infrastructure` depends on EF Core 8/10, and EF Core dropped .NET Framework support after EF Core 3.1.

A process-level workaround (running the ASP.NET Core Editor as a sidecar/reverse-proxied service) was considered and rejected — the client does not want to operate a second deployable process for other reasons. This design is a genuine second implementation, not a hosting trick.

---

## Client environment (discovered via `discover-client-solution.ps1` against the real solution)

| Aspect | Finding |
|---|---|
| Web framework | **ASP.NET MVC 5** (`Microsoft.AspNet.Mvc 5.3.0`, `System.Web.Mvc`/`System.Web.Http` references, 571 `.cshtml` views, **zero** `.aspx`/`.ascx` — confirms MVC 5, not Web Forms) |
| Project style | Legacy (non-SDK-style) `.csproj`, `TargetFrameworkVersion=v4.8`, **`packages.config`**-based NuGet (the old Package Manager Console install model, not `PackageReference`) |
| Data access | **Entity Framework 6.5.1 (EF6)** — referenced in 27 files. `System.Data.SqlClient` (29 files) and `Microsoft.Data.SqlClient 6.1.1` (4 files) both present — a live migration in progress on their side |
| DI container | **Unity 5.11.10 + Unity.Mvc5 1.4.0** — activates their MVC5 controllers today |
| Auth | `web.config` `authentication mode="None"` — auth is fully **OWIN middleware-driven**: `Microsoft.Owin.Security.Cookies`, `Microsoft.AspNet.Identity.Owin`/`.EntityFramework`, plus MSAL/OIDC packages (`Microsoft.Identity.Client`, `Microsoft.IdentityModel.Protocols.OpenIdConnect`) suggesting Entra ID/OIDC login. Claims-based — `User.IsInRole()`/`ClaimsPrincipal` work the same way our authorization convention already assumes. |
| Front-end | jQuery 3.7.1, **Bootstrap 3.3.7** (old v3), Modernizr, jQuery UI, jQuery Validation, DataTables, FontAwesome 4.7, IgniteUI — referenced across 470+ views. Our editor CSS is scoped under `#tb-editor-host`, which should prevent collision, but must be verified once real views are ported. |
| Other | Solution also contains a second, unrelated **net8.0 SDK-style** project (`B23_RAIN.Jobs`, Azure Functions Worker) — confirms the environment has modern .NET tooling installed, but doesn't change the constraint on the web app itself. |

---

## Decision: full duplication, separate repo

Two decisions were made explicitly, overriding what would otherwise be the "least duplication" answer:

1. **Approach A (full-fidelity port)**, not a source-scaffold package or a hybrid — chosen for full feature parity and a true "reference the package, call one setup method" consumer experience matching today's Editor.
2. **Everything — including `Domain` and `Application` — is duplicated into a new, separate repository**, rather than sharing those two layers with this repo via a new multi-targeted (`net48`) NuGet package. This was an explicit tradeoff: it accepts future drift risk between the two copies of `Domain`/`Application` business logic, in exchange for total isolation of the new repo's toolchain (`packages.config`, RazorGenerator, Unity, non-SDK-style patterns are foreign to this repo) from the actively-published, modern `TemplateBuilder` repo. **This repo (`TemplateBuilder`) requires zero changes as a result of this project** — no TFM additions, no new packages published from here.

### Confirmed technical fact backing the port (not an assumption)

All four of `TemplateBuilder.Application`'s current runtime dependencies publish .NET Framework/`.NETStandard2.0`-compatible builds, verified against the live NuGet flatcontainer index:

| Package | Version (current, net8.0/net10.0 line) | .NET Framework compatibility |
|---|---|---|
| HtmlSanitizer | 9.2.995 | `.NETFramework4.6.2`, `.NETFramework4.7` dependency groups (AngleSharp 1.7.1) |
| Scriban | 7.2.6 | `.NETStandard2.0` dependency group |
| Microsoft.Data.SqlClient | 7.0.2 | `.NETFramework4.6.2` dependency group |
| Microsoft.Extensions.Caching.Memory | 8.0.1 | `.NETFramework4.6.2` dependency group |
| Microsoft.Extensions.Options | 8.0.2 | `.NETFramework4.6.2` dependency group |

This means `Domain` and `Application`'s C# source needs **zero changes** to run on `net48` — only a duplicated project file with `net48` as the sole `TargetFramework`, and (per the decision above) its own copy of the source rather than a shared multi-targeted project.

---

## Architecture

### Repo & solution structure

SDK-style `.csproj` throughout (targeting `net48` does not require old-style project files — `Microsoft.NET.Sdk` + `PackageReference` works cleanly on `net48` and gives the same `dotnet build`/`dotnet test` workflow this repo already uses):

```
TemplateBuilder.Mvc5/                        (new repo root)
├── src/
│   ├── TemplateBuilder.Domain/               net48 — entities/interfaces, ported source, no logic changes
│   ├── TemplateBuilder.Application/          net48 — Scriban engine, sanitizer, SQL view discovery, ported source
│   ├── TemplateBuilder.Infrastructure.EF6/   net48 — EF6 DbContext, Code-First migrations, repositories (NEW)
│   └── TemplateBuilder.Editor.Mvc5/          net48 — MVC5 controllers/views/RazorGenerator/Unity registration (NEW)
├── tests/                                    mirrors src/, xunit — same conventions as the origin repo
│   ├── TemplateBuilder.Domain.Tests/
│   ├── TemplateBuilder.Application.Tests/
│   ├── TemplateBuilder.Infrastructure.EF6.Tests/
│   └── TemplateBuilder.Editor.Mvc5.Tests/
├── samples/
│   └── TemplateBuilder.SampleMvc5Host/       small standalone MVC5 app for local dev/testing — plays the role
│                                              TemplateBuilder.Web plays in the origin repo
├── docs/
│   └── superpowers/
│       ├── specs/
│       └── plans/
├── CLAUDE.md
├── README.md
└── TemplateBuilder.Mvc5.sln
```

### Package identity

**`TemplateBuilder.Editor.Mvc5`** — a new, distinct NuGet package ID. Reusing `TemplateBuilder.Editor` for a completely different (MVC5/EF6/Unity) implementation under the same ID would be a support and versioning hazard (which implementation a given version number refers to becomes ambiguous). `Domain`, `Application`, and `Infrastructure.EF6` stay `IsPackable=false` internal projects, bundled into `TemplateBuilder.Editor.Mvc5`'s `lib/net48/` via the same `BundleInternalAssemblies` MSBuild target pattern the origin repo already uses for `Editor`/`Core` — one package to install, matching existing precedent.

### Data layer — `Infrastructure.EF6`

- EF6 `DbContext` + Code-First migrations.
- Same table/column shapes as the existing EF Core migrations (`Templates`, `TemplateVersions`, `Snippets`) — the two Infrastructure implementations never share code, but the schema stays conceptually interchangeable.
- Implements `ITemplateRepository`/`ISnippetRepository` (ported `Domain` interfaces) identically in shape to the EF Core implementation, so `Application`'s `TemplateEngine`/`SqlViewDiscoveryService`/`HtmlSanitizerService` don't need to know which Infrastructure is behind them.
- Migration-on-startup via EF6's `MigrateDatabaseToLatestVersion` initializer, invoked from `Application_Start` — analogous to today's `MigrationHostedService`.

### UI layer — `Editor.Mvc5`

- **Controllers**: ported from `TemplatesController`/`SnippetsController`/`SetupController`. Same action shapes; swap ASP.NET Core-specific types (`IActionResult` → `ActionResult`) for MVC5 equivalents.
- **Views**: ported from the 6 existing `.cshtml` files. Mechanical but real work — no tag helpers (`asp-controller` → `@Url.Action`/`@Html.BeginForm`), `_ViewImports.cshtml` → `_ViewStart.cshtml` + `Web.config` namespace imports.
- **RazorGenerator.Mvc** precompiles views into the DLL at build time. The consumer never sees `.cshtml` files — matches today's "reference the package, call one setup method" experience. This is the piece of the design with the least direct precedent in the existing codebase and carries the most implementation risk; validate it early (spike a single view end-to-end before porting all six).
- **Static assets** (`template-editor.js`/`.css`): embedded as assembly resources, served through a small custom route/handler mirroring ASP.NET Core's `/_content/...` convention. Zero physical files land in the consumer's project.
- **DI**: `container.RegisterTemplateBuilderEditor(options => ...)` extension method on `IUnityContainer` — matches how `Unity.Mvc5` already activates the client's controllers, so this introduces no new DI concept for them.
- **Authorization**: the `Anonymous`/`Authenticated`/`Role`/`PolicyName` convention (currently an ASP.NET Core `IControllerModelConvention`) is ported to a global MVC5 `IAuthorizationFilter` applied via a controller-selector convention, working against `ClaimsPrincipal`/`User.IsInRole()` — the same shape the client's OWIN/claims auth pipeline already produces.
- **Routing bootstrap**: `TemplateBuilderEditorRouteConfig.RegisterRoutes(RouteTable.Routes)`, called once from the client's own `RouteConfig` — analogous to today's `app.MapControllers()`.

### Packaging & distribution

- Single package `TemplateBuilder.Editor.Mvc5`, `net48`. Default assumption: published to nuget.org, same as `TemplateBuilder.Editor`/`TemplateBuilder.Core` today. If the client relationship requires a private feed instead, confirm before the first publish — nothing in this design depends on which registry is used.
- The client's solution is `packages.config`-style and already references `Microsoft.Data.SqlClient 6.1.1` and `EntityFramework 6.5.1` — different versions than whatever this package will depend on. Classic `packages.config` projects need explicit **assembly binding redirects** to reconcile version differences.
  - Plan: pin `Infrastructure.EF6` to the client's existing EF6 line (6.5.x) to avoid a redirect entirely on that dependency where possible; for anything that can't be aligned exactly, ship an `install.ps1` that adds the necessary `<bindingRedirect>` entries to the consumer's `web.config` automatically (the classic NuGet v1/v2 convention their tooling still supports).

### Testing & dev loop

- Same xunit conventions as the origin repo. `Domain`/`Application` test suites port over close to as-is (same assertions, ported to the duplicated source).
- `Infrastructure.EF6` tests run against LocalDB.
- `Editor.Mvc5` gets controller-level unit tests, plus `SampleMvc5Host` for manual/browser verification — the same role `TemplateBuilder.Web` plays in the origin repo.

---

## Explicit non-goals / out of scope for v1

- Sharing any code with the origin `TemplateBuilder` repo (see "Decision" above — deliberate, not an oversight).
- Web Forms support — confirmed not needed; client is MVC 5.
- A hosted-service/sidecar integration model — explicitly rejected earlier in favor of a genuine second implementation.
- Byte-identical EF6/EF Core schema guarantees beyond "conceptually interchangeable" table/column shapes — no requirement surfaced for the two Infrastructure implementations to share one physical database at this time.

---

## Known risks

1. **RazorGenerator precompiled-views pipeline** — least-precedented piece of this design. Mitigate by spiking one view (e.g., `Templates/Index.cshtml`) end-to-end before committing to porting all six.
2. **Assembly binding redirects** on a `packages.config` consumer — must be validated against the client's actual `web.config` early, not assumed to "just work" from `install.ps1`.
3. **Bootstrap 3.3.7 / jQuery 3.7.1 / IgniteUI CSS collision** — the current Editor's CSS scoping (`#tb-editor-host`) is designed to prevent bleed, but this has never been validated against a Bootstrap **v3** host (the origin repo has only ever been tested standalone). Verify visually once the sample host is running.
4. **Drift between duplicated `Domain`/`Application` and the origin repo's versions** — accepted tradeoff (see Decision section); no mitigation planned beyond documentation of the fork point (this design doc + the commit hash of the origin repo at fork time).

---

## Fork point reference

Domain/Application source in this design is to be copied from `TemplateBuilder` at the state described in this document. Record the origin repo's commit hash in the new repo's initial commit message so future maintainers can diff against the source of truth if needed.
