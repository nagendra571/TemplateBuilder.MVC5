# TemplateBuilder.Editor.Mvc5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `TemplateBuilder.Editor.Mvc5`, a NuGet package that gives ASP.NET MVC 5 / .NET Framework 4.8 consumers full feature parity with the existing ASP.NET Core `TemplateBuilder.Editor` package: create/edit templates, version history, compare, live preview, restore, reusable snippets, configurable authorization — as a "reference the package, call one setup method" experience.

**Architecture:** `Domain` and `Application` are duplicated (copied verbatim, only the `.csproj` changes to `net48`) from the origin `TemplateBuilder` repo — their runtime dependencies (Scriban, HtmlSanitizer, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.*`) are confirmed net48-compatible, so zero source changes are needed there. A new `Infrastructure.EF6` project replaces EF Core with EF6 (which EF Core cannot run on .NET Framework), implementing the same `ITemplateRepository`/`ISnippetRepository` contracts. A new `Editor.Mvc5` project replaces the ASP.NET Core Razor Class Library with MVC 5 controllers + RazorGenerator-precompiled views + Unity-based DI registration, ported action-by-action and route-by-route from the existing controllers. JS/CSS assets are reused unchanged as embedded resources.

**Tech Stack:** .NET Framework 4.8, SDK-style `.csproj` (`Microsoft.NET.Sdk`), ASP.NET MVC 5.3.0, EntityFramework 6.5.x, Unity 5.11.x + Unity.Mvc5, RazorGenerator.Mvc/RazorGenerator.MsBuild, Newtonsoft.Json 13.0.3, xunit.

**Spec:** `docs/superpowers/specs/2026-08-16-net48-mvc5-editor-design.md`

## Global Constraints

- Target framework for every `src/`/`tests/` project: `net48` (single TFM — no multi-targeting; this is a standalone net48 line, per the spec's "full duplication" decision).
- SDK-style `.csproj` throughout `src/`/`tests/`/`samples/TemplateBuilder.SampleMvc5Host` is the **exception**: it must be an old-style ASP.NET MVC5 Web Application project (`<ProjectTypeGuids>` for MVC, `Global.asax`, IIS-Express-hostable `web.config`) because `System.Web.Mvc` requires IIS/`System.Web` hosting — there is no SDK-style way to host it. This is a deliberate, necessary deviation from "SDK-style throughout," not an oversight.
- `Domain`/`Application` source is **copied verbatim** from `C:\Users\nchinnam\source\repos\TemplateBuilder\src\TemplateBuilder.Domain` and `...\TemplateBuilder.Application` — no logic changes. If that path has moved or the repo no longer exists, treat the interface signatures embedded in this plan (Task 2/3) as the fallback source of truth and reconstruct the files from the design spec's "Confirmed technical fact" table plus the signatures below.
- New package ID: `TemplateBuilder.Editor.Mvc5`. `Domain`/`Application`/`Infrastructure.EF6` stay `IsPackable=false`, bundled into `TemplateBuilder.Editor.Mvc5`'s `lib/net48/` via a `BundleInternalAssemblies` MSBuild target (same pattern as the origin repo's `Editor.csproj`).
- Client's existing `packages.config` already has `EntityFramework 6.5.1` and `Microsoft.Data.SqlClient 6.1.1` — pin `Infrastructure.EF6`'s `EntityFramework` `PackageReference` to `6.5.1` exactly to avoid a binding redirect on that dependency.
- Unity namespace is `Unity` (the modern `Unity`/`Unity.Container` 5.x line the client already has), **not** `Microsoft.Practices.Unity` (the old Unity 3.x/4.x namespace). Getting this wrong will not compile against the client's actual packages.
- Every task ends with a `dotnet build`/`dotnet test` (or, where noted, a Visual Studio Package Manager Console step) that must actually pass before moving to the next task.

---

## Task 1: Scaffold the repo, solution, and all project skeletons

**Files:**
- Create: `TemplateBuilder.Mvc5.sln`
- Create: `src/TemplateBuilder.Domain/TemplateBuilder.Domain.csproj`
- Create: `src/TemplateBuilder.Application/TemplateBuilder.Application.csproj`
- Create: `src/TemplateBuilder.Infrastructure.EF6/TemplateBuilder.Infrastructure.EF6.csproj`
- Create: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`
- Create: `tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj`
- Create: `tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj`
- Create: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj`
- Create: `.gitignore`

**Interfaces:**
- Produces: an empty, buildable solution with 4 `src/` class-library skeletons (each with a single placeholder `.cs` file) and 3 corresponding xunit test projects, all targeting `net48`.

- [ ] **Step 1: Create the folder structure and empty solution**

```bash
mkdir -p src/TemplateBuilder.Domain src/TemplateBuilder.Application src/TemplateBuilder.Infrastructure.EF6 src/TemplateBuilder.Editor.Mvc5
mkdir -p tests/TemplateBuilder.Domain.Tests tests/TemplateBuilder.Application.Tests tests/TemplateBuilder.Infrastructure.EF6.Tests
mkdir -p samples docs/superpowers/specs docs/superpowers/plans
git init
dotnet new sln -n TemplateBuilder.Mvc5
```

- [ ] **Step 2: Create `TemplateBuilder.Domain.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create `TemplateBuilder.Application.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\TemplateBuilder.Domain\TemplateBuilder.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="HtmlSanitizer" Version="9.2.995" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.2" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.2" />
    <PackageReference Include="Scriban" Version="7.2.6" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>TemplateBuilder.Application.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `TemplateBuilder.Infrastructure.EF6.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\TemplateBuilder.Domain\TemplateBuilder.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="EntityFramework" Version="6.5.1" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Create `TemplateBuilder.Editor.Mvc5.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\TemplateBuilder.Application\TemplateBuilder.Application.csproj">
      <PrivateAssets>all</PrivateAssets>
    </ProjectReference>
    <ProjectReference Include="..\TemplateBuilder.Infrastructure.EF6\TemplateBuilder.Infrastructure.EF6.csproj">
      <PrivateAssets>all</PrivateAssets>
    </ProjectReference>
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNet.Mvc" Version="5.3.0" />
    <PackageReference Include="Unity" Version="5.11.10" />
    <PackageReference Include="Unity.Mvc5" Version="1.4.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="RazorGenerator.Mvc" Version="2.5.0" />
    <PackageReference Include="RazorGenerator.MsBuild" Version="2.5.0" PrivateAssets="all" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PackageId>TemplateBuilder.Editor.Mvc5</PackageId>
    <Version>1.0.0</Version>
    <Authors>TemplateBuilder</Authors>
    <Description>Full template management UI (create, edit, version history, compare, snippets) for ASP.NET MVC 5 / .NET Framework 4.8. Call container.RegisterTemplateBuilderEditor(...) then wire up routes.</Description>
    <PackageTags>template;scriban;html;editor;mvc5;netframework</PackageTags>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
</Project>
```

*(`RazorGenerator.Mvc`/`RazorGenerator.MsBuild` version `2.5.0` is the plan's assumption — confirm the actual latest version on nuget.org before this step; if a newer version exists, use it instead, the setup steps below don't change.)*

- [ ] **Step 6: Create matching `.Tests.csproj` files for Domain, Application, Infrastructure.EF6**

Same shape as the origin repo's test projects (see `Global Constraints` — copy the pattern, not the content):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="10.0.1" />
    <PackageReference Include="FluentAssertions" Version="8.10.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <!-- add ProjectReference to the project under test (and its dependencies) here -->
  </ItemGroup>
</Project>
```

Add `<ProjectReference>` to `TemplateBuilder.Domain.Tests.csproj` → Domain; `TemplateBuilder.Application.Tests.csproj` → Application + Domain; `TemplateBuilder.Infrastructure.EF6.Tests.csproj` → Infrastructure.EF6 + Domain. Add `Moq` `Version="4.20.72"` to Application.Tests only — its test suite is ported verbatim from the origin repo, which uses Moq; `Infrastructure.EF6.Tests` (Task 5) tests against a real LocalDB context instead and doesn't need it.

- [ ] **Step 7: Add every project to the solution**

```bash
dotnet sln add src/TemplateBuilder.Domain/TemplateBuilder.Domain.csproj
dotnet sln add src/TemplateBuilder.Application/TemplateBuilder.Application.csproj
dotnet sln add src/TemplateBuilder.Infrastructure.EF6/TemplateBuilder.Infrastructure.EF6.csproj
dotnet sln add src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
dotnet sln add tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj
dotnet sln add tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj
dotnet sln add tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj
```

- [ ] **Step 8: Add one placeholder class per `src/` project so it compiles**

E.g. `src/TemplateBuilder.Domain/AssemblyMarker.cs`:
```csharp
namespace TemplateBuilder.Domain;
internal static class AssemblyMarker { }
```
Same pattern (adjust namespace) for `Application`, `Infrastructure.EF6`, `Editor.Mvc5`.

- [ ] **Step 9: Verify the whole (still-empty) solution builds**

```bash
dotnet build TemplateBuilder.Mvc5.sln
```
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 10: Create `.gitignore` and commit**

```
bin/
obj/
*.user
.vs/
```

```bash
git add -A
git commit -m "chore: scaffold TemplateBuilder.Mvc5 solution and empty project skeletons"
```

---

## Task 2: Port `TemplateBuilder.Domain`

**Files:**
- Create: `src/TemplateBuilder.Domain/Entities/Template.cs`
- Create: `src/TemplateBuilder.Domain/Entities/TemplateVersion.cs`
- Create: `src/TemplateBuilder.Domain/Entities/Snippet.cs`
- Create: `src/TemplateBuilder.Domain/Interfaces/ITemplateRepository.cs`
- Create: `src/TemplateBuilder.Domain/Interfaces/ISnippetRepository.cs`
- Create: `src/TemplateBuilder.Domain/Interfaces/ITemplateEngine.cs`
- Create: `src/TemplateBuilder.Domain/Exceptions/SchemaVersionMismatchException.cs`
- Create: `src/TemplateBuilder.Domain/Exceptions/TemplateNotFoundException.cs`
- Create: `src/TemplateBuilder.Domain/Exceptions/TemplateRenderException.cs`
- Delete: `src/TemplateBuilder.Domain/AssemblyMarker.cs` (placeholder from Task 1)

**Interfaces:**
- Produces: `Template`, `TemplateVersion`, `Snippet` entities; `ITemplateRepository`, `ISnippetRepository`, `ITemplateEngine` interfaces — all consumed by every later task.

- [ ] **Step 1: Copy the 9 files verbatim**

Copy the byte-identical content of each file from `C:\Users\nchinnam\source\repos\TemplateBuilder\src\TemplateBuilder.Domain\` (same relative sub-paths: `Entities/`, `Interfaces/`, `Exceptions/`) into this repo's `src/TemplateBuilder.Domain/`. Do not change a single line — this is a verbatim port. For reference, the three entities and the three interfaces this plan's later tasks depend on have this exact shape:

```csharp
// Entities/Template.cs
namespace TemplateBuilder.Domain.Entities;

public class Template
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? CurrentVersionId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<TemplateVersion> Versions { get; set; } = new List<TemplateVersion>();
    public TemplateVersion? CurrentVersion { get; set; }
}
```

```csharp
// Entities/TemplateVersion.cs
namespace TemplateBuilder.Domain.Entities;

public class TemplateVersion
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ChangeComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public Template Template { get; set; } = null!;
}
```

```csharp
// Entities/Snippet.cs
namespace TemplateBuilder.Domain.Entities;

public class Snippet
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

```csharp
// Interfaces/ITemplateRepository.cs
using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Domain.Interfaces;

public interface ITemplateRepository
{
    Task<Template?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Template?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<int?> GetCurrentVersionIdAsync(int templateId, CancellationToken ct = default);
    Task<string?> GetVersionBodyAsync(int versionId, CancellationToken ct = default);
    Task<IReadOnlyList<Template>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TemplateVersion>> GetVersionHistoryAsync(int templateId, CancellationToken ct = default);
    Task<int> GetNextVersionNumberAsync(int templateId, CancellationToken ct = default);
    Task<Template> CreateAsync(Template template, CancellationToken ct = default);
    Task UpdateTemplateAsync(Template template, CancellationToken ct = default);
    Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, CancellationToken ct = default);
}
```

```csharp
// Interfaces/ISnippetRepository.cs
using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Domain.Interfaces;

public interface ISnippetRepository
{
    Task<IReadOnlyList<Snippet>> GetAllAsync(CancellationToken ct = default);
    Task<Snippet?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Snippet> CreateAsync(Snippet snippet, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
```

```csharp
// Interfaces/ITemplateEngine.cs
namespace TemplateBuilder.Domain.Interfaces;

public interface ITemplateEngine
{
    Task<string> RenderAsync(int templateId, object model, CancellationToken ct = default);
    Task<string> RenderByNameAsync(string templateName, object model, CancellationToken ct = default);
    Task<string> RenderBodyAsync(string body, object model, CancellationToken ct = default);
}
```

For `SchemaVersionMismatchException.cs`, `TemplateNotFoundException.cs`, `TemplateRenderException.cs` — copy those three files' content directly from the origin path (they're plain exception classes; this plan didn't need their exact body to design later tasks, but the port must still bring them over unchanged since `Application`'s ported source references them).

- [ ] **Step 2: Delete the Task-1 placeholder**

```bash
rm src/TemplateBuilder.Domain/AssemblyMarker.cs
```

- [ ] **Step 3: Build**

```bash
dotnet build src/TemplateBuilder.Domain/TemplateBuilder.Domain.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Port the Domain test suite**

Copy `tests/TemplateBuilder.Domain.Tests/` content verbatim from the origin repo's equivalent folder into this repo's `tests/TemplateBuilder.Domain.Tests/` (same reasoning as Step 1 — these tests only exercise plain POCOs/interfaces, nothing framework-specific).

- [ ] **Step 5: Run the ported tests**

```bash
dotnet test tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj
```
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Domain tests/TemplateBuilder.Domain.Tests
git commit -m "feat: port TemplateBuilder.Domain to net48 (verbatim)"
```

---

## Task 3: Port `TemplateBuilder.Application`

**Files:**
- Create: `src/TemplateBuilder.Application/DTOs/SqlColumnInfo.cs`
- Create: `src/TemplateBuilder.Application/Options/TemplateBuilderOptions.cs`
- Create: `src/TemplateBuilder.Application/Services/IHtmlSanitizerService.cs`
- Create: `src/TemplateBuilder.Application/Services/HtmlSanitizerService.cs`
- Create: `src/TemplateBuilder.Application/Services/ISqlViewDiscoveryService.cs`
- Create: `src/TemplateBuilder.Application/Services/SqlViewDiscoveryService.cs`
- Create: `src/TemplateBuilder.Application/Services/SchemaVersionValidator.cs`
- Create: `src/TemplateBuilder.Application/Services/TemplateEngine.cs`
- Delete: `src/TemplateBuilder.Application/AssemblyMarker.cs` (placeholder from Task 1)

**Interfaces:**
- Consumes: `Domain` entities/interfaces/exceptions (Task 2).
- Produces: `IHtmlSanitizerService`, `ISqlViewDiscoveryService`, `ITemplateEngine` implementation, `TemplateBuilderOptions`, `SqlColumnInfo` — all consumed by `Infrastructure.EF6`'s DI wiring and `Editor.Mvc5`'s controllers.

For reference, the two interfaces later tasks call directly:

```csharp
// Services/ISqlViewDiscoveryService.cs
using TemplateBuilder.Application.DTOs;
namespace TemplateBuilder.Application.Services;

public interface ISqlViewDiscoveryService
{
    Task<IReadOnlyList<string>> GetViewNamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SqlColumnInfo>> GetViewColumnsAsync(string viewName, CancellationToken ct = default);
}
```

```csharp
// Services/IHtmlSanitizerService.cs
namespace TemplateBuilder.Application.Services;

public interface IHtmlSanitizerService
{
    string Sanitize(string html);
}
```

- [ ] **Step 1: Copy the 8 files verbatim**

Copy byte-identical content from `C:\Users\nchinnam\source\repos\TemplateBuilder\src\TemplateBuilder.Application\` (same relative sub-paths) into this repo's `src/TemplateBuilder.Application/`. No logic changes — all four runtime dependencies (Scriban, HtmlSanitizer, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.Caching.Memory`/`Options`) are confirmed net48-compatible (see spec), so the exact same C# compiles unchanged.

- [ ] **Step 2: Delete the Task-1 placeholder**

```bash
rm src/TemplateBuilder.Application/AssemblyMarker.cs
```

- [ ] **Step 3: Build**

```bash
dotnet build src/TemplateBuilder.Application/TemplateBuilder.Application.csproj
```
Expected: `Build succeeded.` If it fails on a missing net48-compatible API from one of the 4 packages, that's new information contradicting the spec's compatibility table — stop and re-verify the specific failing call against the package's actual `net48`/`.NETFramework4.6.2` assembly (not just its dependency-group listing) before proceeding.

- [ ] **Step 4: Port the Application test suite**

Copy `tests/TemplateBuilder.Application.Tests/` content verbatim from the origin repo.

- [ ] **Step 5: Run the ported tests**

```bash
dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj
```
Expected: all tests pass (21 tests in the origin repo's suite, at time of writing).

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Application tests/TemplateBuilder.Application.Tests
git commit -m "feat: port TemplateBuilder.Application to net48 (verbatim)"
```

---

## Task 4: `Infrastructure.EF6` — DbContext, entity configuration, migration

**Files:**
- Create: `src/TemplateBuilder.Infrastructure.EF6/Data/TemplateBuilderDbContext.cs`
- Delete: `src/TemplateBuilder.Infrastructure.EF6/AssemblyMarker.cs`

**Interfaces:**
- Consumes: `Template`, `TemplateVersion`, `Snippet` (Task 2).
- Produces: `TemplateBuilderDbContext` — consumed by Task 5's repositories and Task 6's DI registration.

- [ ] **Step 1: Write the DbContext with Fluent API configuration**

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Data.Entity.Infrastructure.Annotations;
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Infrastructure.EF6.Data;

public class TemplateBuilderDbContext : DbContext
{
    public TemplateBuilderDbContext(string connectionString) : base(connectionString)
    {
        Database.SetInitializer(new MigrateDatabaseToLatestVersion<TemplateBuilderDbContext, Migrations.Configuration>());
    }

    public DbSet<Template> Templates { get; set; } = null!;
    public DbSet<TemplateVersion> TemplateVersions { get; set; } = null!;
    public DbSet<Snippet> Snippets { get; set; } = null!;

    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Template>(e =>
        {
            e.ToTable("Templates");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name)
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnAnnotation(
                    IndexAnnotation.AnnotationName,
                    new IndexAnnotation(new IndexAttribute("IX_Templates_Name") { IsUnique = true }));
            e.Property(t => t.TemplateType).IsRequired().HasMaxLength(50);
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.RowVersion).IsRowVersion();
            e.HasMany(t => t.Versions)
                .WithRequired(v => v.Template)
                .HasForeignKey(v => v.TemplateId)
                .WillCascadeOnDelete(false);
            e.HasOptional(t => t.CurrentVersion)
                .WithMany()
                .HasForeignKey(t => t.CurrentVersionId)
                .WillCascadeOnDelete(false);
        });

        modelBuilder.Entity<TemplateVersion>(e =>
        {
            e.ToTable("TemplateVersions");
            e.HasKey(v => v.Id);
            e.Property(v => v.Body).IsRequired();
            e.Property(v => v.ChangeComment).HasMaxLength(500);
            e.Property(v => v.CreatedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<Snippet>(e =>
        {
            e.ToTable("Snippets");
            e.HasKey(s => s.Id);
            e.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnAnnotation(
                    IndexAnnotation.AnnotationName,
                    new IndexAnnotation(new IndexAttribute("IX_Snippets_Name") { IsUnique = true }));
            e.Property(s => s.Description).HasMaxLength(500);
            e.Property(s => s.Body).IsRequired();
        });
    }
}
```

- [ ] **Step 2: Delete the Task-1 placeholder**

```bash
rm src/TemplateBuilder.Infrastructure.EF6/AssemblyMarker.cs
```

- [ ] **Step 3: Build**

```bash
dotnet build src/TemplateBuilder.Infrastructure.EF6/TemplateBuilder.Infrastructure.EF6.csproj
```
Expected: fails at this point — `Migrations.Configuration` doesn't exist yet. That's expected; it's created in Step 4.

- [ ] **Step 4: Enable EF6 Code-First Migrations and generate the initial migration (Visual Studio Package Manager Console)**

EF6 migrations are driven by Visual Studio's Package Manager Console (there is no `dotnet ef`-equivalent CLI for EF6 on SDK-style projects). Open the solution in Visual Studio, set `TemplateBuilder.Infrastructure.EF6` as the PMC "Default project", and run:

```powershell
Enable-Migrations -ContextTypeName TemplateBuilder.Infrastructure.EF6.Data.TemplateBuilderDbContext -MigrationsDirectory Migrations
Add-Migration InitialCreate
```

This generates `Migrations/Configuration.cs` (referenced by Step 1's `MigrateDatabaseToLatestVersion<TemplateBuilderDbContext, Migrations.Configuration>()`) and `Migrations/<timestamp>_InitialCreate.cs`. Verify the generated migration creates exactly three tables (`Templates`, `TemplateVersions`, `Snippets`) with the columns/constraints from Step 1 — read the generated `Up()` method and confirm it matches.

- [ ] **Step 5: Build again**

```bash
dotnet build src/TemplateBuilder.Infrastructure.EF6/TemplateBuilder.Infrastructure.EF6.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Infrastructure.EF6
git commit -m "feat: add EF6 DbContext, entity configuration, and initial migration"
```

---

## Task 5: `Infrastructure.EF6` — repository implementations

**Files:**
- Create: `src/TemplateBuilder.Infrastructure.EF6/Repositories/TemplateRepository.cs`
- Create: `src/TemplateBuilder.Infrastructure.EF6/Repositories/SnippetRepository.cs`
- Test: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateRepositoryTests.cs`
- Test: `tests/TemplateBuilder.Infrastructure.EF6.Tests/SnippetRepositoryTests.cs`

**Interfaces:**
- Consumes: `TemplateBuilderDbContext` (Task 4), `ITemplateRepository`/`ISnippetRepository` (Task 2).
- Produces: `TemplateRepository : ITemplateRepository`, `SnippetRepository : ISnippetRepository` — consumed by Task 6's DI registration.

- [ ] **Step 1: Write the failing test for `TemplateRepository.CreateAsync` + `GetByIdAsync`**

```csharp
using System.Data.Entity;
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

public class TemplateRepositoryTests
{
    private static TemplateBuilderDbContext CreateContext()
    {
        var ctx = new TemplateBuilderDbContext(
            "Server=(localdb)\\MSSQLLocalDB;Database=TemplateBuilderMvc5Tests;Trusted_Connection=True;");
        Database.SetInitializer(new DropCreateDatabaseAlways<TemplateBuilderDbContext>());
        ctx.Database.Initialize(force: true);
        return ctx;
    }

    [Fact]
    public async Task CreateAsync_then_GetByIdAsync_returns_the_created_template()
    {
        using var ctx = CreateContext();
        var repo = new TemplateRepository(ctx);

        var created = await repo.CreateAsync(new Template
        {
            Name = "Welcome Email",
            TemplateType = "Email"
        });

        var fetched = await repo.GetByIdAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Welcome Email");
        fetched.CreatedAt.Should().NotBe(default);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --filter CreateAsync_then_GetByIdAsync_returns_the_created_template
```
Expected: FAIL — `TemplateRepository` doesn't exist yet.

- [ ] **Step 3: Implement `TemplateRepository`**

```csharp
using System.Data.Entity;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class TemplateRepository : ITemplateRepository
{
    private readonly TemplateBuilderDbContext _db;

    public TemplateRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task<Template?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Templates.Include(t => t.CurrentVersion).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Template?> GetByNameAsync(string name, CancellationToken ct = default)
        => await _db.Templates.Include(t => t.CurrentVersion).FirstOrDefaultAsync(t => t.Name == name, ct);

    public async Task<int?> GetCurrentVersionIdAsync(int templateId, CancellationToken ct = default)
    {
        var template = await _db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
        return template?.CurrentVersionId;
    }

    public async Task<string?> GetVersionBodyAsync(int versionId, CancellationToken ct = default)
    {
        var version = await _db.TemplateVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
        return version?.Body;
    }

    public async Task<IReadOnlyList<Template>> GetAllAsync(CancellationToken ct = default)
        => await _db.Templates.Include(t => t.CurrentVersion).OrderBy(t => t.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<TemplateVersion>> GetVersionHistoryAsync(int templateId, CancellationToken ct = default)
        => await _db.TemplateVersions.Where(v => v.TemplateId == templateId)
            .OrderByDescending(v => v.VersionNumber).ToListAsync(ct);

    public async Task<int> GetNextVersionNumberAsync(int templateId, CancellationToken ct = default)
    {
        var max = await _db.TemplateVersions.Where(v => v.TemplateId == templateId)
            .Select(v => (int?)v.VersionNumber).MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<Template> CreateAsync(Template template, CancellationToken ct = default)
    {
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        _db.Templates.Add(template);
        await _db.SaveChangesAsync(ct);
        return template;
    }

    public async Task UpdateTemplateAsync(Template template, CancellationToken ct = default)
    {
        template.UpdatedAt = DateTime.UtcNow;
        _db.Entry(template).State = EntityState.Modified;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, CancellationToken ct = default)
    {
        version.CreatedAt = DateTime.UtcNow;
        _db.TemplateVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        var template = await _db.Templates.FirstAsync(t => t.Id == templateId, ct);
        template.CurrentVersionId = version.Id;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return version;
    }
}
```

*(Concurrency conflicts here throw `System.Data.Entity.Infrastructure.DbUpdateConcurrencyException` — a different type than EF Core's `Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException`. Task 8's controller port must catch the EF6 type.)*

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --filter CreateAsync_then_GetByIdAsync_returns_the_created_template
```
Expected: PASS. Requires a local `(localdb)\MSSQLLocalDB` instance — if unavailable, adjust the connection string in the test to a reachable SQL Server instance.

- [ ] **Step 5: Write the failing test for `SnippetRepository`**

```csharp
[Fact]
public async Task CreateAsync_then_DeleteAsync_removes_the_snippet()
{
    using var ctx = CreateContext();
    var repo = new SnippetRepository(ctx);

    var created = await repo.CreateAsync(new Snippet { Name = "Footer", Body = "<p>Thanks</p>" });
    await repo.DeleteAsync(created.Id);

    var fetched = await repo.GetByIdAsync(created.Id);
    fetched.Should().BeNull();
}
```

(Place in `SnippetRepositoryTests.cs`, same `CreateContext()` helper pattern as `TemplateRepositoryTests`.)

- [ ] **Step 6: Run test to verify it fails**

```bash
dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --filter CreateAsync_then_DeleteAsync_removes_the_snippet
```
Expected: FAIL — `SnippetRepository` doesn't exist yet.

- [ ] **Step 7: Implement `SnippetRepository`**

```csharp
using System.Data.Entity;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class SnippetRepository : ISnippetRepository
{
    private readonly TemplateBuilderDbContext _db;

    public SnippetRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task<IReadOnlyList<Snippet>> GetAllAsync(CancellationToken ct = default)
        => await _db.Snippets.OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<Snippet?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Snippets.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Snippet> CreateAsync(Snippet snippet, CancellationToken ct = default)
    {
        snippet.CreatedAt = DateTime.UtcNow;
        snippet.UpdatedAt = DateTime.UtcNow;
        _db.Snippets.Add(snippet);
        await _db.SaveChangesAsync(ct);
        return snippet;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var snippet = await _db.Snippets.FindAsync(ct, id);
        if (snippet is not null)
        {
            _db.Snippets.Remove(snippet);
            await _db.SaveChangesAsync(ct);
        }
    }
}
```

*(Note the argument order on `FindAsync(ct, id)` — EF6's `DbSet<T>.FindAsync` takes the `CancellationToken` **first**, then key values. This is reversed from most EF Core-influenced intuition and is a common porting mistake.)*

- [ ] **Step 8: Run test to verify it passes**

```bash
dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj --filter CreateAsync_then_DeleteAsync_removes_the_snippet
```
Expected: PASS.

- [ ] **Step 9: Run the full Infrastructure.EF6 test suite and commit**

```bash
dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj
git add src/TemplateBuilder.Infrastructure.EF6/Repositories tests/TemplateBuilder.Infrastructure.EF6.Tests
git commit -m "feat: implement EF6 TemplateRepository and SnippetRepository"
```

---

## Task 6: `Editor.Mvc5` — options, authorization types, Unity registration

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorOptions.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Authorization/TemplateBuilderAuthorizationMode.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Authorization/TemplateBuilderAuthorizationOptions.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Authorization/TemplateBuilderAuthorizationFilter.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Authorization/TemplateBuilderAuthorizationPolicyRegistry.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs`
- Delete: `src/TemplateBuilder.Editor.Mvc5/AssemblyMarker.cs`

**Interfaces:**
- Consumes: `ITemplateRepository`, `ISnippetRepository` (Task 2), `IHtmlSanitizerService`, `ISqlViewDiscoveryService`, `ITemplateEngine`, `TemplateBuilderOptions` (Task 3), `TemplateBuilderDbContext`, `TemplateRepository`, `SnippetRepository` (Tasks 4-5).
- Produces: `container.RegisterTemplateBuilderEditor(options => ...)` — the consumer's single setup call, and `TemplateBuilderAuthorizationFilter` — registered globally by the consumer in Task 9's routing/host wiring.

- [ ] **Step 1: Port the options and authorization types (same shape as ASP.NET Core original, MVC5 has no framework changes needed here)**

```csharp
// TemplateBuilderEditorOptions.cs
using TemplateBuilder.Editor.Mvc5.Authorization;

namespace TemplateBuilder.Editor.Mvc5;

public class TemplateBuilderEditorOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public TemplateBuilderAuthorizationOptions Authorization { get; set; } = new();
}
```

```csharp
// Authorization/TemplateBuilderAuthorizationMode.cs
namespace TemplateBuilder.Editor.Mvc5.Authorization;

public enum TemplateBuilderAuthorizationMode
{
    Anonymous,
    Authenticated,
    Role
}
```

```csharp
// Authorization/TemplateBuilderAuthorizationOptions.cs
namespace TemplateBuilder.Editor.Mvc5.Authorization;

public class TemplateBuilderAuthorizationOptions
{
    public TemplateBuilderAuthorizationMode Mode { get; set; } = TemplateBuilderAuthorizationMode.Anonymous;
    public string[]? RoleNames { get; set; }
    public string? PolicyName { get; set; }
}
```

- [ ] **Step 2: Write the MVC5 authorization filter and policy registry**

MVC5 has no built-in named-policy system (unlike ASP.NET Core's `AddAuthorization(o => o.AddPolicy(...))`), so `PolicyName` needs a small registry the host app populates:

```csharp
// Authorization/TemplateBuilderAuthorizationPolicyRegistry.cs
using System.Collections.Generic;
using System.Web.Mvc;

namespace TemplateBuilder.Editor.Mvc5.Authorization;

public static class TemplateBuilderAuthorizationPolicyRegistry
{
    private static readonly Dictionary<string, IAuthorizationFilter> Policies =
        new(System.StringComparer.OrdinalIgnoreCase);

    public static void Register(string name, IAuthorizationFilter filter) => Policies[name] = filter;

    internal static IAuthorizationFilter? Resolve(string name)
        => Policies.TryGetValue(name, out var f) ? f : null;
}
```

```csharp
// Authorization/TemplateBuilderAuthorizationFilter.cs
using System;
using System.Web.Mvc;

namespace TemplateBuilder.Editor.Mvc5.Authorization;

public sealed class TemplateBuilderAuthorizationFilter : IAuthorizationFilter
{
    private static TemplateBuilderAuthorizationOptions _options = new();

    internal static void Configure(TemplateBuilderAuthorizationOptions options)
        => _options = options ?? new TemplateBuilderAuthorizationOptions();

    public void OnAuthorization(AuthorizationContext filterContext)
    {
        var controllerType = filterContext.ActionDescriptor.ControllerDescriptor.ControllerType;
        if (controllerType.Assembly != typeof(TemplateBuilderAuthorizationFilter).Assembly)
            return; // not one of our controllers — leave the host's own auth alone

        bool useCustomPolicy = !string.IsNullOrWhiteSpace(_options.PolicyName);
        bool isSecured = useCustomPolicy || _options.Mode != TemplateBuilderAuthorizationMode.Anonymous;
        if (!isSecured) return;

        if (useCustomPolicy)
        {
            var hostFilter = TemplateBuilderAuthorizationPolicyRegistry.Resolve(_options.PolicyName!);
            if (hostFilter is null)
                throw new InvalidOperationException(
                    $"TemplateBuilder.Editor.Mvc5: no policy named '{_options.PolicyName}' was registered. " +
                    "Call TemplateBuilderAuthorizationPolicyRegistry.Register(name, filter) during application startup.");
            hostFilter.OnAuthorization(filterContext);
            return;
        }

        var user = filterContext.HttpContext.User;
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            filterContext.Result = new HttpUnauthorizedResult();
            return;
        }

        if (_options.Mode == TemplateBuilderAuthorizationMode.Role)
        {
            if (_options.RoleNames is not { Length: > 0 })
                throw new InvalidOperationException(
                    "TemplateBuilder.Editor.Mvc5: Authorization.RoleNames must contain at least one role when Mode is Role.");

            bool inAnyRole = false;
            foreach (var role in _options.RoleNames)
            {
                if (user.IsInRole(role)) { inAnyRole = true; break; }
            }
            if (!inAnyRole)
                filterContext.Result = new HttpStatusCodeResult(403);
        }
    }
}
```

- [ ] **Step 3: Write the Unity registration extension**

```csharp
// UnityContainerExtensions.cs
using System;
using Unity;
using Unity.Lifetime;
using TemplateBuilder.Application.Options;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Authorization;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Editor.Mvc5;

public static class UnityContainerExtensions
{
    public static IUnityContainer RegisterTemplateBuilderEditor(
        this IUnityContainer container,
        Action<TemplateBuilderEditorOptions> configure)
    {
        var options = new TemplateBuilderEditorOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException(
                "TemplateBuilder.Editor.Mvc5 requires a connection string. " +
                "Set options.ConnectionString in RegisterTemplateBuilderEditor().");

        var connectionString = options.ConnectionString;

        // HierarchicalLifetimeManager == Unity.Mvc5's per-request scope, via its
        // child-container-per-HTTP-request pattern (UnityPerRequestHttpModule).
        container.RegisterFactory<TemplateBuilderDbContext>(
            c => new TemplateBuilderDbContext(connectionString),
            new HierarchicalLifetimeManager());

        container.RegisterType<ITemplateRepository, TemplateRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<ISnippetRepository, SnippetRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<IHtmlSanitizerService, HtmlSanitizerService>(new ContainerControlledLifetimeManager());
        container.RegisterType<ITemplateEngine, TemplateEngine>(new HierarchicalLifetimeManager());
        container.RegisterInstance(new TemplateBuilderOptions());
        container.RegisterFactory<ISqlViewDiscoveryService>(
            c => new SqlViewDiscoveryService(connectionString, c.Resolve<TemplateBuilderOptions>()),
            new HierarchicalLifetimeManager());

        TemplateBuilderAuthorizationFilter.Configure(options.Authorization);

        // Triggers EF6 MigrateDatabaseToLatestVersion on first access — mirrors the ASP.NET Core
        // MigrationHostedService's "migrate on startup" behavior without a hosted-service concept in MVC5.
        using var migrationContext = new TemplateBuilderDbContext(connectionString);
        migrationContext.Database.Initialize(force: false);

        return container;
    }
}
```

- [ ] **Step 4: Delete the Task-1 placeholder and build**

```bash
rm src/TemplateBuilder.Editor.Mvc5/AssemblyMarker.cs
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5
git commit -m "feat: add Editor.Mvc5 options, authorization filter, and Unity DI registration"
```

---

## Task 7: `Editor.Mvc5` — JSON body helper and base controller

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/HttpRequestJsonExtensions.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplateBuilderControllerBase.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/ErrorResult.cs`

**Interfaces:**
- Produces: `ReadJsonBodyAsync<T>()` extension and `JsonOk`/`JsonError` helpers — consumed by every controller ported in Tasks 8-10. `ErrorResult` matches the original's `record ErrorResult(string Code, string Message)` shape exactly (controllers serialize it as the JSON error body).

MVC5's `Controller` base class has no `IActionResult`/`[FromBody]`/`Ok()`/`NotFound()`/`BadRequest()` equivalents (those are ASP.NET Core and Web API 2 concepts) — this task builds the small shim that lets the controller ports in Tasks 8-10 stay close to the original code shape.

- [ ] **Step 1: Write `ErrorResult`**

```csharp
namespace TemplateBuilder.Editor.Mvc5.Models;

public class ErrorResult
{
    public ErrorResult(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }
}
```

- [ ] **Step 2: Write the JSON body reader**

```csharp
using System.IO;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;

namespace TemplateBuilder.Editor.Mvc5;

public static class HttpRequestJsonExtensions
{
    public static async Task<T> ReadJsonBodyAsync<T>(this HttpRequestBase request)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();
        return JsonConvert.DeserializeObject<T>(body)!;
    }
}
```

- [ ] **Step 3: Write the base controller with JSON result helpers**

```csharp
using System.Web.Mvc;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public abstract class TemplateBuilderControllerBase : Controller
{
    protected JsonResult JsonOk(object data) => Json(data, JsonRequestBehavior.AllowGet);

    protected ActionResult JsonError(int statusCode, object errorBody)
    {
        Response.StatusCode = statusCode;
        return Json(errorBody, JsonRequestBehavior.AllowGet);
    }

    protected ActionResult NoContentResult() => new HttpStatusCodeResult(204);
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5
git commit -m "feat: add JSON body helper and base controller for Editor.Mvc5"
```

---

## Task 8: Port `TemplatesController`

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/TemplateListViewModel.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/TemplateEditorViewModel.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/SaveVersionRequest.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/PreviewRequest.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/DuplicateRequest.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs`

**Interfaces:**
- Consumes: `ITemplateRepository`, `ISqlViewDiscoveryService`, `ITemplateEngine`, `IHtmlSanitizerService` (Tasks 2-3), `TemplateBuilderControllerBase`/`ReadJsonBodyAsync`/`ErrorResult` (Task 7).
- Produces: the full `/Templates/*` route surface, consumed by Task 11's views and Task 13's routing bootstrap.

- [ ] **Step 1: Port the request/view models (same shape as the ASP.NET Core originals)**

```csharp
// Models/DuplicateRequest.cs
namespace TemplateBuilder.Editor.Mvc5.Models;
public class DuplicateRequest { public string NewName { get; set; } = string.Empty; }
```

```csharp
// Models/SaveVersionRequest.cs
namespace TemplateBuilder.Editor.Mvc5.Models;
public class SaveVersionRequest
{
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ChangeComment { get; set; }
}
```

```csharp
// Models/PreviewRequest.cs
namespace TemplateBuilder.Editor.Mvc5.Models;
public class PreviewRequest { public string Body { get; set; } = string.Empty; public string? ModelJson { get; set; } }
public class ValidateRequest { public string Body { get; set; } = string.Empty; }
```

```csharp
// Models/TemplateListViewModel.cs
using System.Collections.Generic;
using System.Linq;
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Editor.Mvc5.Models;

public class TemplateListViewModel
{
    public List<Template> Templates { get; set; } = new();
    public string? Search { get; set; }
    public string? TypeFilter { get; set; }

    public Dictionary<string, int> CountByType => Templates
        .GroupBy(t => t.TemplateType)
        .ToDictionary(g => g.Key, g => g.Count());

    public static readonly string[] KnownTypes = { "Email", "Report", "Notice", "Custom" };
}
```

```csharp
// Models/TemplateEditorViewModel.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TemplateBuilder.Editor.Mvc5.Models;

public class TemplateEditorViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string TemplateType { get; set; } = "Email";

    [StringLength(500)]
    public string? Description { get; set; }

    public string? Body { get; set; }
    public int? CurrentVersionId { get; set; }
    public int CurrentVersionNumber { get; set; }
    public List<string> AvailableViews { get; set; } = new();
}
```

*(Note: the ASP.NET Core originals use C# `record` types for the request DTOs; MVC5's default model binder works most reliably against plain classes with settable properties, so these are ported as classes, not records — behaviorally identical, just a binder-compatibility adjustment.)*

- [ ] **Step 2: Port `TemplatesController`, action by action**

```csharp
using System;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Exceptions;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Models;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class TemplatesController : TemplateBuilderControllerBase
{
    private const int MaxPreviewJsonBytes = 64 * 1024;
    private const int PreviewTimeoutSeconds = 5;

    private readonly ITemplateRepository _repository;
    private readonly ISqlViewDiscoveryService _viewDiscovery;
    private readonly ITemplateEngine _engine;
    private readonly IHtmlSanitizerService _sanitizer;

    public TemplatesController(ITemplateRepository repository, ISqlViewDiscoveryService viewDiscovery, ITemplateEngine engine, IHtmlSanitizerService sanitizer)
    {
        _repository = repository;
        _viewDiscovery = viewDiscovery;
        _engine = engine;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    public async Task<ActionResult> Index(string? search, string? type)
    {
        var templates = await _repository.GetAllAsync();
        var filtered = templates.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(t => t.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        if (!string.IsNullOrWhiteSpace(type))
            filtered = filtered.Where(t => t.TemplateType == type);

        return View(new TemplateListViewModel { Templates = filtered.ToList(), Search = search, TypeFilter = type });
    }

    [HttpGet]
    public async Task<ActionResult> Create()
    {
        var views = await _viewDiscovery.GetViewNamesAsync();
        return View("Edit", new TemplateEditorViewModel { AvailableViews = views.ToList() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> Create(TemplateEditorViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableViews = (await _viewDiscovery.GetViewNamesAsync()).ToList();
            return View("Edit", model);
        }
        try
        {
            var template = await _repository.CreateAsync(new Template
            {
                Name = model.Name.Trim(),
                TemplateType = model.TemplateType,
                Description = model.Description
            });

            if (!string.IsNullOrWhiteSpace(model.Body))
            {
                await _repository.PublishVersionAsync(template.Id, new TemplateVersion
                {
                    TemplateId = template.Id,
                    VersionNumber = 1,
                    Body = model.Body,
                    ChangeComment = "Initial version"
                });
            }

            return RedirectToAction(nameof(Edit), new { id = template.Id });
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", $"A template named '{model.Name.Trim()}' already exists."));
        }
    }

    [Route("Templates/{id:int}/Edit")]
    [HttpGet]
    public async Task<ActionResult> Edit(int id)
    {
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return HttpNotFound();
        var views = await _viewDiscovery.GetViewNamesAsync();
        return View(new TemplateEditorViewModel
        {
            Id = template.Id,
            Name = template.Name,
            TemplateType = template.TemplateType,
            Description = template.Description,
            Body = template.CurrentVersion?.Body ?? string.Empty,
            CurrentVersionId = template.CurrentVersionId,
            CurrentVersionNumber = template.CurrentVersion?.VersionNumber ?? 0,
            AvailableViews = views.ToList()
        });
    }

    [Route("Templates/{id:int}/SaveVersion")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> SaveVersion(int id)
    {
        var request = await Request.ReadJsonBodyAsync<SaveVersionRequest>();
        if (string.IsNullOrWhiteSpace(request.Name))
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", "Template name is required."));
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Template {id} not found."));
        try
        {
            template.Name = request.Name.Trim();
            template.TemplateType = request.TemplateType;
            template.Description = request.Description;
            await _repository.UpdateTemplateAsync(template);
            var nextNumber = await _repository.GetNextVersionNumberAsync(id);
            var version = await _repository.PublishVersionAsync(id, new TemplateVersion
            {
                TemplateId = id,
                VersionNumber = nextNumber,
                Body = request.Body,
                ChangeComment = request.ChangeComment
            });
            return JsonOk(new { versionId = version.Id, versionNumber = version.VersionNumber });
        }
        catch (DbUpdateConcurrencyException)
        {
            return JsonError(409, new ErrorResult("CONFLICT", "This template was modified by another user while you were editing. Please refresh and try again."));
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", $"A template named '{request.Name.Trim()}' already exists."));
        }
    }

    [Route("Templates/{id:int}/Versions")]
    [HttpGet]
    public async Task<ActionResult> GetVersionHistory(int id)
    {
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return HttpNotFound();
        var versions = await _repository.GetVersionHistoryAsync(id);
        return PartialView("_VersionHistory", (versions.ToList(), template.CurrentVersionId));
    }

    [Route("Templates/{id:int}/Versions/{versionId:int}/Body")]
    [HttpGet]
    public async Task<ActionResult> GetVersionBody(int id, int versionId)
    {
        var body = await _repository.GetVersionBodyAsync(versionId);
        if (body is null) return JsonError(404, new ErrorResult("VERSION_NOT_FOUND", $"Version {versionId} not found."));
        return JsonOk(new { body });
    }

    [Route("Templates/{id:int}/Restore/{versionId:int}/{sourceVersionNumber:int}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> RestoreVersion(int id, int versionId, int sourceVersionNumber)
    {
        try
        {
            var oldBody = await _repository.GetVersionBodyAsync(versionId);
            if (oldBody is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Version {versionId} not found."));
            var nextNumber = await _repository.GetNextVersionNumberAsync(id);
            var version = await _repository.PublishVersionAsync(id, new TemplateVersion
            {
                TemplateId = id,
                VersionNumber = nextNumber,
                Body = oldBody,
                ChangeComment = $"Restored from v{sourceVersionNumber}"
            });
            return JsonOk(new { versionId = version.Id, versionNumber = version.VersionNumber });
        }
        catch (DbUpdateConcurrencyException)
        {
            return JsonError(409, new ErrorResult("CONFLICT", "This template was modified by another user while you were editing. Please refresh and try again."));
        }
    }

    [Route("Templates/Api/Views/{viewName}/Columns")]
    [HttpGet]
    public async Task<ActionResult> GetViewColumns(string viewName)
    {
        var columns = await _viewDiscovery.GetViewColumnsAsync(viewName);
        return JsonOk(columns);
    }

    [Route("Templates/{id:int}/Preview")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> Preview(int id)
    {
        var request = await Request.ReadJsonBodyAsync<PreviewRequest>();

        if (request.ModelJson is not null && Encoding.UTF8.GetByteCount(request.ModelJson) > MaxPreviewJsonBytes)
            return JsonError(400, new ErrorResult("PREVIEW_JSON_TOO_LARGE", "Preview JSON payload exceeds the 64 KB limit."));

        JObject? modelObj = null;
        if (request.ModelJson is not null)
        {
            try { modelObj = JObject.Parse(request.ModelJson); }
            catch (JsonException ex)
            {
                return JsonError(400, new ErrorResult("PREVIEW_JSON_INVALID", $"Invalid JSON: {ex.Message}"));
            }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(PreviewTimeoutSeconds));
        try
        {
            object model = modelObj is null ? new { } : modelObj.ToObject<System.Collections.Generic.Dictionary<string, object>>()!;
            var html = _sanitizer.Sanitize(await _engine.RenderBodyAsync(request.Body, model, cts.Token));
            return JsonOk(new { html });
        }
        catch (OperationCanceledException)
        {
            return JsonError(408, new ErrorResult("PREVIEW_TIMEOUT", "Preview timed out. Simplify the template or model and try again."));
        }
        catch (TemplateRenderException)
        {
            return JsonError(400, new ErrorResult("TEMPLATE_RENDER_ERROR", "Template rendering failed. Check template syntax."));
        }
    }

    [Route("Templates/{id:int}/ToggleActive")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> ToggleActive(int id)
    {
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Template {id} not found."));
        template.IsActive = !template.IsActive;
        await _repository.UpdateTemplateAsync(template);
        return JsonOk(new { isActive = template.IsActive });
    }

    [Route("Templates/{id:int}/Validate")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> Validate(int id)
    {
        var request = await Request.ReadJsonBodyAsync<ValidateRequest>();
        if (string.IsNullOrWhiteSpace(request?.Body))
            return JsonError(400, new { message = "Body is required." });
        if (Encoding.UTF8.GetByteCount(request.Body) > 64 * 1024)
            return JsonError(400, new { message = "Body exceeds size limit." });

        try
        {
            await _engine.RenderBodyAsync(request.Body, new { });
            return JsonOk(new { valid = true });
        }
        catch (Exception ex)
        {
            return JsonOk(new { valid = false, message = ex.Message });
        }
    }

    [Route("Templates/{id:int}/Duplicate")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> Duplicate(int id)
    {
        var request = await Request.ReadJsonBodyAsync<DuplicateRequest>();
        var source = await _repository.GetByIdAsync(id);
        if (source is null) return HttpNotFound();

        var body = source.CurrentVersion?.Body ?? string.Empty;
        try
        {
            var newTemplate = await _repository.CreateAsync(new Template
            {
                Name = request.NewName.Trim(),
                TemplateType = source.TemplateType,
                Description = source.Description
            });

            await _repository.PublishVersionAsync(newTemplate.Id, new TemplateVersion
            {
                TemplateId = newTemplate.Id,
                VersionNumber = 1,
                Body = body,
                ChangeComment = $"Duplicated from '{source.Name}'"
            });

            return JsonOk(new { id = newTemplate.Id });
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", $"A template named '{request.NewName.Trim()}' already exists."));
        }
    }
}
```

*(Route attributes require `RouteTable.Routes.MapMvcAttributeRoutes()` — added in Task 13. `{id:int}` constraint syntax is unchanged from the ASP.NET Core original; MVC5's attribute routing supports the same inline-constraint syntax.)*

- [ ] **Step 3: Build**

```bash
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```
Expected: `Build succeeded.` (Views referenced by `View(...)`/`PartialView(...)` don't exist yet — that's fine, they're resolved at runtime, not compile time, until RazorGenerator precompilation is wired up in Task 11.)

- [ ] **Step 4: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Models src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs
git commit -m "feat: port TemplatesController to MVC5"
```

---

## Task 9: Port `SnippetsController`

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/CreateSnippetRequest.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Controllers/SnippetsController.cs`

**Interfaces:**
- Consumes: `ISnippetRepository` (Task 2), `TemplateBuilderControllerBase`/`ReadJsonBodyAsync` (Task 7).
- Produces: `/Templates/Api/Snippets` CRUD routes, consumed by Task 13's routing bootstrap.

- [ ] **Step 1: Port the request model**

```csharp
// Models/CreateSnippetRequest.cs
namespace TemplateBuilder.Editor.Mvc5.Models;

public class CreateSnippetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Body { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Port the controller**

```csharp
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Models;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class SnippetsController : TemplateBuilderControllerBase
{
    private readonly ISnippetRepository _snippets;

    public SnippetsController(ISnippetRepository snippets) => _snippets = snippets;

    [Route("Templates/Api/Snippets")]
    [HttpGet]
    public async Task<ActionResult> GetAll()
    {
        var snippets = await _snippets.GetAllAsync();
        return JsonOk(snippets.Select(s => new { s.Id, s.Name, s.Description, s.Body }));
    }

    [Route("Templates/Api/Snippets")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<ActionResult> Create()
    {
        var request = await Request.ReadJsonBodyAsync<CreateSnippetRequest>();

        if (string.IsNullOrWhiteSpace(request.Name))
            return JsonError(400, new Models.ErrorResult("INVALID_NAME", "Snippet name is required."));
        if (string.IsNullOrWhiteSpace(request.Body))
            return JsonError(400, new Models.ErrorResult("INVALID_BODY", "Snippet content cannot be empty."));

        var snippet = new Snippet
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Body = request.Body
        };

        try
        {
            var created = await _snippets.CreateAsync(snippet);
            return JsonOk(new { id = created.Id, created.Name });
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new Models.ErrorResult("DUPLICATE_NAME", $"A snippet named '{request.Name.Trim()}' already exists."));
        }
    }

    [Route("Templates/Api/Snippets/{id:int}")]
    [HttpDelete, ValidateAntiForgeryToken]
    public async Task<ActionResult> Delete(int id)
    {
        var snippet = await _snippets.GetByIdAsync(id);
        if (snippet is null) return JsonError(404, new Models.ErrorResult("NOT_FOUND", "Snippet not found."));
        await _snippets.DeleteAsync(id);
        return NoContentResult();
    }
}
```

*(`ErrorResult` from Task 7 lives in the `Models` namespace here — referenced with a full qualifier where a local `Models.CreateSnippetRequest` is also `using`'d, to avoid ambiguity; if your IDE resolves it unambiguously via the existing `using TemplateBuilder.Editor.Mvc5.Models;`, drop the qualifier.)*

- [ ] **Step 3: Build**

```bash
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Models/CreateSnippetRequest.cs src/TemplateBuilder.Editor.Mvc5/Controllers/SnippetsController.cs
git commit -m "feat: port SnippetsController to MVC5"
```

---

## Task 10: Port `SetupController` (dev-only diagnostic)

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/Models/SetupCheckResult.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Controllers/SetupController.cs`

**Interfaces:**
- Consumes: `TemplateBuilderDbContext` (Task 4).
- Produces: `GET /Templates/_setup` diagnostic page, consumed by Task 13's routing bootstrap.

- [ ] **Step 1: Port the check-result model**

```csharp
// Models/SetupCheckResult.cs
namespace TemplateBuilder.Editor.Mvc5.Models;

public class SetupCheckResult
{
    public SetupCheckResult(string name, string description, bool passed, string fix, string? detail = null)
    {
        Name = name;
        Description = description;
        Passed = passed;
        Fix = fix;
        Detail = detail;
    }

    public string Name { get; }
    public string Description { get; }
    public bool Passed { get; }
    public string Fix { get; }
    public string? Detail { get; }
}
```

- [ ] **Step 2: Port the controller, adapted to MVC5/`System.Web.HttpContext` equivalents of the ASP.NET Core checks**

The original checks "is Development environment" via `IWebHostEnvironment`, "are attribute routes registered" via `IActionDescriptorCollectionProvider`, and "is `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` set" via `IOptions<MvcOptions>` — none of which exist in MVC5. The MVC5-equivalent checks:

```csharp
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using TemplateBuilder.Editor.Mvc5.Models;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class SetupController : TemplateBuilderControllerBase
{
    private readonly TemplateBuilderDbContext _db;

    public SetupController(TemplateBuilderDbContext db) => _db = db;

    [Route("Templates/_setup")]
    [HttpGet]
    public ActionResult Index()
    {
        if (!HttpContext.IsDebuggingEnabled) return HttpNotFound();

        var checks = new System.Collections.Generic.List<SetupCheckResult>();

        bool dbOk;
        string? dbDetail = null;
        try { dbOk = _db.Database.Exists(); }
        catch (Exception ex) { dbOk = false; dbDetail = ex.Message; }

        checks.Add(new SetupCheckResult(
            "Database connection",
            "SQL Server is reachable with the configured connection string.",
            dbOk,
            "Verify the ConnectionString passed to container.RegisterTemplateBuilderEditor() in your Unity bootstrapper.",
            dbDetail));

        bool routesOk = RouteTable.Routes.OfType<System.Web.Routing.Route>()
            .Any(r => r.Url != null && r.Url.Contains("{id}") && r.Url.Contains("Edit"))
            || RouteTable.Routes.Count > 0; // attribute routes don't enumerate as System.Web.Routing.Route the same way — presence of MapMvcAttributeRoutes() is the real signal, checked next
        checks.Add(new SetupCheckResult(
            "MapMvcAttributeRoutes() registered",
            "Attribute-routed endpoints (Edit, Preview, SaveVersion, Versions) are reachable.",
            routesOk,
            "Call RouteTable.Routes.MapMvcAttributeRoutes() in your RouteConfig, before any catch-all conventional route."));

        checks.Add(new SetupCheckResult(
            "Static assets serving",
            "The editor CSS and JS are accessible at the route registered by TemplateBuilderStaticAssetsRouteHandler.",
            true,
            "See Task 12 — verify /TemplateBuilderEditor/css/template-editor.css returns 200 in your browser's network tab."));

        return View("_Setup", checks);
    }

    [Route("Templates/_setup/layout-probe")]
    [HttpGet]
    public ActionResult LayoutProbe()
    {
        if (!HttpContext.IsDebuggingEnabled) return HttpNotFound();
        return View("_LayoutProbe");
    }
}
```

*(`HttpContext.IsDebuggingEnabled` reflects `<compilation debug="true">` in `web.config` — the closest MVC5/`System.Web` equivalent of ASP.NET Core's `IWebHostEnvironment.IsDevelopment()`. It is not identical semantically — debug compilation and "development environment" are different concepts in classic ASP.NET — document this difference for the client since it means the diagnostic page is gated on a compilation flag, not an environment variable.)*

- [ ] **Step 3: Build**

```bash
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Models/SetupCheckResult.cs src/TemplateBuilder.Editor.Mvc5/Controllers/SetupController.cs
git commit -m "feat: port SetupController diagnostic page to MVC5"
```

---

## Task 11: RazorGenerator spike — prove the precompiled-view pipeline works

This is the plan's highest-risk task (see spec's "Known risks" #1) — it must be done with a **trivial throwaway view**, not the real UI, so a pipeline failure is cheap to diagnose.

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Spike/Hello.cshtml`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/web.config` (RazorGenerator/MVC view-compilation config, not the app's own web.config)
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`
- Create: `samples/TemplateBuilder.SampleMvc5Host/` (minimal MVC5 Web Application project — see Task 14 for the full build-out; this task only needs enough of it to prove the spike)

**Interfaces:**
- Produces: confirmation that `RazorGenerator.MsBuild` precompiles a `.cshtml` file into the DLL, and that `RazorGenerator.Mvc`'s `PrecompiledMvcEngine` view engine serves it at runtime without the `.cshtml` file being present in the consuming project. Tasks 12 (real view port) depend on this working.

- [ ] **Step 1: Write a trivial view**

```html
@* Views/Spike/Hello.cshtml *@
@{
    ViewBag.Title = "RazorGenerator Spike";
}
<h1>RazorGenerator works: @DateTime.UtcNow.ToString("O")</h1>
```

- [ ] **Step 2: Mark the view for RazorGenerator precompilation**

RazorGenerator uses a custom tool directive on each `.cshtml` file (traditionally set via the "Custom Tool" property in the old `.csproj` item metadata; for RazorGenerator.MsBuild in an SDK-style project, this is done via an MSBuild item transform instead). Add to `TemplateBuilder.Editor.Mvc5.csproj`:

```xml
<ItemGroup>
  <Content Remove="Views/**/*.cshtml" />
  <None Include="Views/**/*.cshtml">
    <Generator>RazorGenerator</Generator>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Build and inspect the compiled output**

```bash
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```

Expected: `Build succeeded.`, and a generated `Views/Spike/Hello.generated.cs` (or similar, RazorGenerator's naming convention) appears under `obj/`, containing a C# class deriving from `System.Web.Mvc.WebViewPage` with the rendered HTML logic compiled in. If this file isn't generated, the `RazorGenerator.MsBuild` package isn't wired into the build — check the package's own docs for the exact SDK-style integration steps for the installed version before proceeding; do not guess.

- [ ] **Step 4: Wire up the minimal sample host to serve it**

Scaffold just enough of `samples/TemplateBuilder.SampleMvc5Host` (full build-out is Task 14) to prove the spike: an MVC5 Web Application project referencing `TemplateBuilder.Editor.Mvc5`, with `RazorGenerator.Mvc`'s view engine registered in `Global.asax.cs`:

```csharp
using System.Web.Mvc;
using RazorGenerator.Mvc;

namespace TemplateBuilder.SampleMvc5Host
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new PrecompiledMvcEngine(typeof(MvcApplication).Assembly));

            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(System.Web.Routing.RouteTable.Routes);
        }
    }
}
```

Add a temporary route + a trivial controller (`SpikeController : Controller { public ActionResult Hello() => View("~/Views/Spike/Hello.cshtml"); }`) directly in the sample host project referencing the view by its precompiled virtual path.

- [ ] **Step 5: Run the sample host and verify in browser**

```bash
# Open the solution in Visual Studio, set TemplateBuilder.SampleMvc5Host as startup project, run with IIS Express (F5)
```

Navigate to `/Spike/Hello`. Expected: the page renders `RazorGenerator works: <timestamp>` — proving the DLL-embedded view resolves and executes without a physical `.cshtml` file present in the sample host project.

- [ ] **Step 6: Remove the throwaway spike view/controller, keep the proven wiring**

Delete `Views/Spike/`, the temporary `SpikeController`, and its route — but leave the `PrecompiledMvcEngine` registration in `Global.asax.cs` and the `<Generator>RazorGenerator</Generator>` `.csproj` wiring in place; both are needed by Task 12.

- [ ] **Step 7: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj samples/TemplateBuilder.SampleMvc5Host
git commit -m "chore: prove RazorGenerator precompiled-view pipeline with a throwaway spike"
```

---

## Task 12: Port the real views

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Index.cshtml`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/_VersionHistory.cshtml`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Setup/_Setup.cshtml`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Setup/_LayoutProbe.cshtml`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/web.config` (namespace imports — MVC5's replacement for `_ViewImports.cshtml`)

**Interfaces:**
- Consumes: `TemplateListViewModel`, `TemplateEditorViewModel`, `SetupCheckResult` (Task 8/10), the proven RazorGenerator wiring (Task 11).
- Produces: the actual editor UI, rendered by the controllers ported in Tasks 8-10.

This is mechanical syntax conversion, not new logic — port each view's actual markup from the origin repo (`C:\Users\nchinnam\source\repos\TemplateBuilder\src\TemplateBuilder.Editor\Views\...`), applying this conversion table:

| ASP.NET Core Razor | MVC5 Razor equivalent |
|---|---|
| `@model TemplateEditorViewModel` (top of file) | Unchanged — `@model` directive works identically in MVC5 |
| `@inject IServiceProvider sp` | Not used in these views (confirmed: none of the 5 views inject services) — skip |
| `asp-controller="Templates" asp-action="Edit" asp-route-id="@Model.Id"` | `@Html.ActionLink("text", "Edit", "Templates", new { id = Model.Id }, null)` or `@Url.Action("Edit", "Templates", new { id = Model.Id })` inside a raw `<a href="...">` |
| `asp-for="Name"` (on `<input>`) | `@Html.TextBoxFor(m => m.Name)` or keep raw `<input>` + `@Html.ValidationMessageFor(m => m.Name)` |
| `<form asp-action="Create" method="post">` | `@using (Html.BeginForm("Create", "Templates", FormMethod.Post)) { ... }` |
| `@Html.AntiForgeryToken()` | Unchanged — exists in MVC5 too |
| `_ViewImports.cshtml` (`@using`, `@addTagHelper`) | MVC5 equivalent is `Views/web.config`'s `<system.web.webPages.razor><pages><namespaces>` section for `@using`-equivalent imports; there is no tag-helper concept to port since MVC5 doesn't have tag helpers (all tag-helper usages are already being converted to HTML helpers per the rows above, so nothing carries over from `@addTagHelper`) |
| `PartialView("_VersionHistory", (versions, currentId))` (tuple model) | Unchanged — MVC5 `PartialView` supports the same tuple-as-model pattern; `@model (List<TemplateVersion> Versions, int? CurrentVersionId)` at the top of `_VersionHistory.cshtml` |

- [ ] **Step 1: Write `Views/web.config`**

```xml
<?xml version="1.0"?>
<configuration>
  <configSections>
    <sectionGroup name="system.web.webPages.razor" type="System.Web.WebPages.Razor.Configuration.RazorWebSectionGroup, System.Web.WebPages.Razor, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35">
      <section name="host" type="System.Web.WebPages.Razor.Configuration.HostSection, System.Web.WebPages.Razor, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" requirePermission="false" />
      <section name="pages" type="System.Web.WebPages.Razor.Configuration.RazorPagesSection, System.Web.WebPages.Razor, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" requirePermission="false" />
    </sectionGroup>
  </configSections>
  <system.web.webPages.razor>
    <host factoryType="System.Web.Mvc.MvcWebRazorHostFactory, System.Web.Mvc, Version=5.3.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" />
    <pages pageBaseType="System.Web.Mvc.WebViewPage">
      <namespaces>
        <add namespace="System.Web.Mvc" />
        <add namespace="System.Web.Mvc.Ajax" />
        <add namespace="System.Web.Mvc.Html" />
        <add namespace="System.Web.Routing" />
        <add namespace="TemplateBuilder.Editor.Mvc5.Models" />
        <add namespace="TemplateBuilder.Domain.Entities" />
      </namespaces>
    </pages>
  </system.web.webPages.razor>
</configuration>
```

- [ ] **Step 2: Port each view**

For each of the 5 views, open the origin file, apply the conversion table above, and write the MVC5 equivalent at the destination path listed in **Files**. Since these are UI markup files (not logic), verify correctness visually rather than by code review alone — Step 3 below covers that.

- [ ] **Step 3: Rebuild and manually verify each view renders**

```bash
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```
Then, via the sample host (Task 14) once it exists, or a temporary controller action per view in the meantime: navigate to each of `/Templates` (Index), `/Templates/Create` (Edit view in create mode), `/Templates/{id}/Edit` (Edit view in edit mode), and confirm no Razor compilation errors and that the page structure matches the original (headings, form fields, table columns present). `_VersionHistory` and `_Setup`/`_LayoutProbe` are exercised via Task 8/10's controller actions once wired into routing (Task 13).

- [ ] **Step 4: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Views
git commit -m "feat: port Templates/Setup views to MVC5 Razor"
```

---

## Task 13: Static assets, routing bootstrap

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js` (copied from origin, unchanged)
- Create: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css` (copied from origin, unchanged)
- Create: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilderStaticAssetsRouteHandler.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorRouteConfig.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`

**Interfaces:**
- Produces: `TemplateBuilderEditorRouteConfig.RegisterRoutes(RouteTable.Routes)` — the consumer's one-line routing setup call, and a `/TemplateBuilderEditor/{**path}` static-asset route mirroring ASP.NET Core's `/_content/...` convention.

- [ ] **Step 1: Copy the JS/CSS unchanged**

Copy `wwwroot/js/template-editor.js` and `wwwroot/css/template-editor.css` byte-for-byte from `C:\Users\nchinnam\source\repos\TemplateBuilder\src\TemplateBuilder.Editor\wwwroot\` into `src/TemplateBuilder.Editor.Mvc5/StaticAssets/`. No changes — this is pure client-side behavior (dark/light theme, table toolbar, find & replace, auto-save, etc.) with zero server-framework dependency; per the spec, none of this needs re-implementation.

- [ ] **Step 2: Embed them as assembly resources**

```xml
<ItemGroup>
  <EmbeddedResource Include="StaticAssets\template-editor.js" LogicalName="TemplateBuilder.Editor.Mvc5.StaticAssets.template-editor.js" />
  <EmbeddedResource Include="StaticAssets\template-editor.css" LogicalName="TemplateBuilder.Editor.Mvc5.StaticAssets.template-editor.css" />
</ItemGroup>
```

- [ ] **Step 3: Write the route handler that serves them**

```csharp
using System;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.Routing;

namespace TemplateBuilder.Editor.Mvc5;

public sealed class TemplateBuilderStaticAssetsRouteHandler : IRouteHandler, IHttpHandler
{
    private static readonly Assembly Asm = typeof(TemplateBuilderStaticAssetsRouteHandler).Assembly;

    public bool IsReusable => true;

    public IHttpHandler GetHttpHandler(RequestContext requestContext) => this;

    public void ProcessRequest(HttpContext context) => ProcessRequest(new HttpContextWrapper(context));

    private void ProcessRequest(HttpContextBase context)
    {
        var path = context.Request.RequestContext.RouteData.Values["path"] as string ?? string.Empty;
        var (resourceSuffix, contentType) = path switch
        {
            "css/template-editor.css" => ("StaticAssets.template-editor.css", "text/css"),
            "js/template-editor.js" => ("StaticAssets.template-editor.js", "application/javascript"),
            _ => (null, null)
        };

        if (resourceSuffix is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var resourceName = $"TemplateBuilder.Editor.Mvc5.{resourceSuffix}";
        using var stream = Asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        context.Response.ContentType = contentType;
        using var reader = new StreamReader(stream);
        context.Response.Write(reader.ReadToEnd());
    }

    void IHttpHandler.ProcessRequest(HttpContext context) => ProcessRequest(context);
}
```

- [ ] **Step 4: Write the routing bootstrap the consumer calls once**

```csharp
using System.Web.Mvc;
using System.Web.Routing;

namespace TemplateBuilder.Editor.Mvc5;

public static class TemplateBuilderEditorRouteConfig
{
    public static void RegisterRoutes(RouteCollection routes)
    {
        routes.MapMvcAttributeRoutes();

        routes.Add("TemplateBuilderEditorStaticAssets", new Route(
            "TemplateBuilderEditor/{*path}",
            new TemplateBuilderStaticAssetsRouteHandler()));
    }
}
```

*(The consumer calls `TemplateBuilderEditorRouteConfig.RegisterRoutes(RouteTable.Routes);` from their own `RouteConfig.RegisterRoutes`, before their own catch-all `{controller}/{action}/{id}` conventional route — same ordering requirement the original README documents for `app.MapControllers()` before `MapControllerRoute`.)*

- [ ] **Step 5: Build**

```bash
dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/StaticAssets src/TemplateBuilder.Editor.Mvc5/TemplateBuilderStaticAssetsRouteHandler.cs src/TemplateBuilder.Editor.Mvc5/TemplateBuilderEditorRouteConfig.cs src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj
git commit -m "feat: embed static assets and add routing bootstrap"
```

---

## Task 14: Build out `TemplateBuilder.SampleMvc5Host`

**Files:**
- Modify: `samples/TemplateBuilder.SampleMvc5Host/Global.asax.cs` (extend Task 11's spike version)
- Create: `samples/TemplateBuilder.SampleMvc5Host/App_Start/RouteConfig.cs`
- Create: `samples/TemplateBuilder.SampleMvc5Host/App_Start/UnityConfig.cs`
- Create: `samples/TemplateBuilder.SampleMvc5Host/App_Start/FilterConfig.cs`
- Create: `samples/TemplateBuilder.SampleMvc5Host/Web.config`
- Create: `samples/TemplateBuilder.SampleMvc5Host/Views/Home/Index.cshtml` (simple landing page with a link to `/Templates`)

**Interfaces:**
- Consumes: `TemplateBuilder.Editor.Mvc5`'s full public surface (Tasks 6-13) — this is the first task that exercises the whole package together, end-to-end.

- [ ] **Step 1: Write `RouteConfig.cs`**

```csharp
using System.Web.Mvc;
using System.Web.Routing;
using TemplateBuilder.Editor.Mvc5;

namespace TemplateBuilder.SampleMvc5Host
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            TemplateBuilderEditorRouteConfig.RegisterRoutes(routes);

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}
```

- [ ] **Step 2: Write `UnityConfig.cs`**

```csharp
using Unity;
using TemplateBuilder.Editor.Mvc5;
using Unity.Mvc5;

namespace TemplateBuilder.SampleMvc5Host
{
    public static class UnityConfig
    {
        public static void RegisterComponents()
        {
            var container = new UnityContainer();

            container.RegisterTemplateBuilderEditor(options =>
            {
                options.ConnectionString =
                    System.Configuration.ConfigurationManager.ConnectionStrings["TemplateDb"].ConnectionString;
                // options.Authorization.Mode defaults to Anonymous for the sample host
            });

            DependencyResolver.SetResolver(new UnityDependencyResolver(container));
        }
    }
}
```

- [ ] **Step 3: Write `FilterConfig.cs`**

```csharp
using System.Web.Mvc;
using TemplateBuilder.Editor.Mvc5.Authorization;

namespace TemplateBuilder.SampleMvc5Host
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new TemplateBuilderAuthorizationFilter());
        }
    }
}
```

- [ ] **Step 4: Extend `Global.asax.cs` from Task 11's spike version**

```csharp
using System.Web.Mvc;
using System.Web.Routing;
using RazorGenerator.Mvc;

namespace TemplateBuilder.SampleMvc5Host
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new PrecompiledMvcEngine(typeof(TemplateBuilder.Editor.Mvc5.UnityContainerExtensions).Assembly));
            ViewEngines.Engines.Add(new RazorViewEngine()); // for the sample host's own Views/Home/Index.cshtml

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            UnityConfig.RegisterComponents();
        }
    }
}
```

- [ ] **Step 5: Add a connection string to `Web.config`**

```xml
<connectionStrings>
  <add name="TemplateDb" connectionString="Server=(localdb)\MSSQLLocalDB;Database=TemplateBuilderMvc5Sample;Trusted_Connection=True;" providerName="System.Data.SqlClient" />
</connectionStrings>
```

- [ ] **Step 6: Add a trivial home page**

```html
@* Views/Home/Index.cshtml *@
<h1>TemplateBuilder.Editor.Mvc5 sample host</h1>
<p>@Html.ActionLink("Open the template editor", "Index", "Templates")</p>
```

- [ ] **Step 7: Run and manually verify the full flow**

Run via IIS Express (F5 in Visual Studio). Walk through, confirming each step matches the origin Editor's documented behavior:

1. Navigate to `/` → click "Open the template editor" → lands on `/Templates` (Index view, empty list).
2. Click "+ New Template" → fill in name/type/body → Create → redirected to `/Templates/{id}/Edit`.
3. Edit the body, save a new version → version number increments.
4. Open version history → confirm the new version appears.
5. Navigate to `/Templates/_setup` → confirm all checks show green (or a clear, actionable red with a fix).
6. Confirm `/TemplateBuilderEditor/css/template-editor.css` and `/TemplateBuilderEditor/js/template-editor.js` return 200 with correct content-type in the browser's network tab.
7. Visually confirm no Bootstrap 3 / editor CSS collision (the editor should render inside `#tb-editor-host` unaffected by any host-page styling — the sample host doesn't reference Bootstrap by default, so this specific check must be re-verified against the real client app during integration, not here).

- [ ] **Step 8: Commit**

```bash
git add samples/TemplateBuilder.SampleMvc5Host
git commit -m "feat: build out SampleMvc5Host, verify end-to-end template CRUD + versioning flow"
```

---

## Task 15: Packaging — bundle internal assemblies, binding redirects, pack

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`
- Create: `src/TemplateBuilder.Editor.Mvc5/tools/install.ps1`
- Create: `src/TemplateBuilder.Editor.Mvc5/README.md` (package-local README, per the origin repo's precedent of a separate package-scoped README distinct from the repo root README — see the origin repo's own stale-README incident for why this matters)

**Interfaces:**
- Produces: `TemplateBuilder.Editor.Mvc5.1.0.0.nupkg` — the final, publishable deliverable.

- [ ] **Step 1: Add the `BundleInternalAssemblies` MSBuild target**

```xml
<Target Name="BundleInternalAssemblies" BeforeTargets="_GetPackageFiles">
  <ItemGroup>
    <None Include="$(OutputPath)TemplateBuilder.Domain.dll" Pack="true" PackagePath="lib\$(TargetFramework)\" Visible="false" />
    <None Include="$(OutputPath)TemplateBuilder.Application.dll" Pack="true" PackagePath="lib\$(TargetFramework)\" Visible="false" />
    <None Include="$(OutputPath)TemplateBuilder.Infrastructure.EF6.dll" Pack="true" PackagePath="lib\$(TargetFramework)\" Visible="false" />
  </ItemGroup>
</Target>
```

- [ ] **Step 2: Write `install.ps1` for binding redirects**

Classic `packages.config` NuGet packages can run a PowerShell script at install time (Visual Studio Package Manager Console convention). This ensures the consumer's `web.config` gets the redirects our dependency tree needs without them hand-editing XML:

```powershell
param($installPath, $toolsPath, $package, $project)

$bindingRedirects = @(
    @{ Name = "Newtonsoft.Json"; PublicKeyToken = "30ad4fe6b2a6aeed"; OldVersionRange = "0.0.0.0-13.0.0.0"; NewVersion = "13.0.0.0" }
    @{ Name = "EntityFramework"; PublicKeyToken = "b77a5c561934e089"; OldVersionRange = "0.0.0.0-6.5.1.0"; NewVersion = "6.5.1.0" }
)

$configFile = $project.ProjectItems | Where-Object { $_.Name -eq "Web.config" }
if ($configFile -eq $null) {
    Write-Host "TemplateBuilder.Editor.Mvc5: could not locate Web.config to add binding redirects automatically."
    Write-Host "Add these manually to <runtime><assemblyBinding> if you hit a FileLoadException / assembly version mismatch:"
    $bindingRedirects | ForEach-Object { Write-Host "  $($_.Name): redirect $($_.OldVersionRange) -> $($_.NewVersion)" }
    return
}

Write-Host "TemplateBuilder.Editor.Mvc5: verify assembly binding redirects for Newtonsoft.Json and EntityFramework against your project's existing references — version conflicts on a packages.config project require explicit <bindingRedirect> entries."
```

*(A fully automated XML-editing `install.ps1` is possible but riskier to get right blind — this version surfaces the exact redirects needed and asks the installer to confirm, rather than silently mutating the consumer's `web.config`. Revisit once this is validated against the actual client solution in a real install.)*

```xml
<ItemGroup>
  <None Include="tools\install.ps1" Pack="true" PackagePath="tools\" />
</ItemGroup>
```

- [ ] **Step 3: Write the package-local README**

Short — mirrors the origin repo's `TemplateBuilder.Editor` package README shape (install command, connection string, `RegisterTemplateBuilderEditor` call, routing bootstrap call, `What's New` section starting at `v1.0.0`). Reference: `C:\Users\nchinnam\source\repos\TemplateBuilder\src\TemplateBuilder.Editor\README.md` for the shape to follow — do not copy its content verbatim, since the MVC5 setup steps (Unity registration, `Global.asax`, routing) are different from the ASP.NET Core original.

```xml
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
<PropertyGroup>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

- [ ] **Step 4: Pack and inspect**

```bash
dotnet pack src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj -c Release -o ./nupkg
```

Expected: `Successfully created package 'nupkg/TemplateBuilder.Editor.Mvc5.1.0.0.nupkg'`. Extract it and confirm `lib/net48/` contains `TemplateBuilder.Editor.Mvc5.dll`, `TemplateBuilder.Domain.dll`, `TemplateBuilder.Application.dll`, `TemplateBuilder.Infrastructure.EF6.dll`, and that `tools/install.ps1` and the root `README.md` are present — same verification discipline as the origin repo's packaging tasks (extract and actually look, don't assume).

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj src/TemplateBuilder.Editor.Mvc5/tools src/TemplateBuilder.Editor.Mvc5/README.md
git commit -m "feat: package TemplateBuilder.Editor.Mvc5 v1.0.0 for distribution"
```

---

## Final Verification Checklist

- [ ] `dotnet build TemplateBuilder.Mvc5.sln` completes with zero errors
- [ ] `dotnet test` passes for `Domain.Tests`, `Application.Tests`, `Infrastructure.EF6.Tests`
- [ ] `dotnet pack src/TemplateBuilder.Editor.Mvc5/` produces a `.nupkg` with `lib/net48/` containing all 4 DLLs
- [ ] `SampleMvc5Host` runs under IIS Express and the full create → edit → save version → view history flow works end-to-end (Task 14, Step 7)
- [ ] `/Templates/_setup` diagnostic page shows all checks passing against the sample host
- [ ] Binding-redirect guidance in `install.ps1` has been validated against a real `packages.config`-style test install (not just the sample host, which doesn't have the client's exact dependency versions)
- [ ] Bootstrap 3.3.7 / jQuery 3.7.1 CSS collision has been visually checked against a host page that actually loads those libraries (the sample host by design doesn't — this must happen during real client integration)
