# Audit Log + Activity Drawer (TemplateBuilder.Editor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an append-only audit log (template + snippet actions) with a full-featured global audit page (filters, stat chips, 30-day chart, before/after diffs, pagination, CSV export, live poll) and an Edit-page activity drawer (day-grouped timeline, count badge) to TemplateBuilder.Editor.

**Architecture:** `AuditLog` entity + `AuditActions` + `AuditQuery`/`IAuditRepository` + `AuditFiltering` + `AuditStatsRepository` + `AuditService` (EF Core, InMemory-testable); wire `RecordAsync` into every mutating endpoint (draft/active split by `IsActive`); `AuditController` (Index/Stats/Export) + RCL view + JS module; timeline endpoint + Edit.cshtml drawer + JS module; version 2.2.0. Supersedes lifecycle L13 (import + bulk delete DO record audit).

**Tech Stack:** .NET 8 / .NET 10 multi-target, ASP.NET Core MVC (Razor RCL), EF Core 8/10 SqlServer, System.Text.Json, xUnit + Moq + FluentAssertions, InMemory EF for repo tests.

**Spec:** `docs/superpowers/specs/2026-08-21-origin-audit-activity-design.md` — decisions A1–A12 are quoted from there.

## Global Constraints

- Repo: `github.com/nagendra571/TemplateBuilder` (private), branch `main`. `git pull` first; work from the repo root.
- **Prerequisite**: the two-state save model (2.0.0) and lifecycle & ops (2.1.0) must be merged (or in flight) — Task 2's wiring hooks onto their endpoints. Per spec A10: Tasks 1, 3–5 are independent and can proceed first; gate Task 2 on the endpoints existing.
- Build: `dotnet build TemplateBuilder.slnx` — 0 errors on both TFMs (net8.0 + net10.0). Tests: run the four test projects individually; never concurrently.
- JSON: System.Text.Json only. MVC `Ok(...)`/`[FromBody]` camelCase; stored state JSON via `JsonSerializer.Serialize(new {...})` (default PascalCase keys are fine — display-only text).
- Antiforgery: `[ValidateAntiForgeryToken]` + the `RequestVerificationToken` header (native).
- Views/assets: RCL `.cshtml` + `wwwroot` edited directly; all CSS scoped `#tb-editor-host`; reuse existing token classes; verify token names in the origin's `template-editor.css` before mapping fork CSS.
- EF Core migrations: `dotnet ef migrations add <Name> --project src/TemplateBuilder.Infrastructure`; `MigrationHostedService` applies at startup; InMemory tests bypass migrations (verified at e2e).
- e2e host: `src/TemplateBuilder.Web` at `https://localhost:7275/`; `GET /Templates/_setup` diagnostics.
- Version: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` → `2.2.0`; README What's New in sync (repo lesson).
- Commits: conventional style; only what each task lists; pushes approved separately.
- Reference implementation: the fork's audit/activity commits (`github.com/nagendra571/TemplateBuilder.MVC5`, private, `0886916`..`c5f0dae`) — exact shapes in the spec's reference table. **The fork is private — if inaccessible, the embedded tests + spec rules are the complete contract.**
- EF Core note: a DbContext does not support concurrent async operations — run the stats queries sequentially (fork's pattern).
- Do NOT touch: autosave, Create behavior, snippets beyond the two audit calls, authorization, setup page, the render contract, `TemplateBuilder.Core`.

---

### Task 1: Data layer — AuditLog, actions, repositories, service, migration

**Files:**
- Create: `src/TemplateBuilder.Domain/Entities/AuditLog.cs`, `src/TemplateBuilder.Domain/Entities/AuditActions.cs`
- Create: `src/TemplateBuilder.Domain/Interfaces/IAuditRepository.cs` (AuditQuery + interface), `src/TemplateBuilder.Domain/Interfaces/IAuditStatsRepository.cs` (AuditStats, AuditDailyBucket, interface)
- Create: `src/TemplateBuilder.Application/Services/IAuditService.cs`, `src/TemplateBuilder.Application/Services/AuditService.cs`
- Modify: `src/TemplateBuilder.Infrastructure/Data/TemplateBuilderDbContext.cs` (add `DbSet<AuditLog>`)
- Create: `src/TemplateBuilder.Infrastructure/Data/Configurations/AuditLogConfiguration.cs`
- Create: `src/TemplateBuilder.Infrastructure/Repositories/AuditFiltering.cs`, `AuditRepository.cs`, `AuditStatsRepository.cs`
- Create: `src/TemplateBuilder.Infrastructure/Migrations/<timestamp>_AddAuditLog.cs` (+ Designer; scaffolded)
- Modify: `src/TemplateBuilder.Editor/ServiceCollectionExtensions.cs` (register the three services, Scoped)
- Create: `tests/TemplateBuilder.Infrastructure.Tests/Repositories/AuditRepositoryTests.cs`, `AuditStatsRepositoryTests.cs`
- Create: `tests/TemplateBuilder.Application.Tests/Services/AuditServiceTests.cs`

**Interfaces:**
- Produces (spec Module 1 — exact shapes):
  - `AuditLog { Id, EntityType (20), EntityId (int), Action (40), Actor (200), OccurredAt, BeforeState?, AfterState?, Comment? }`
  - `AuditActions` constants: `Created, DraftSaved, Published, Restored, Duplicated, ToggledActive, Imported, Deleted, SnippetCreated, SnippetDeleted` (values `created, draft_saved, published, restored, duplicated, toggled_active, imported, deleted, snippet_created, snippet_deleted`).
  - `AuditQuery { EntityType?, EntityId?, Action?, Actor?, From?, To?, Search?, Page = 1, PageSize = 25 }`; `IAuditRepository { AddAsync, GetLastOccurrenceAsync, QueryAsync, CountAsync }`.
  - `AuditDailyBucket { Date, Count }`; `AuditStats { Total, TemplateCount, SnippetCount, UniqueActors, FirstOccurrence?, LastOccurrence?, DailyBuckets }`; `IAuditStatsRepository.GetStatsAsync(AuditQuery, ct)`.
  - `IAuditService.RecordAsync(string entityType, int entityId, string action, string actor, string? beforeState = null, string? afterState = null, string? comment = null, CancellationToken ct = default)`.
  - DI in `AddTemplateBuilderEditor` (Editor only — A12): `AddScoped<IAuditRepository, AuditRepository>()`, `AddScoped<IAuditStatsRepository, AuditStatsRepository>()`, `AddScoped<IAuditService, AuditService>()`.

- [ ] **Step 1: Write the failing tests**

`AuditRepositoryTests.cs` (new file — copy the InMemory `CreateContext()` helper from `TemplateRepositoryTests.cs`; the context must expose `AuditLogs` — Step 3 adds it):

```csharp
[Fact]
public async Task QueryAsync_FiltersByActionActorSearch_AndPaginatesDesc()
{
    await using var context = CreateContext();
    var repo = new AuditRepository(context);
    for (var i = 1; i <= 5; i++)
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = "published", Actor = "bob", Comment = $"c{i}", OccurredAt = DateTime.UtcNow.AddMinutes(-i) }, CancellationToken.None);

    var page = await repo.QueryAsync(new AuditQuery { Action = "published", Actor = "bob", Page = 1, PageSize = 2 });

    page.Should().HaveCount(2);
    page[0].Comment.Should().Be("c1");   // desc order
    (await repo.CountAsync(new AuditQuery { Action = "published" })).Should().Be(5);
}

[Fact]
public async Task QueryAsync_ToDateIsExclusive_FromDateInclusive()
{
    await using var context = CreateContext();
    var repo = new AuditRepository(context);
    var day = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = "created", Actor = "bob", OccurredAt = day }, CancellationToken.None);
    await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = "created", Actor = "bob", OccurredAt = day.AddDays(1) }, CancellationToken.None);

    var rows = await repo.QueryAsync(new AuditQuery { From = new DateTime(2026, 8, 1), To = new DateTime(2026, 8, 1) });

    rows.Should().ContainSingle(r => r.OccurredAt == day);
}

[Fact]
public async Task GetLastOccurrenceAsync_ReturnsLatestMatching()
{
    await using var context = CreateContext();
    var repo = new AuditRepository(context);
    await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 7, Action = "draft_saved", Actor = "bob", OccurredAt = DateTime.UtcNow.AddMinutes(-2) }, CancellationToken.None);
    var latest = DateTime.UtcNow;
    await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 7, Action = "draft_saved", Actor = "bob", OccurredAt = latest }, CancellationToken.None);

    var result = await repo.GetLastOccurrenceAsync("Template", 7, "draft_saved");

    result.Should().BeCloseTo(latest, TimeSpan.FromSeconds(5));
}
```

`AuditStatsRepositoryTests.cs`:

```csharp
[Fact]
public async Task GetStatsAsync_ReturnsTotalsAndUniqueActors()
{
    await using var context = CreateContext();
    var repo = new AuditStatsRepository(context);
    await context.AuditLogs.AddRangeAsync(
        new AuditLog { EntityType = "Template", EntityId = 1, Action = "published", Actor = "bob", OccurredAt = DateTime.UtcNow.AddDays(-1) },
        new AuditLog { EntityType = "Template", EntityId = 1, Action = "draft_saved", Actor = "bob", OccurredAt = DateTime.UtcNow },
        new AuditLog { EntityType = "Snippet", EntityId = 2, Action = "snippet_created", Actor = "alice", OccurredAt = DateTime.UtcNow });
    await context.SaveChangesAsync();

    var stats = await repo.GetStatsAsync(new AuditQuery());

    stats.Total.Should().Be(3);
    stats.TemplateCount.Should().Be(2);
    stats.SnippetCount.Should().Be(1);
    stats.UniqueActors.Should().Be(2);
}

[Fact]
public async Task GetStatsAsync_Fills30DayBuckets_WithZeroDays()
{
    await using var context = CreateContext();
    var repo = new AuditStatsRepository(context);
    var today = DateTime.UtcNow.Date;
    await context.AuditLogs.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = "created", Actor = "bob", OccurredAt = today });
    await context.SaveChangesAsync();

    var stats = await repo.GetStatsAsync(new AuditQuery());

    stats.DailyBuckets.Should().HaveCount(30);
    stats.DailyBuckets.Should().ContainSingle(b => b.Date == today && b.Count == 1);
    stats.DailyBuckets.Should().Contain(b => b.Date == today.AddDays(-29) && b.Count == 0);
}

[Fact]
public async Task GetStatsAsync_RespectsFromToWindow()
{
    await using var context = CreateContext();
    var repo = new AuditStatsRepository(context);
    await context.AuditLogs.AddRangeAsync(
        new AuditLog { EntityType = "Template", EntityId = 1, Action = "created", Actor = "bob", OccurredAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc) },
        new AuditLog { EntityType = "Template", EntityId = 1, Action = "created", Actor = "bob", OccurredAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc) });
    await context.SaveChangesAsync();

    var stats = await repo.GetStatsAsync(new AuditQuery { From = new DateTime(2026, 8, 3), To = new DateTime(2026, 8, 5) });

    stats.Total.Should().Be(1);
    stats.DailyBuckets.Should().HaveCount(3);   // Aug 3, 4, 5
}
```

`AuditServiceTests.cs` (Moq `IAuditRepository`):

```csharp
[Fact]
public async Task RecordAsync_SetsOccurredAt_AndDelegates()
{
    var repo = new Mock<IAuditRepository>();
    var svc = new AuditService(repo.Object);
    AuditLog? captured = null;
    repo.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask)
        .Callback((AuditLog a, CancellationToken _) => captured = a);

    await svc.RecordAsync("Template", 7, AuditActions.Published, "bob", afterState: "{}", comment: "hi");

    captured.Should().NotBeNull();
    captured!.EntityType.Should().Be("Template");
    captured.EntityId.Should().Be(7);
    captured.Action.Should().Be("published");
    captured.Actor.Should().Be("bob");
    captured.AfterState.Should().Be("{}");
    captured.Comment.Should().Be("hi");
    captured.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.Tests` and `dotnet test tests/TemplateBuilder.Application.Tests --filter "FullyQualifiedName~AuditService"`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement**

Entities (`Domain/Entities/`):

```csharp
namespace TemplateBuilder.Domain.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;   // "Template" | "Snippet"
    public int EntityId { get; set; }                        // no FK — survives hard deletes
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? Comment { get; set; }
}
```

```csharp
namespace TemplateBuilder.Domain.Entities;

public static class AuditActions
{
    public const string Created = "created";
    public const string DraftSaved = "draft_saved";
    public const string Published = "published";
    public const string Restored = "restored";
    public const string Duplicated = "duplicated";
    public const string ToggledActive = "toggled_active";
    public const string Imported = "imported";
    public const string Deleted = "deleted";
    public const string SnippetCreated = "snippet_created";
    public const string SnippetDeleted = "snippet_deleted";
}
```

`Domain/Interfaces/IAuditRepository.cs`:

```csharp
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Domain.Interfaces;

public class AuditQuery
{
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Search { get; set; }   // matches Action, Actor, Comment
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public interface IAuditRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct = default);
    Task<DateTime?> GetLastOccurrenceAsync(string entityType, int entityId, string action, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> QueryAsync(AuditQuery query, CancellationToken ct = default);
    Task<int> CountAsync(AuditQuery query, CancellationToken ct = default);
}
```

`Domain/Interfaces/IAuditStatsRepository.cs`:

```csharp
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Domain.Interfaces;

public class AuditDailyBucket
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class AuditStats
{
    public int Total { get; set; }
    public int TemplateCount { get; set; }
    public int SnippetCount { get; set; }
    public int UniqueActors { get; set; }
    public DateTime? FirstOccurrence { get; set; }
    public DateTime? LastOccurrence { get; set; }
    public IReadOnlyList<AuditDailyBucket> DailyBuckets { get; set; } = new List<AuditDailyBucket>();
}

public interface IAuditStatsRepository
{
    Task<AuditStats> GetStatsAsync(AuditQuery query, CancellationToken ct = default);
}
```

`Application/Services/IAuditService.cs` + `AuditService.cs`:

```csharp
using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Application.Services;

public interface IAuditService
{
    Task RecordAsync(string entityType, int entityId, string action, string actor,
        string? beforeState = null, string? afterState = null, string? comment = null,
        CancellationToken ct = default);
}
```

```csharp
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class AuditService : IAuditService
{
    private readonly IAuditRepository _repository;
    public AuditService(IAuditRepository repository) => _repository = repository;

    public async Task RecordAsync(string entityType, int entityId, string action, string actor,
        string? beforeState = null, string? afterState = null, string? comment = null,
        CancellationToken ct = default)
        => await _repository.AddAsync(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Actor = actor,
            BeforeState = beforeState,
            AfterState = afterState,
            Comment = comment,
            OccurredAt = DateTime.UtcNow
        }, ct);
}
```

`Infrastructure`:

- `Data/TemplateBuilderDbContext.cs` — add `public DbSet<AuditLog> AuditLogs => Set<AuditLog>();`
- `Data/Configurations/AuditLogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Actor).HasMaxLength(200).IsRequired();
        builder.Property(a => a.BeforeState).HasMaxLength(4000);
        builder.Property(a => a.AfterState).HasMaxLength(4000);
        builder.Property(a => a.Comment).HasMaxLength(1000);
        builder.Property(a => a.OccurredAt).HasColumnType("datetime2");
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAt });
        builder.HasIndex(a => a.OccurredAt);
    }
}
```

- `Repositories/AuditFiltering.cs` (internal static — port the spec's reference shape verbatim, adapting `IQueryable`):

```csharp
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Infrastructure.Repositories;

internal static class AuditFiltering
{
    internal static IQueryable<AuditLog> Apply(IQueryable<AuditLog> source, AuditQuery query)
    {
        var q = source;
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (query.EntityId.HasValue) q = q.Where(a => a.EntityId == query.EntityId.Value);
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.Actor)) q = q.Where(a => a.Actor.Contains(query.Actor));
        if (query.From.HasValue) q = q.Where(a => a.OccurredAt >= query.From.Value);
        if (query.To.HasValue)
        {
            var toExclusive = query.To.Value.Date.AddDays(1);
            q = q.Where(a => a.OccurredAt < toExclusive);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.Action.Contains(query.Search) || a.Actor.Contains(query.Search) || (a.Comment != null && a.Comment.Contains(query.Search)));
        return q;
    }
}
```

- `Repositories/AuditRepository.cs` — the spec's reference shape, EF Core LINQ (`Skip/Take/ToListAsync/FirstOrDefaultAsync`).
- `Repositories/AuditStatsRepository.cs` — port the fork's `GetStatsAsync` (sequential queries), with EF Core daily buckets:

```csharp
var (start, end) = ResolveWindow(query, last);
var buckets = await filtered
    .Where(a => a.OccurredAt >= start && a.OccurredAt < end.AddDays(1))
    .GroupBy(a => a.OccurredAt.Date)
    .Select(g => new AuditDailyBucket { Date = g.Key, Count = g.Count() })
    .ToListAsync(ct);
var byDate = buckets.ToDictionary(b => b.Date);
var filled = new List<AuditDailyBucket>();
for (var d = start; d <= end; d = d.AddDays(1))
    filled.Add(byDate.TryGetValue(d, out var b) ? b : new AuditDailyBucket { Date = d, Count = 0 });
```

(`ResolveWindow`: `From`/`To` driven, else `last?.Date ?? today` ending, `start = end.AddDays(-29)` — 30-day window; clamp `start > end → (end, end)`.)

- Scaffold the migration: `dotnet ef migrations add AddAuditLog --project src/TemplateBuilder.Infrastructure` — the generated `Up()` creates `AuditLogs` with the columns + two indexes. No hand-edits expected.
- `ServiceCollectionExtensions.AddTemplateBuilderEditor` — add the three `AddScoped` registrations (spec A12).

- [ ] **Step 4: Run — verify green**

Run the Step 2 commands. Expected: PASS (5 repo + 3 stats + 1 service tests). Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Domain src/TemplateBuilder.Application src/TemplateBuilder.Infrastructure src/TemplateBuilder.Editor/ServiceCollectionExtensions.cs tests
git commit -m "feat: audit data layer — AuditLog, actions, repositories, service, migration"
```

---

### Task 2: Action wiring into existing endpoints (supersedes lifecycle L13)

**Files:**
- Modify: `src/TemplateBuilder.Editor/Controllers/TemplatesController.cs`
- Modify: `src/TemplateBuilder.Editor/Controllers/SnippetsController.cs`
- Modify: `tests/TemplateBuilder.Editor.Tests/Controllers/TemplatesControllerTests.cs`, `tests/TemplateBuilder.Editor.Tests/Controllers/SnippetsControllerTests.cs` (create if absent)
- Gate: requires the two-state (`SaveVersionRequest.IsActive`) and lifecycle (`ImportAsync`, `BulkDelete`) endpoints to exist (spec A10). If they aren't merged yet, implement only the wiring for endpoints that exist and leave the rest for a follow-up commit — note it in the commit message.

**Interfaces:**
- Consumes: `IAuditService` (T1), `AuditActions` (T1).
- Produces: every mutating endpoint records audit per the spec Module 2 table; `CurrentActor` protected property on both controllers (`User?.Identity?.Name ?? "anonymous"`).

- [ ] **Step 1: Write the failing tests**

`TemplatesControllerTests.cs` — extend `CreateController` with `IAuditService? audit = null` (Mock). Add:

```csharp
[Fact]
public async Task SaveVersion_Active_RecordsPublished()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 1, Name = "A", TemplateType = "Email" });
    repo.Setup(r => r.GetNextVersionNumberAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(2);
    repo.Setup(r => r.PublishVersionAsync(1, It.IsAny<TemplateVersion>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int _, TemplateVersion v, CancellationToken _) => v);
    var audit = new Mock<IAuditService>();
    var controller = CreateController(repo.Object, audit: audit);

    await controller.SaveVersion(1, new SaveVersionRequest("A", "Email", null, "<p>x</p>", null, IsActive: true));

    audit.Verify(a => a.RecordAsync("Template", 1, AuditActions.Published, "anonymous",
        It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task SaveVersion_Draft_RecordsDraftSaved()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 1, Name = "A", TemplateType = "Email" });
    repo.Setup(r => r.GetNextVersionNumberAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(2);
    repo.Setup(r => r.PublishVersionAsync(1, It.IsAny<TemplateVersion>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((int _, TemplateVersion v, CancellationToken _) => v);
    var audit = new Mock<IAuditService>();
    var controller = CreateController(repo.Object, audit: audit);

    await controller.SaveVersion(1, new SaveVersionRequest("A", "Email", null, "<p>x</p>", null, IsActive: false));

    audit.Verify(a => a.RecordAsync("Template", 1, AuditActions.DraftSaved, "anonymous",
        It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Create_RecordsCreated()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.CreateAsync(It.IsAny<Template>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Template { Id = 5, Name = "A", TemplateType = "Email" });
    var audit = new Mock<IAuditService>();
    var controller = CreateController(repo.Object, audit: audit);

    await controller.Create(new TemplateEditorViewModel { Name = "A", TemplateType = "Email" });

    audit.Verify(a => a.RecordAsync("Template", 5, AuditActions.Created, "anonymous",
        It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task ToggleActive_RecordsToggledActive()
{
    var repo = new Mock<ITemplateRepository>();
    repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new Template { Id = 1, Name = "A", TemplateType = "Email", IsActive = true });
    var audit = new Mock<IAuditService>();
    var controller = CreateController(repo.Object, audit: audit);

    await controller.ToggleActive(1);

    audit.Verify(a => a.RecordAsync("Template", 1, AuditActions.ToggledActive, "anonymous",
        It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

(`SnippetsControllerTests` if a test project file exists for it — the fork's pattern: `Create` records `snippet_created` with afterState `{ name }`; `Delete` records `snippet_deleted` with beforeState `{ name }` — verify the origin's `CreateSnippetRequest` shape first.)

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests`
Expected: FAIL — no audit wiring (or controller ctor mismatch once `CreateController` gains the param).

- [ ] **Step 3: Implement**

Both controllers gain `private readonly IAuditService _auditService;` (constructor param) and:

```csharp
protected string CurrentActor => User?.Identity?.Name ?? "anonymous";
```

Wire per the spec Module 2 table — for each endpoint, call `await _auditService.RecordAsync(...)` with the exact action/state/comment, using `JsonSerializer.Serialize(new { ... })` for state (e.g. `afterState: JsonSerializer.Serialize(new { versionNumber = version.VersionNumber, versionId = version.Id, isActive = version.IsActive })`). For `SaveVersion`, branch on `version.IsActive`:

```csharp
await _auditService.RecordAsync("Template", id, version.IsActive ? AuditActions.Published : AuditActions.DraftSaved,
    CurrentActor, afterState: JsonSerializer.Serialize(new { versionNumber = version.VersionNumber, versionId = version.Id, isActive = version.IsActive }), ct: ct);
```

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS. Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor/Controllers tests/TemplateBuilder.Editor.Tests
git commit -m "feat: audit wiring on template and snippet mutations (supersedes lifecycle L13)"
```

---

### Task 3: Audit page server — controller, view model, timeline endpoint

**Files:**
- Create: `src/TemplateBuilder.Editor/Controllers/AuditController.cs`
- Create: `src/TemplateBuilder.Editor/Models/AuditIndexViewModel.cs`
- Modify: `src/TemplateBuilder.Editor/Controllers/TemplatesController.cs` (timeline endpoint)
- Create: `tests/TemplateBuilder.Editor.Tests/Controllers/AuditControllerTests.cs`
- Modify: `tests/TemplateBuilder.Editor.Tests/Controllers/TemplatesControllerTests.cs`

**Interfaces:**
- Consumes: `IAuditRepository`, `IAuditStatsRepository` (T1); `AuditActions` (T1).
- Produces routes: `GET /Audit` (view), `GET /Audit/Stats` (JSON), `GET /Audit/Export` (CSV), `GET /Templates/{id}/Audit` (timeline JSON).

- [ ] **Step 1: Write the failing tests**

`AuditControllerTests.cs` (Moq; follow the existing `CreateController` helper pattern):

```csharp
private static AuditController CreateController(IAuditRepository? auditRepo = null, IAuditStatsRepository? stats = null)
    => new(auditRepo ?? new Mock<IAuditRepository>().Object, stats ?? new Mock<IAuditStatsRepository>().Object);

[Fact]
public async Task Index_ReturnsViewWithRowsAndStats()
{
    var audit = new Mock<IAuditRepository>();
    audit.Setup(r => r.QueryAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AuditLog> { new() { Id = 1, EntityType = "Template", EntityId = 1, Action = "published", Actor = "bob", OccurredAt = DateTime.UtcNow } });
    audit.Setup(r => r.CountAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
    var stats = new Mock<IAuditStatsRepository>();
    stats.Setup(s => s.GetStatsAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AuditStats { Total = 1 });
    var controller = CreateController(auditRepo: audit.Object, stats: stats.Object);

    var result = await controller.Index(null, null, null, null, null, null);

    result.Should().BeOfType<ViewResult>();
    var model = ((ViewResult)result).Model as AuditIndexViewModel;
    model!.Rows.Should().HaveCount(1);
    model.Total.Should().Be(1);
    model.Stats.Total.Should().Be(1);
    model.KnownActions.Should().Contain(AuditActions.Published);
}

[Fact]
public async Task Stats_ReturnsOkObjectResult()
{
    var stats = new Mock<IAuditStatsRepository>();
    stats.Setup(s => s.GetStatsAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AuditStats { Total = 3 });
    var controller = CreateController(stats: stats.Object);

    var result = await controller.Stats(null, null, null, null, null, null);

    result.Should().BeOfType<OkObjectResult>();
}

[Fact]
public async Task Export_ReturnsCsvWithBomAndColumns()
{
    var audit = new Mock<IAuditRepository>();
    audit.Setup(r => r.QueryAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AuditLog>
        {
            new() { Id = 1, EntityType = "Template", EntityId = 1, Action = "published", Actor = "bob", Comment = "a,b", OccurredAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc) }
        });
    var controller = CreateController(auditRepo: audit.Object);

    var result = await controller.Export(null, null, null, null, null, null);

    var file = (FileContentResult)result;
    file.ContentType.Should().Be("text/csv");
    file.FileContents.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);   // UTF-8 BOM
    var text = Encoding.UTF8.GetString(file.FileContents, 3, file.FileContents.Length - 3);
    text.Should().StartWith("OccurredAt,EntityType,EntityId,Action,Actor,Comment,BeforeState,AfterState");
    text.Should().Contain("\"a,b\"");   // quoted comma
}
```

`TemplatesControllerTests.cs` — timeline endpoint:

```csharp
[Fact]
public async Task GetAuditTimeline_ReturnsShapedRows()
{
    var audit = new Mock<IAuditRepository>();
    audit.Setup(r => r.QueryAsync(It.IsAny<AuditQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AuditLog>
        {
            new() { Id = 9, EntityType = "Template", EntityId = 1, Action = "published", Actor = "bob", OccurredAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), Comment = "c" }
        });
    var controller = CreateController(auditRepo: audit.Object);

    var result = await controller.GetAuditTimeline(1);

    result.Should().BeOfType<OkObjectResult>();
    var ok = (OkObjectResult)result;
    ok.Value.Should().BeEquivalentTo(new[]
    {
        new { id = 9, action = "published", actor = "bob", occurredAt = "2026-08-01T12:00:00.0000000Z", comment = "c" }
    });
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test tests/TemplateBuilder.Editor.Tests`
Expected: FAIL — controller/view model/timeline endpoint missing.

- [ ] **Step 3: Implement**

`AuditIndexViewModel.cs`:

```csharp
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Editor.Models;

public class AuditIndexViewModel
{
    public List<AuditLog> Rows { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? Search { get; set; }
    public string? EntityType { get; set; }
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public AuditStats Stats { get; set; } = new();
    public IReadOnlyList<string> KnownActions { get; set; } = new List<string>();
}
```

`AuditController.cs` — port the fork's controller (spec reference shapes): `Index` builds the view model (rows + total + stats + `KnownActions` via `typeof(AuditActions).GetFields()` → values); `Stats` returns `Ok(stats)`; `Export` builds the CSV (columns + quoting + UTF-8 BOM + `Content-Disposition` header + `File(bytes, "text/csv")`); private `BuildQuery(entityType, actionName, actor, from, to, search, page, pageSize)` with `ParseDate`/`ParseToDate` (To → `AddDays(1).AddTicks(-1)`), and `Quote(string)` for CSV fields. Route attributes: `[Route("Audit")]`, `[Route("Audit/Stats")]`, `[Route("Audit/Export")]`.

`TemplatesController.GetAuditTimeline` (the controller gains `private readonly IAuditRepository _auditRepository;` as a constructor dependency — distinct from Task 2's `_auditService`):

```csharp
[HttpGet("Templates/{id:int}/Audit")]
public async Task<IActionResult> GetAuditTimeline(int id, CancellationToken ct = default)
{
    var rows = await _auditRepository.QueryAsync(new AuditQuery { EntityType = "Template", EntityId = id, PageSize = 100 }, ct);
    return Ok(rows.Select(a => new { id = a.Id, action = a.Action, actor = a.Actor, occurredAt = a.OccurredAt.ToString("o"), comment = a.Comment }));
}
```

(The timeline endpoint needs `IAuditRepository` — the same interface the controller's Index/Export paths used in the fork; `TemplatesController`'s constructor grows both `IAuditService` and `IAuditRepository`.)

- [ ] **Step 4: Run — verify green**

Run the Step 2 command. Expected: PASS. Then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor/Controllers src/TemplateBuilder.Editor/Models tests/TemplateBuilder.Editor.Tests
git commit -m "feat: audit page server — index/stats/export and template timeline endpoint"
```

---

### Task 4: Audit page UI — view, CSS, JS module

**Files:**
- Create: `src/TemplateBuilder.Editor/Views/Audit/Index.cshtml`
- Modify: `src/TemplateBuilder.Editor/wwwroot/css/template-editor.css` (append audit section)
- Modify: `src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (append audit module + shared date/action helpers)

**Interfaces:**
- Consumes: routes from T3; `AuditIndexViewModel` (T3); `AuditActions` values for badge classes.
- Produces (spec A5 + id list): the audit page and the `initAuditPage()` module; shared `fmtRelative`, `fmtDayLabel`, `actionKind` helpers consumed by Task 5's drawer.

- [ ] **Step 1: Write the view** — port the fork's `Views/Audit/Index.cshtml` (RCL, `@model AuditIndexViewModel`), preserving the exact element ids from the spec's reference table (`tb-audit-total`, `tb-stat-templates`, `tb-stat-snippets`, `tb-stat-actors`, `tb-stat-range`, `tb-audit-chart-svg`, `tb-audit-chart-axis`, `tb-audit-initial-stats`, `tb-live-pill`, `audit-expand-{id}`, `audit-state-{id}`, `audit-detail-{id}`). Server-render: header + CSV link (`@Url.Action("Export", "Audit", new { search = Model.Search, entityType = Model.EntityType, actionName = Model.Action, actor = Model.Actor, from = Model.From, to = Model.To })`), stat chips (initial values from `Model.Stats`), empty chart svg containers, filter card (search input + entity-type select + action select from `Model.KnownActions` + actor input + from/to date inputs + Clear), table with action badges (class per action via `actionKind`-style mapping — the fork uses `tb-action-badge--{action}` classes) + expand buttons + hidden state rows, windowed pagination ("Showing X–Y", prev/next), empty state. Adapt: the fork's inline anti-forgery + `_csrf` const are already handled by the origin layout scripts; use `@Url.Action` tag helpers.

- [ ] **Step 2: Write the CSS** — port the fork's audit CSS section mapped to the origin's tokens (`--surface`, `--border`, `--accent`, `--success-*`, `--warning-*`, `--danger-*`, `--radius-*`, `--surface2`, `--text-muted` — verify names first; map any renamed tokens). Include: `.tb-audit-page`, stat chips grid, chart styles, filter card, badge variants (`tb-action-badge--created/published/restored/duplicated/toggled_active/draft_saved/imported/deleted/snippet_created/snippet_deleted`), expandable detail rows (`.chg` diff highlight), pagination, live pill, empty state, responsive breakpoints.

- [ ] **Step 3: Write the JS module** — append to `template-editor.js`:

```javascript
// ── Shared date/action helpers (audit page + activity drawer) ──
function fmtRelative(isoOrDate) { /* fork's implementation verbatim */ }
function fmtDayLabel(isoOrDate) { /* fork's implementation verbatim */ }
function actionKind(action) {
    if (action === 'published' || action === 'approved') return 'success';
    if (action === 'deleted' || action === 'rejected') return 'danger';
    if (['restored', 'duplicated', 'toggled_active', 'imported'].includes(action)) return 'warning';
    return 'info';
}

// ── Audit Log page ──
(function initAuditPage() {
    const page = document.querySelector('#tb-editor-host.tb-audit-page');
    if (!page) return;
    /* fork's audit module: relative timestamps on [data-audit-time],
       expand rows (JSON diff highlight + plain-string diff),
       30-day SVG chart from /Audit/Stats dailyBuckets,
       filter form submit + Clear (location.reload with cleared params),
       windowed pagination buttons,
       30s poll of /Audit/Stats comparing totals → "N new — Refresh" pill */
})();
```

(The fork's module is the reference — the exact JS is verified working there. Fork source: `TemplateBuilder.Mvc5` `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js`, the `initAuditPage` section (~line 2671) plus the shared helpers `fmtRelative`/`fmtDayLabel`/`actionKind` (~line 20). Port verbatim, adapting only: fetch URLs are relative (same origin) and the `_csrf` const exists. If the fork is inaccessible, implement from the behavior list above — it is the contract.)

- [ ] **Step 4: Verify**

Run: `node --check src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (exit 0, no output), then `dotnet build TemplateBuilder.slnx` (0 errors — the RCL view compiles).

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor/Views/Audit src/TemplateBuilder.Editor/wwwroot
git commit -m "feat: audit page UI — filters, chart, stat chips, diffs, pagination, live poll"
```

---

### Task 5: Activity drawer on the Edit page

**Files:**
- Modify: `src/TemplateBuilder.Editor/Views/Templates/Edit.cshtml`
- Modify: `src/TemplateBuilder.Editor/wwwroot/css/template-editor.css` (append drawer section)
- Modify: `src/TemplateBuilder.Editor/wwwroot/js/template-editor.js` (append drawer module)

**Interfaces:**
- Consumes: `GET /Templates/{id}/Audit` (T3); `fmtRelative`/`fmtDayLabel`/`actionKind` (T4).
- Produces (spec A6 + ids): the drawer + `initActivityDrawer()` module.

- [ ] **Step 1: Edit.cshtml markup** — inside `.tb-editor-grid` (which gains `position: relative` via CSS), after the `.tb-panel-right` div:

```html
<button type="button" id="tb-activity-tab" class="tb-activity-tab"
        aria-expanded="false" aria-controls="tb-activity-drawer" hidden>
    Activity <span id="tb-activity-count" class="tb-activity-count">0</span>
</button>
<aside id="tb-activity-drawer" class="tb-activity-drawer" hidden
       role="dialog" aria-modal="false" aria-label="Activity timeline">
    <div class="tb-activity-header">
        <span>Activity</span>
        <button type="button" id="btn-activity-close" class="tb-activity-close" aria-label="Close">&#x2715;</button>
    </div>
    <div id="tb-timeline" class="tb-timeline"></div>
</aside>
```

(Keep the tab `hidden` until the timeline loads — the fork shows the tab once the count fetch succeeds; only render it when `!isNew`.)

- [ ] **Step 2: CSS** — port the fork's drawer section (tab + drawer absolutely positioned inside the grid; grid `position: relative`; drawer full-height right edge, slide transition, `[hidden]` display rules, timeline item/day-group/dot styles, count badge) mapped to origin tokens.

- [ ] **Step 3: JS** — append `initActivityDrawer()` (guard on `#tb-activity-tab`):

```javascript
(function initActivityDrawer() {
    const tab = document.getElementById('tb-activity-tab');
    if (!tab || templateId === null) return;
    /* open/close (tab click, X, Escape, Tab focus trap),
       load timeline on first open: fetch(`/Templates/${templateId}/Audit`)
         → day-grouped list (fmtDayLabel headers, fmtRelative times,
           actionKind dot colors, escapeHtml(comment)),
         count badge = rows length, tab.hidden = false after first load,
       refresh count on each subsequent open, empty state message */
})();
```

(Fork source: `TemplateBuilder.Mvc5` `template-editor.js`, the `// Activity drawer (Edit page)` section ~line 2176, plus the drawer CSS in `template-editor.css` (~line 1503, "Section 34"). Port verbatim, mapping token names. If the fork is inaccessible, the behavior list above is the contract.)

- [ ] **Step 4: Verify**

Run: `node --check src/TemplateBuilder.Editor/wwwroot/js/template-editor.js`, then `dotnet build TemplateBuilder.slnx` — 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor/Views/Templates/Edit.cshtml src/TemplateBuilder.Editor/wwwroot
git commit -m "feat: activity drawer on the edit page (day-grouped timeline)"
```

---

### Task 6: e2e verification + version 2.2.0 + README

**Files:**
- Modify: `src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj` (`<Version>` → `2.2.0`)
- Modify: `src/TemplateBuilder.Editor/README.md` (features table rows: `Audit log GET /Audit`, `Audit CSV export GET /Audit/Export`, `Template audit timeline GET /Templates/{id}/Audit`; an Audit & Activity section; What's New `#### v2.2.0`)

- [ ] **Step 1: Version bump + README** — bump to 2.2.0; add the rows/section/What's New (the repo's README-sync lesson).

- [ ] **Step 2: Build + all suites**

`dotnet build TemplateBuilder.slnx` (0 errors) then the four test projects. Expected: all green.

- [ ] **Step 3: e2e (Web at https://localhost:7275/)** — the spec Module 5 checklist:

1. Fresh DB boot applies `AddAuditLog` (sqlcmd: `AuditLogs` table + 2 indexes).
2. Flow: create → Save Draft → Save Version → toggle active → duplicate → import a v2 export → bulk delete; then:
3. `/Audit` page: rows present with correct badges (created/draft_saved/published/toggled_active/imported/deleted); filter by action `published` narrows; date-range filter works (from/to); search matches action/actor/comment; stat chips (total/templates/snippets/actors/range) correct; 30-day chart renders bars (svg elements); expand a row → before/after state diff with `.chg` highlights; pagination shows "Showing 1–25" and pages; CSV export downloads `template-builder-audit.csv` (8 columns, BOM, quoted fields — verify with `head -c` / file).
4. Live poll: wait ~30s → pill shows "N new — Refresh" after a new action; click refreshes.
5. Edit page drawer: tab visible with count; opens on click; day-grouped timeline with colored dots; Esc/X close; count matches the template's audit rows (incl. draft_saved/published rows); `GET /Templates/{id}/Audit` returns ≤100 rows desc.
6. Snippet create/delete → `snippet_created`/`snippet_deleted` rows on the audit page.
7. `GET /Templates/_setup` — all checks pass.

- [ ] **Step 4: Pack + inspect** — `dotnet pack`; extract the nupkg: 4 DLLs + RCL views/assets + README (What's New 2.2.0); no surprises.

- [ ] **Step 5: Fix forward** — failures return to the owning task (TDD first), re-run Steps 1–4.

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Editor/TemplateBuilder.Editor.csproj src/TemplateBuilder.Editor/README.md
git commit -m "docs: v2.2.0 — audit log and activity drawer"
```

---

## Self-review notes

- Spec coverage: A1–A2 → T1; A3–A4 → T2; A5/A7/A8 → T3 + T4; A6 → T5; A9 → T6; A10 → Global Constraints gating; A11 → noted (convention covers new routes); A12 → T1 DI step. Modules 1–5 all mapped.
- EF Core adaptation vs the fork: sequential stats queries (same DbContext rule as EF6), `GroupBy(a => a.OccurredAt.Date)` for daily buckets, `IQueryable` filtering — all InMemory-testable.
- Task 2 is the only task gated on the other agent's endpoints (A10); Tasks 1, 3–5 are independent.
- The timeline endpoint is placed on `TemplatesController` (route `Templates/{id:int}/Audit`) — it needs `IAuditRepository` (query), distinct from the drawer's consumption of its JSON.
- CSV quoting/BOM tests pin the exact export contract (spec A8).
- `actionKind` maps the fork's badge/dot colors onto the reduced action set (A3) — `approved`/`rejected` cases are dead branches kept for forward-compat with the fork's palette; harmless.
