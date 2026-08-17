# TemplateBuilder.Editor.Mvc5

Template management UI for ASP.NET MVC 5 / .NET Framework 4.8 applications: create and edit
templates, version history, restore, live preview, reusable snippets, and configurable
authorization. Templates are rendered with Scriban and sanitized before rendering.

## Install

```
Install-Package TemplateBuilder.Editor.Mvc5
```

Requires ASP.NET MVC 5.x, Unity 5.x, Entity Framework 6.x and .NET Framework 4.8.

## Setup

In `UnityConfig` (or wherever you register your Unity container):

```csharp
container.RegisterTemplateBuilderEditor(options =>
{
    options.ConnectionString = ConfigurationManager.ConnectionStrings["TemplateDb"].ConnectionString;
    options.Authorization.Mode = TemplateBuilderAuthorizationMode.AuthenticatedUser; // or Anonymous/ConfiguredRoles
});
```

The editor renders inside a `#tb-editor-host` container so its CSS cannot collide with your
host page's Bootstrap 3 (or other) styles.

## Wiring

1. **Routing** — in `RegisterRoutes` (or `Application_Start`):

```csharp
TemplateBuilderEditorRouteConfig.RegisterRoutes(RouteTable.Routes);
```

This maps the MVC attribute routes (`/Templates`, `/Templates/{id}/Edit`, `/Templates/_setup`,
`/Templates/Api/Snippets`, ...) and the `/TemplateBuilderEditor/{*path}` static-asset route
(`css/template-editor.css`, `js/template-editor.js`).

2. **Assets** — add these to the page or layout that hosts the editor (mirrors the ASP.NET Core
   `/_content/...` convention):

```html
<link href="/TemplateBuilderEditor/css/template-editor.css" rel="stylesheet" />
<script src="/TemplateBuilderEditor/js/template-editor.js"></script>
```

3. **Authorization** — register `TemplateBuilderAuthorizationFilter` as a global filter if you
   want the editor's policy applied to all requests:

```csharp
filters.Add(new TemplateBuilderAuthorizationFilter());
```

4. **Async actions** — the editor's controllers use `async`/`await`. IIS + .NET Framework flow
   `HttpContext` through async continuations natively, so no extra wiring is needed on Windows.

5. **Connection string** — the editor's EF6 context migrates the schema on first access. Point
   `options.ConnectionString` at a database you own (the sample host uses a
   `TemplateBuilderDbContext` connection string for the migration initializer's internal context).

## Diagnostics

`/Templates/_setup` runs a setup check that reports each requirement (database reachability,
schema, view discovery, static assets) with PASS/FAIL and a fix for every failure.

## What's New

### v1.0.0

- Initial release. MVC 5 port of `TemplateBuilder.Editor` (ASP.NET Core), including:
  - Template create/edit with Scriban body, find & replace, live preview with model JSON
  - Version history, compare/restore, duplicate, toggle active, server-side validation
  - Reusable snippets
  - SQL view discovery for template preview data
  - Configurable authorization (anonymous / authenticated user / configured roles)
  - `#tb-editor-host` CSS scoping against host-page style collisions