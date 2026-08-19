# Governance & Compliance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add draft→review→approve→publish workflow with server-side drafts, an append-only audit log (per-template timeline + global view + CSV export), publish-race and concurrency hardening, and snippet versioning + usage tracking to the MVC5 fork.

**Architecture:** In-entity state (Approach A from the spec): `TemplateStatus` enum column on Template, server-side `DraftBody`, one append-only `AuditLog` table, `SnippetVersion` + `SnippetUsage` tables, and an Application-layer `TemplateWorkflowService` that owns all transition rules so they are unit-testable without a web context. EF6 handwritten migrations (repo pattern), RazorGenerator precompiled views, Unity DI.

**Tech Stack:** net48, ASP.NET MVC 5, EF6 (Code-First, handwritten migrations), Unity 5.x, Scriban 7.2.6, xunit + FluentAssertions, vanilla JS (SunEditor), RazorGenerator.Mvc.

**Spec:** `docs/superpowers/specs/2026-08-19-governance-compliance-design.md` — the plan argues from the spec; executors read both.

## Global Constraints

- Domain/Application are fork-extended (new additions allowed; verbatim-port policy applies only to pre-existing files, which are edited minimally). Document new fork decisions in commit messages.
- Unity namespace is `Unity`; EF6 exception types are `System.Data.Entity.Infrastructure.DbUpdateConcurrencyException` and `System.Data.Entity.Infrastructure.DbUpdateException` — never the EF Core namespaces.
- New `.cshtml` views are auto-precompiled by the csproj RazorGenDriver during build — always run a full build after adding views; `obj/CodeGen` regenerates automatically.
- Enum stored as int: `Draft=0, Review=1, Approved=2, Published=3`.
- All timestamps UTC. Actor = `User.Identity?.Name ?? "anonymous"`.
- Conventional commit messages (`feat:`, `fix:`, `chore:`, `test:`). Tests first, per task.

---

### Task 1: Domain model — status, audit, snippet versioning/usage entities

**Files:**
- Create: `src/TemplateBuilder.Domain/Entities/TemplateStatus.cs`
- Create: `src/TemplateBuilder.Domain/Entities/AuditLog.cs`
- Create: `src/TemplateBuilder.Domain/Entities/SnippetVersion.cs`
- Create: `src/TemplateBuilder.Domain/Entities/SnippetUsage.cs`
- Create: `src/TemplateBuilder.Domain/Entities/AuditActions.cs`
- Modify: `src/TemplateBuilder.Domain/Entities/Template.cs`
- Modify: `src/TemplateBuilder.Domain/Entities/Snippet.cs`
- Modify: `src/TemplateBuilder.Domain/Interfaces/ITemplateRepository.cs`
- Modify: `src/TemplateBuilder.Domain/Interfaces/ISnippetRepository.cs`
- Create: `src/TemplateBuilder.Domain/Interfaces/IAuditRepository.cs`
- Test: `tests/TemplateBuilder.Domain.Tests/EntitiesTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `TemplateStatus`, `AuditLog`, `SnippetVersion`, `SnippetUsage`, `AuditActions` constants, extended `Template`/`Snippet`, extended `ITemplateRepository`/`ISnippetRepository`, new `IAuditRepository`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/TemplateBuilder.Domain.Tests/EntitiesTests.cs`:

```csharp
[Fact]
public void Template_defaults_to_Draft_status()
{
    var t = new Template { Name = "X", TemplateType = "Email" };
    t.Status.Should().Be(TemplateStatus.Draft);
}

[Fact]
public void AuditLog_defaults_are_nullable()
{
    var a = new AuditLog { EntityType = "Template", EntityId = 1, Action = "created", Actor = "user" };
    a.OccurredAt.Should().Be(default);
    a.BeforeState.Should().BeNull();
}

[Fact]
public void SnippetVersion_defaults_to_empty_body()
{
    var v = new SnippetVersion { SnippetId = 1, VersionNumber = 1 };
    v.Body.Should().BeEmpty();
}

[Fact]
public void AuditActions_contains_all_expected_values()
{
    AuditActions.Published.Should().Be("published");
    AuditActions.Submitted.Should().Be("submitted");
    AuditActions.Approved.Should().Be("approved");
    AuditActions.Rejected.Should().Be("rejected");
    AuditActions.ReviewCancelled.Should().Be("review_cancelled");
    AuditActions.SnippetRestored.Should().Be("snippet_restored");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj --filter "FullyQualifiedName~EntitiesTests"`
Expected: FAIL — `TemplateStatus` type not found.

- [ ] **Step 3: Write the implementation**

`src/TemplateBuilder.Domain/Entities/TemplateStatus.cs`:

```csharp
namespace TemplateBuilder.Domain.Entities;

public enum TemplateStatus
{
    Draft = 0,
    Review = 1,
    Approved = 2,
    Published = 3
}
```

`src/TemplateBuilder.Domain/Entities/AuditLog.cs`:

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

`src/TemplateBuilder.Domain/Entities/SnippetVersion.cs`:

```csharp
namespace TemplateBuilder.Domain.Entities;

public class SnippetVersion
{
    public int Id { get; set; }
    public int SnippetId { get; set; }
    public int VersionNumber { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ChangeComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public Snippet Snippet { get; set; } = null!;
}
```

`src/TemplateBuilder.Domain/Entities/SnippetUsage.cs`:

```csharp
namespace TemplateBuilder.Domain.Entities;

public class SnippetUsage
{
    public int Id { get; set; }
    public int SnippetId { get; set; }
    public int TemplateId { get; set; }
    public DateTime UsedAt { get; set; }
    public string? UsedBy { get; set; }
}
```

`src/TemplateBuilder.Domain/Entities/AuditActions.cs`:

```csharp
namespace TemplateBuilder.Domain.Entities;

public static class AuditActions
{
    public const string Created = "created";
    public const string Edited = "edited";
    public const string Published = "published";
    public const string Restored = "restored";
    public const string Duplicated = "duplicated";
    public const string ToggledActive = "toggled_active";
    public const string DraftSaved = "draft_saved";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string ReviewCancelled = "review_cancelled";
    public const string SnippetCreated = "snippet_created";
    public const string SnippetEdited = "snippet_edited";
    public const string SnippetDeleted = "snippet_deleted";
    public const string SnippetRestored = "snippet_restored";
}
```

Extend `Template.cs` (add three properties; keep everything else):

```csharp
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    public string? DraftBody { get; set; }
    public string? ReviewComment { get; set; }
```

Extend `Snippet.cs` (add one property):

```csharp
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
```

Extend `ITemplateRepository.cs` — add after the existing `PublishVersionAsync` line:

```csharp
    Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, Action<Template>? updateTemplate, CancellationToken ct = default);
```

Extend `ISnippetRepository.cs` — add:

```csharp
    Task<Snippet> UpdateWithVersionAsync(Snippet snippet, string oldBody, string? changeComment, string actor, CancellationToken ct = default);
    Task<IReadOnlyList<SnippetVersion>> GetVersionHistoryAsync(int snippetId, CancellationToken ct = default);
    Task<SnippetVersion?> GetVersionAsync(int snippetId, int versionId, CancellationToken ct = default);
    Task<Snippet> RestoreVersionAsync(int snippetId, int sourceVersionId, string actor, CancellationToken ct = default);
    Task RecordUsageAsync(int snippetId, int templateId, string actor, CancellationToken ct = default);
    Task<IReadOnlyList<SnippetUsageStats>> GetUsageStatsAsync(CancellationToken ct = default);
```

Create `src/TemplateBuilder.Domain/Interfaces/IAuditRepository.cs`:

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

public record SnippetUsageStats(int SnippetId, int UsageCount, int TemplateCount, DateTime? LastUsedAt);
```

Note: `SnippetUsageStats` lives in `Domain.Interfaces` (same file as the interface that returns it) to avoid a circular reference.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TemplateBuilder.Domain.Tests/TemplateBuilder.Domain.Tests.csproj`
Expected: PASS (existing interface-contract tests may need no changes; if `InterfaceContractTests` asserts exact interface members, update those assertions to include the new members).

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Domain tests/TemplateBuilder.Domain.Tests
git commit -m "feat: domain model for workflow, audit log, snippet versioning and usage tracking"
```

---

### Task 2: Application — AuditService

**Files:**
- Create: `src/TemplateBuilder.Application/Services/IAuditService.cs`
- Create: `src/TemplateBuilder.Application/Services/AuditService.cs`
- Test: `tests/TemplateBuilder.Application.Tests/AuditServiceTests.cs`

**Interfaces:**
- Consumes: `AuditLog`, `AuditQuery`, `IAuditRepository` (Task 1).
- Produces: `IAuditService` — used by `TemplateWorkflowService` (Task 3) and all controllers (Tasks 5–6).

- [ ] **Step 1: Write the failing tests**

`tests/TemplateBuilder.Application.Tests/AuditServiceTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Application.Tests;

public class AuditServiceTests
{
    private static (AuditService service, IAuditRepository repo) Create()
    {
        var repo = Substitute.For<IAuditRepository>();
        return (new AuditService(repo), repo);
    }

    [Fact]
    public async Task Record_sets_occurred_at_utc_and_persists()
    {
        var (svc, repo) = Create();
        await svc.RecordAsync("Template", 1, AuditActions.Created, "bob");
        await repo.Received(1).AddAsync(Arg.Is<AuditLog>(a =>
            a.EntityType == "Template" && a.EntityId == 1 &&
            a.Action == AuditActions.Created && a.Actor == "bob" &&
            (DateTime.UtcNow - a.OccurredAt).Duration().TotalSeconds < 10));
    }

    [Fact]
    public async Task Draft_saved_is_throttled_to_once_per_five_minutes()
    {
        var (svc, repo) = Create();
        repo.GetLastOccurrenceAsync("Template", 1, AuditActions.DraftSaved, default)
            .Returns(DateTime.UtcNow.AddMinutes(-2));
        await svc.RecordAsync("Template", 1, AuditActions.DraftSaved, "bob");
        await repo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Draft_saved_records_when_stale_enough()
    {
        var (svc, repo) = Create();
        repo.GetLastOccurrenceAsync("Template", 1, AuditActions.DraftSaved, default)
            .Returns(DateTime.UtcNow.AddMinutes(-6));
        await svc.RecordAsync("Template", 1, AuditActions.DraftSaved, "bob");
        await repo.Received(1).AddAsync(Arg.Any<AuditLog>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --filter "FullyQualifiedName~AuditServiceTests"`
Expected: FAIL — types not found. (If NSubstitute is not a package reference in the test project, add `NSubstitute` 5.x to the test csproj first.)

- [ ] **Step 3: Write the implementation**

`src/TemplateBuilder.Application/Services/IAuditService.cs`:

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

`src/TemplateBuilder.Application/Services/AuditService.cs`:

```csharp
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class AuditService : IAuditService
{
    private static readonly TimeSpan DraftSaveThrottle = TimeSpan.FromMinutes(5);

    private readonly IAuditRepository _repository;

    public AuditService(IAuditRepository repository) => _repository = repository;

    public async Task RecordAsync(string entityType, int entityId, string action, string actor,
        string? beforeState = null, string? afterState = null, string? comment = null,
        CancellationToken ct = default)
    {
        if (action == AuditActions.DraftSaved)
        {
            var last = await _repository.GetLastOccurrenceAsync(entityType, entityId, action, ct);
            if (last.HasValue && DateTime.UtcNow - last.Value < DraftSaveThrottle)
                return;
        }

        await _repository.AddAsync(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Actor = actor,
            OccurredAt = DateTime.UtcNow,
            BeforeState = beforeState,
            AfterState = afterState,
            Comment = comment
        }, ct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Application tests/TemplateBuilder.Application.Tests
git commit -m "feat: append-only audit service with draft-save throttling"
```

---

### Task 3: Application — TemplateWorkflowService

**Files:**
- Create: `src/TemplateBuilder.Application/Services/TemplateWorkflowResult.cs`
- Create: `src/TemplateBuilder.Application/Services/ITemplateWorkflowService.cs`
- Create: `src/TemplateBuilder.Application/Services/TemplateWorkflowService.cs`
- Test: `tests/TemplateBuilder.Application.Tests/TemplateWorkflowServiceTests.cs`

**Interfaces:**
- Consumes: `ITemplateRepository`, `IAuditService`, `TemplateStatus`, `AuditActions` (Tasks 1–2).
- Produces: `ITemplateWorkflowService` — used by TemplatesController (Task 5).

- [ ] **Step 1: Write the failing tests**

`tests/TemplateBuilder.Application.Tests/TemplateWorkflowServiceTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Application.Tests;

public class TemplateWorkflowServiceTests
{
    private static Template MakeTemplate(TemplateStatus status, string? body = null, int? currentVersionId = null)
        => new()
        {
            Id = 7,
            Name = "T",
            TemplateType = "Email",
            Status = status,
            DraftBody = body,
            CurrentVersionId = currentVersionId,
            CurrentVersion = currentVersionId is null ? null : new TemplateVersion { Id = currentVersionId.Value, Body = body ?? string.Empty },
            Versions = new List<TemplateVersion>()
        };

    private static (TemplateWorkflowService svc, ITemplateRepository repo, IAuditService audit) Create()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var audit = Substitute.For<IAuditService>();
        return (new TemplateWorkflowService(repo, audit), repo, audit);
    }

    [Fact]
    public async Task SubmitForReview_moves_draft_to_review_and_saves_draft_body()
    {
        var (svc, repo, audit) = Create();
        var t = MakeTemplate(TemplateStatus.Draft);
        repo.GetByIdAsync(7, default).Returns(t);

        var result = await svc.SubmitForReviewAsync(7, "New body", "bob");

        result.Success.Should().BeTrue();
        t.Status.Should().Be(TemplateStatus.Review);
        t.DraftBody.Should().Be("New body");
        await repo.Received(1).UpdateTemplateAsync(t, default);
        await audit.Received(1).RecordAsync("Template", 7, AuditActions.Submitted, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), null, default);
    }

    [Fact]
    public async Task SubmitForReview_rejects_empty_body()
    {
        var (svc, repo, _) = Create();
        var t = MakeTemplate(TemplateStatus.Draft);
        repo.GetByIdAsync(7, default).Returns(t);

        var result = await svc.SubmitForReviewAsync(7, "   ", "bob");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
        await repo.DidNotReceiveWithAnyArgs().UpdateTemplateAsync(default!, default);
    }

    [Fact]
    public async Task SubmitForReview_rejects_non_draft()
    {
        var (svc, repo, _) = Create();
        repo.GetByIdAsync(7, default).Returns(MakeTemplate(TemplateStatus.Published));

        var result = await svc.SubmitForReviewAsync(7, "body", "bob");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Approve_requires_review()
    {
        var (svc, repo, audit) = Create();
        var t = MakeTemplate(TemplateStatus.Review, "Body");
        repo.GetByIdAsync(7, default).Returns(t);

        var result = await svc.ApproveAsync(7, "bob");

        result.Success.Should().BeTrue();
        t.Status.Should().Be(TemplateStatus.Approved);
        await audit.Received(1).RecordAsync("Template", 7, AuditActions.Approved, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), null, default);
    }

    [Fact]
    public async Task Approve_requires_review_state()
    {
        var (svc, repo, _) = Create();
        repo.GetByIdAsync(7, default).Returns(MakeTemplate(TemplateStatus.Draft));

        var result = await svc.ApproveAsync(7, "bob");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Reject_returns_to_draft_with_comment()
    {
        var (svc, repo, audit) = Create();
        var t = MakeTemplate(TemplateStatus.Review, "Body");
        repo.GetByIdAsync(7, default).Returns(t);

        var result = await svc.RejectAsync(7, "waiting on legal wording", "bob");

        result.Success.Should().BeTrue();
        t.Status.Should().Be(TemplateStatus.Draft);
        t.ReviewComment.Should().Be("waiting on legal wording");
        await audit.Received(1).RecordAsync("Template", 7, AuditActions.Rejected, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), "waiting on legal wording", default);
    }

    [Fact]
    public async Task CancelReview_returns_review_or_approved_to_draft()
    {
        var (svc, repo, audit) = Create();
        var t = MakeTemplate(TemplateStatus.Approved, "Body");
        repo.GetByIdAsync(7, default).Returns(t);

        var result = await svc.CancelReviewAsync(7, "bob");

        result.Success.Should().BeTrue();
        t.Status.Should().Be(TemplateStatus.Draft);
        await audit.Received(1).RecordAsync("Template", 7, AuditActions.ReviewCancelled, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), null, default);
    }

    [Fact]
    public async Task SaveDraft_moves_published_to_draft_when_body_changes()
    {
        var (svc, repo, _) = Create();
        var t = MakeTemplate(TemplateStatus.Published, null, currentVersionId: 99);
        repo.GetByIdAsync(7, default).Returns(t);

        var result = await svc.SaveDraftAsync(7, "changed body", "bob");

        result.Success.Should().BeTrue();
        t.Status.Should().Be(TemplateStatus.Draft);
        t.DraftBody.Should().Be("changed body");
    }

    [Fact]
    public async Task SaveDraft_keeps_published_when_body_unchanged()
    {
        var (svc, repo, _) = Create();
        var t = MakeTemplate(TemplateStatus.Published, "current body", currentVersionId: 99);
        repo.GetByIdAsync(7, default).Returns(t);

        var result = await svc.SaveDraftAsync(7, "current body", "bob");

        result.Success.Should().BeTrue();
        t.Status.Should().Be(TemplateStatus.Published);
    }

    [Fact]
    public async Task SaveDraft_rejects_when_locked()
    {
        var (svc, repo, _) = Create();
        repo.GetByIdAsync(7, default).Returns(MakeTemplate(TemplateStatus.Review, "Body"));

        var result = await svc.SaveDraftAsync(7, "Body2", "bob");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Publish_creates_version_from_draft_body_and_returns_to_published()
    {
        var (svc, repo, audit) = Create();
        var t = MakeTemplate(TemplateStatus.Approved, "Approved body", null);
        var version = new TemplateVersion { Id = 5, VersionNumber = 2 };
        repo.GetByIdAsync(7, default).Returns(t);
        repo.PublishVersionAsync(7, Arg.Any<TemplateVersion>(), Arg.Any<Action<Template>?>(), default)
            .Returns(version);

        var result = await svc.PublishAsync(7, "bob");

        result.Success.Should().BeTrue();
        await repo.Received(1).PublishVersionAsync(7, Arg.Is<TemplateVersion>(v =>
            v.TemplateId == 7 && v.Body == "Approved body"), Arg.Any<Action<Template>?>(), default);
        await audit.Received(1).RecordAsync("Template", 7, AuditActions.Published, "bob",
            Arg.Any<string?>(), Arg.Is<string>(s => s.Contains("2")), null, default);
    }

    [Fact]
    public async Task Publish_requires_approved()
    {
        var (svc, repo, _) = Create();
        repo.GetByIdAsync(7, default).Returns(MakeTemplate(TemplateStatus.Review, "Body"));

        var result = await svc.PublishAsync(7, "bob");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Publish_rejects_when_draft_body_missing()
    {
        var (svc, repo, _) = Create();
        repo.GetByIdAsync(7, default).Returns(MakeTemplate(TemplateStatus.Approved, null, null));

        var result = await svc.PublishAsync(7, "bob");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Missing_template_returns_not_found()
    {
        var (svc, repo, _) = Create();
        repo.GetByIdAsync(7, default).Returns((Template?)null);

        var result = await svc.SubmitForReviewAsync(7, "body", "bob");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj --filter "FullyQualifiedName~TemplateWorkflowServiceTests"`
Expected: FAIL — types not found.

- [ ] **Step 3: Write the implementation**

`src/TemplateBuilder.Application/Services/TemplateWorkflowResult.cs`:

```csharp
using TemplateBuilder.Domain.Entities;
namespace TemplateBuilder.Application.Services;

public class TemplateWorkflowResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }       // NOT_FOUND | CONFLICT | VALIDATION_ERROR
    public string? ErrorMessage { get; init; }
    public Template? Template { get; init; }

    public static TemplateWorkflowResult Ok(Template template) => new() { Success = true, Template = template };
    public static TemplateWorkflowResult Fail(string code, string message) => new() { Success = false, ErrorCode = code, ErrorMessage = message };
}
```

`src/TemplateBuilder.Application/Services/ITemplateWorkflowService.cs`:

```csharp
namespace TemplateBuilder.Application.Services;

public interface ITemplateWorkflowService
{
    Task<TemplateWorkflowResult> SaveDraftAsync(int templateId, string body, string actor, CancellationToken ct = default);
    Task<TemplateWorkflowResult> SubmitForReviewAsync(int templateId, string body, string actor, CancellationToken ct = default);
    Task<TemplateWorkflowResult> ApproveAsync(int templateId, string actor, CancellationToken ct = default);
    Task<TemplateWorkflowResult> RejectAsync(int templateId, string comment, string actor, CancellationToken ct = default);
    Task<TemplateWorkflowResult> CancelReviewAsync(int templateId, string actor, CancellationToken ct = default);
    Task<TemplateWorkflowResult> PublishAsync(int templateId, string actor, CancellationToken ct = default);
}
```

`src/TemplateBuilder.Application/Services/TemplateWorkflowService.cs`:

```csharp
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class TemplateWorkflowService : ITemplateWorkflowService
{
    private readonly ITemplateRepository _repository;
    private readonly IAuditService _audit;

    public TemplateWorkflowService(ITemplateRepository repository, IAuditService audit)
    {
        _repository = repository;
        _audit = audit;
    }

    public async Task<TemplateWorkflowResult> SaveDraftAsync(int templateId, string body, string actor, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return TemplateWorkflowResult.Fail("NOT_FOUND", $"Template {templateId} not found.");
        if (template.Status == TemplateStatus.Review || template.Status == TemplateStatus.Approved)
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "This template is locked for review.");

        var currentBody = template.DraftBody ?? template.CurrentVersion?.Body ?? string.Empty;
        if (template.Status == TemplateStatus.Published && body != currentBody)
            template.Status = TemplateStatus.Draft;
        template.DraftBody = body;

        await _repository.UpdateTemplateAsync(template, ct);
        await _audit.RecordAsync("Template", templateId, AuditActions.DraftSaved, actor, ct: ct);
        return TemplateWorkflowResult.Ok(template);
    }

    public async Task<TemplateWorkflowResult> SubmitForReviewAsync(int templateId, string body, string actor, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return TemplateWorkflowResult.Fail("NOT_FOUND", $"Template {templateId} not found.");
        if (template.Status != TemplateStatus.Draft)
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "Only draft templates can be submitted for review.");
        if (string.IsNullOrWhiteSpace(body))
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "A template body is required before submitting for review.");

        var before = template.Status.ToString();
        template.DraftBody = body;
        template.Status = TemplateStatus.Review;

        await _repository.UpdateTemplateAsync(template, ct);
        await _audit.RecordAsync("Template", templateId, AuditActions.Submitted, actor, before, template.Status.ToString(), ct: ct);
        return TemplateWorkflowResult.Ok(template);
    }

    public async Task<TemplateWorkflowResult> ApproveAsync(int templateId, string actor, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return TemplateWorkflowResult.Fail("NOT_FOUND", $"Template {templateId} not found.");
        if (template.Status != TemplateStatus.Review)
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "Only templates under review can be approved.");

        var before = template.Status.ToString();
        template.Status = TemplateStatus.Approved;

        await _repository.UpdateTemplateAsync(template, ct);
        await _audit.RecordAsync("Template", templateId, AuditActions.Approved, actor, before, template.Status.ToString(), ct: ct);
        return TemplateWorkflowResult.Ok(template);
    }

    public async Task<TemplateWorkflowResult> RejectAsync(int templateId, string comment, string actor, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return TemplateWorkflowResult.Fail("NOT_FOUND", $"Template {templateId} not found.");
        if (template.Status != TemplateStatus.Review)
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "Only templates under review can be rejected.");

        var before = template.Status.ToString();
        template.Status = TemplateStatus.Draft;
        template.ReviewComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        await _repository.UpdateTemplateAsync(template, ct);
        await _audit.RecordAsync("Template", templateId, AuditActions.Rejected, actor, before, template.Status.ToString(), template.ReviewComment, ct);
        return TemplateWorkflowResult.Ok(template);
    }

    public async Task<TemplateWorkflowResult> CancelReviewAsync(int templateId, string actor, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return TemplateWorkflowResult.Fail("NOT_FOUND", $"Template {templateId} not found.");
        if (template.Status != TemplateStatus.Review && template.Status != TemplateStatus.Approved)
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "Only templates under review or approved can be cancelled.");

        var before = template.Status.ToString();
        template.Status = TemplateStatus.Draft;

        await _repository.UpdateTemplateAsync(template, ct);
        await _audit.RecordAsync("Template", templateId, AuditActions.ReviewCancelled, actor, before, template.Status.ToString(), ct: ct);
        return TemplateWorkflowResult.Ok(template);
    }

    public async Task<TemplateWorkflowResult> PublishAsync(int templateId, string actor, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return TemplateWorkflowResult.Fail("NOT_FOUND", $"Template {templateId} not found.");
        if (template.Status != TemplateStatus.Approved)
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "Only approved templates can be published.");
        if (string.IsNullOrWhiteSpace(template.DraftBody))
            return TemplateWorkflowResult.Fail("VALIDATION_ERROR", "The approved body is missing.");

        var version = await _repository.PublishVersionAsync(templateId, new TemplateVersion
        {
            TemplateId = templateId,
            Body = template.DraftBody
        }, t =>
        {
            t.Status = TemplateStatus.Published;
            t.DraftBody = null;
            t.ReviewComment = null;
        }, ct);

        await _audit.RecordAsync("Template", templateId, AuditActions.Published, actor,
            null, $"{{\"versionNumber\":{version.VersionNumber},\"versionId\":{version.Id}}}", ct: ct);
        return TemplateWorkflowResult.Ok(template);
    }
}
```

**Concurrency note:** the Application project does not reference EF6 (verified: its csproj has no EntityFramework package), so `DbUpdateConcurrencyException` must NOT be caught here — repository `SaveChangesAsync` failures bubble to the controller, which maps them to 409 exactly like the existing `SaveVersion` action (Task 5).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TemplateBuilder.Application.Tests/TemplateBuilder.Application.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Application tests/TemplateBuilder.Application.Tests
git commit -m "feat: template workflow service (draft, submit, approve, reject, cancel, publish)"
```

---

### Task 4: EF6 — DbContext, migration, repositories (atomic publish, audit, snippet versioning/usage)

**Files:**
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Data/TemplateBuilderDbContext.cs`
- Create: `src/TemplateBuilder.Infrastructure.EF6/Migrations/AddGovernance.cs`
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Repositories/TemplateRepository.cs`
- Modify: `src/TemplateBuilder.Infrastructure.EF6/Repositories/SnippetRepository.cs`
- Create: `src/TemplateBuilder.Infrastructure.EF6/Repositories/AuditRepository.cs`
- Test: `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateRepositoryTests.cs`
- Test: `tests/TemplateBuilder.Infrastructure.EF6.Tests/SnippetRepositoryTests.cs`
- Create: `tests/TemplateBuilder.Infrastructure.EF6.Tests/AuditRepositoryTests.cs`

**Interfaces:**
- Consumes: entities/interfaces from Task 1.
- Produces: persisted versions of the repository APIs; `IAuditRepository` implementation; atomic `PublishVersionAsync` with the new optional callback.

- [ ] **Step 1: Write the failing tests**

Append to `tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateRepositoryTests.cs`:

```csharp
[Fact]
public async Task PublishVersionAsync_assigns_incrementing_version_numbers()
{
    using var ctx = CreateContext();
    var repo = new TemplateRepository(ctx);
    var t = await repo.CreateAsync(new Template { Name = "Publish Race", TemplateType = "Email" });

    var v1 = await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = "a" });
    var v2 = await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = "b" });

    v1.VersionNumber.Should().Be(1);
    v2.VersionNumber.Should().Be(2);
    (await repo.GetByIdAsync(t.Id))!.CurrentVersionId.Should().Be(v2.Id);
}

[Fact]
public async Task PublishVersionAsync_applies_template_callback_after_insert()
{
    using var ctx = CreateContext();
    var repo = new TemplateRepository(ctx);
    var t = await repo.CreateAsync(new Template { Name = "Callback Publish", TemplateType = "Email", Status = TemplateStatus.Approved });

    await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = "body" },
        tb => { tb.Status = TemplateStatus.Published; tb.DraftBody = null; });

    var fetched = await repo.GetByIdAsync(t.Id);
    fetched!.Status.Should().Be(TemplateStatus.Published);
    fetched.DraftBody.Should().BeNull();
}

[Fact]
public async Task Concurrent_publishes_never_duplicate_a_version_number()
{
    using (var seed = CreateContext()) { /* drop+create schema once, before any parallel work */ }
    var t = await CreateTemplate("Concurrent Race");

    var tasks = Enumerable.Range(0, 5).Select(async i =>
    {
        using var ctx = CreateContextNoRecreate();   // EF6 DbContext is not thread-safe — one context per task
        var repo = new TemplateRepository(ctx);
        return await repo.PublishVersionAsync(t.Id, new TemplateVersion { TemplateId = t.Id, Body = $"body {i}" });
    });
    var versions = await Task.WhenAll(tasks);

    versions.Select(v => v.VersionNumber).Distinct().Count().Should().Be(5);
}

private static TemplateBuilderDbContext CreateContextNoRecreate()
{
    var ctx = new TemplateBuilderDbContext(
        "Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;");
    ctx.Database.Initialize(force: false);   // schema already exists — must NOT re-run DropCreateDatabaseAlways mid-test
    return ctx;
}

[Fact]
public async Task Template_status_and_draft_body_persist()
{
    using var ctx = CreateContext();
    var repo = new TemplateRepository(ctx);
    var t = await repo.CreateAsync(new Template { Name = "Status Persist", TemplateType = "Email" });
    t.Status = TemplateStatus.Review;
    t.DraftBody = "draft";
    t.ReviewComment = "nope";
    await repo.UpdateTemplateAsync(t);

    var fetched = await repo.GetByIdAsync(t.Id);
    fetched!.Status.Should().Be(TemplateStatus.Review);
    fetched.DraftBody.Should().Be("draft");
    fetched.ReviewComment.Should().Be("nope");
}

private static async Task<Template> CreateTemplate(string name)
{
    using var ctx = CreateContext();
    return await new TemplateRepository(ctx).CreateAsync(new Template { Name = name, TemplateType = "Email" });
}
```

Append to `tests/TemplateBuilder.Infrastructure.EF6.Tests/SnippetRepositoryTests.cs`:

```csharp
[Fact]
public async Task UpdateWithVersionAsync_creates_version_when_body_changes()
{
    using var ctx = CreateContext();
    var repo = new SnippetRepository(ctx);
    var s = await repo.CreateAsync(new Snippet { Name = "V Snippet", Description = "d", Body = "v1" });

    s.Body = "v2";
    await repo.UpdateWithVersionAsync(s, "v1", "second version", "bob");

    var versions = await repo.GetVersionHistoryAsync(s.Id);
    versions.Should().HaveCount(2);
    versions[0].VersionNumber.Should().Be(1);
    versions[1].VersionNumber.Should().Be(2);
    versions[1].Body.Should().Be("v2");
    versions[1].CreatedBy.Should().Be("bob");
    (await repo.GetByIdAsync(s.Id))!.Body.Should().Be("v2");
}

[Fact]
public async Task UpdateWithVersionAsync_skips_version_when_body_unchanged()
{
    using var ctx = CreateContext();
    var repo = new SnippetRepository(ctx);
    var s = await repo.CreateAsync(new Snippet { Name = "No Change Snippet", Description = "d", Body = "same" });

    await repo.UpdateWithVersionAsync(s, "same", "no change", "bob");

    (await repo.GetVersionHistoryAsync(s.Id)).Should().HaveCount(0);
}

[Fact]
public async Task RestoreVersionAsync_creates_new_version_with_restored_body()
{
    using var ctx = CreateContext();
    var repo = new SnippetRepository(ctx);
    var s = await repo.CreateAsync(new Snippet { Name = "Restore Snippet", Description = "d", Body = "v1" });
    s.Body = "v2";
    await repo.UpdateWithVersionAsync(s, "v1", "second", "bob");
    var versions = await repo.GetVersionHistoryAsync(s.Id);
    var v1 = versions[0];

    var restored = await repo.RestoreVersionAsync(s.Id, v1.Id, "bob");

    restored.Body.Should().Be("v1");
    var after = await repo.GetVersionHistoryAsync(s.Id);
    after.Should().HaveCount(3);
    after[2].VersionNumber.Should().Be(3);
    after[2].ChangeComment.Should().Be("Restored from v1");
}

[Fact]
public async Task RecordUsageAsync_and_GetUsageStatsAsync_report_inserts()
{
    using var ctx = CreateContext();
    var repo = new SnippetRepository(ctx);
    var s = await repo.CreateAsync(new Snippet { Name = "Used Snippet", Description = "d", Body = "b" });

    await repo.RecordUsageAsync(s.Id, 11, "bob");
    await repo.RecordUsageAsync(s.Id, 11, "bob");
    await repo.RecordUsageAsync(s.Id, 12, "alice");

    var stats = await repo.GetUsageStatsAsync();
    var stat = stats.Single(x => x.SnippetId == s.Id);
    stat.UsageCount.Should().Be(3);
    stat.TemplateCount.Should().Be(2);
    stat.LastUsedAt.Should().NotBeNull();
}
```

Create `tests/TemplateBuilder.Infrastructure.EF6.Tests/AuditRepositoryTests.cs`:

```csharp
using FluentAssertions;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Infrastructure.EF6.Tests;

[Collection("Database")]
public class AuditRepositoryTests
{
    private static TemplateBuilderDbContext CreateContext()
    {
        var ctx = new TemplateBuilderDbContext(
            "Server=localhost,1433;Database=TemplateBuilderMvc5Tests;User Id=sa;Password=TemplateBuilder!2026;TrustServerCertificate=True;");
        Database.SetInitializer(new DropCreateDatabaseAlways<TemplateBuilderDbContext>());
        ctx.Database.Initialize(force: true);
        return ctx;
    }

    [Fact]
    public async Task Add_then_query_round_trips()
    {
        using var ctx = CreateContext();
        var repo = new AuditRepository(ctx);
        await repo.AddAsync(new AuditLog
        {
            EntityType = "Template", EntityId = 3, Action = AuditActions.Published,
            Actor = "bob", OccurredAt = DateTime.UtcNow, AfterState = "{}"
        });

        var rows = await repo.QueryAsync(new AuditQuery { EntityType = "Template", EntityId = 3 });
        rows.Should().ContainSingle(a => a.Action == AuditActions.Published && a.Actor == "bob");
    }

    [Fact]
    public async Task Query_filters_by_action_and_actor()
    {
        using var ctx = CreateContext();
        var repo = new AuditRepository(ctx);
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Created, Actor = "bob", OccurredAt = DateTime.UtcNow });
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.Approved, Actor = "alice", OccurredAt = DateTime.UtcNow });

        var rows = await repo.QueryAsync(new AuditQuery { EntityId = 1, Action = AuditActions.Created });
        rows.Should().ContainSingle(a => a.Actor == "bob");

        (await repo.CountAsync(new AuditQuery { EntityId = 1 })).Should().Be(2);
    }

    [Fact]
    public async Task GetLastOccurrence_returns_most_recent()
    {
        using var ctx = CreateContext();
        var repo = new AuditRepository(ctx);
        var old = DateTime.UtcNow.AddHours(-2);
        var recent = DateTime.UtcNow.AddMinutes(-1);
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.DraftSaved, Actor = "bob", OccurredAt = old });
        await repo.AddAsync(new AuditLog { EntityType = "Template", EntityId = 1, Action = AuditActions.DraftSaved, Actor = "bob", OccurredAt = recent });

        var last = await repo.GetLastOccurrenceAsync("Template", 1, AuditActions.DraftSaved);
        last.Should().BeCloseTo(recent, TimeSpan.FromSeconds(2));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj`
Expected: FAIL — new members on interfaces have no implementations; new entities not mapped.

- [ ] **Step 3: Write the implementation**

**DbContext** — modify `TemplateBuilderDbContext.cs`:

Add DbSets and mappings. Full new content of the class (replace in place):

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
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<SnippetVersion> SnippetVersions { get; set; } = null!;
    public DbSet<SnippetUsage> SnippetUsages { get; set; } = null!;

    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        var template = modelBuilder.Entity<Template>();
        template.ToTable("Templates");
        template.HasKey(t => t.Id);
        template.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_Templates_Name") { IsUnique = true }));
        template.Property(t => t.TemplateType).IsRequired().HasMaxLength(50);
        template.Property(t => t.Description).HasMaxLength(500);
        template.Property(t => t.RowVersion).IsRowVersion();
        template.Property(t => t.ReviewComment).HasMaxLength(1000);
        template.HasMany(t => t.Versions)
            .WithRequired(v => v.Template)
            .HasForeignKey(v => v.TemplateId)
            .WillCascadeOnDelete(false);
        template.HasOptional(t => t.CurrentVersion)
            .WithMany()
            .HasForeignKey(t => t.CurrentVersionId)
            .WillCascadeOnDelete(false);

        var version = modelBuilder.Entity<TemplateVersion>();
        version.ToTable("TemplateVersions");
        version.HasKey(v => v.Id);
        version.Property(v => v.Body).IsRequired();
        version.Property(v => v.ChangeComment).HasMaxLength(500);
        version.Property(v => v.CreatedBy).HasMaxLength(200);
        version.Property(v => v.TemplateId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_TemplateVersions_TemplateId_VersionNumber", 0) { IsUnique = true }));
        version.Property(v => v.VersionNumber)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_TemplateVersions_TemplateId_VersionNumber", 1) { IsUnique = true }));

        var snippet = modelBuilder.Entity<Snippet>();
        snippet.ToTable("Snippets");
        snippet.HasKey(s => s.Id);
        snippet.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_Snippets_Name") { IsUnique = true }));
        snippet.Property(s => s.Description).HasMaxLength(500);
        snippet.Property(s => s.Body).IsRequired();
        snippet.Property(s => s.RowVersion).IsRowVersion();
        snippet.HasMany(s => s.Versions)
            .WithRequired(v => v.Snippet)
            .HasForeignKey(v => v.SnippetId)
            .WillCascadeOnDelete(false);

        var snippetVersion = modelBuilder.Entity<SnippetVersion>();
        snippetVersion.ToTable("SnippetVersions");
        snippetVersion.HasKey(v => v.Id);
        snippetVersion.Property(v => v.Body).IsRequired();
        snippetVersion.Property(v => v.ChangeComment).HasMaxLength(500);
        snippetVersion.Property(v => v.CreatedBy).HasMaxLength(200);
        snippetVersion.Property(v => v.SnippetId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetVersions_SnippetId_VersionNumber", 0) { IsUnique = true }));
        snippetVersion.Property(v => v.VersionNumber)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetVersions_SnippetId_VersionNumber", 1) { IsUnique = true }));

        var audit = modelBuilder.Entity<AuditLog>();
        audit.ToTable("AuditLogs");
        audit.HasKey(a => a.Id);
        audit.Property(a => a.EntityType).IsRequired().HasMaxLength(20);
        audit.Property(a => a.Action).IsRequired().HasMaxLength(40);
        audit.Property(a => a.Actor).IsRequired().HasMaxLength(200);
        audit.Property(a => a.BeforeState).HasMaxLength(4000);
        audit.Property(a => a.AfterState).HasMaxLength(4000);
        audit.Property(a => a.Comment).HasMaxLength(1000);
        audit.Property(a => a.EntityId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_Entity", 0)));
        audit.Property(a => a.EntityType)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_Entity", 1)));
        audit.Property(a => a.OccurredAt)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_Entity", 2)));
        audit.Property(a => a.OccurredAt)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_OccurredAt")));

        var usage = modelBuilder.Entity<SnippetUsage>();
        usage.ToTable("SnippetUsages");
        usage.HasKey(u => u.Id);
        usage.Property(u => u.UsedBy).HasMaxLength(200);
        usage.Property(u => u.SnippetId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetUsages_SnippetId")));
        usage.Property(u => u.TemplateId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetUsages_TemplateId")));
    }
}
```

Note: `Snippet` needs a `Versions` navigation property. Add it in `Snippet.cs` alongside the existing properties:

```csharp
    public ICollection<SnippetVersion> Versions { get; set; } = new List<SnippetVersion>();
```

**Migration** — create `src/TemplateBuilder.Infrastructure.EF6/Migrations/AddGovernance.cs` + `.Designer.cs` + `.resx` (mirrors the repo's REAL migration pattern: every existing migration — InitialCreate, AddSampleDataToTemplates — has a Designer implementing `IMigrationMetadata` with a resx Target snapshot; EF6 discovers migrations via `IMigrationMetadata` and `MigrateDatabaseToLatestVersion`'s model-compat check needs the Target blob. A Designer-less `DbMigration` is silently skipped at runtime (`AutomaticMigrationsDisabledException`). Generate the Designer+resx with the repo's MigrationScaffolder recipe; the Up/Down code below is the plain half):

```csharp
namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    public partial class AddGovernance : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Templates", "Status", c => c.Int(nullable: false, defaultValue: 3));
            AddColumn("dbo.Templates", "DraftBody", c => c.String());
            AddColumn("dbo.Templates", "ReviewComment", c => c.String(maxLength: 1000));
            AddColumn("dbo.Snippets", "RowVersion", c => c.Binary(nullable: false, defaultValueSql: "0x00000000000007D0"));

            CreateIndex("dbo.TemplateVersions", new[] { "TemplateId", "VersionNumber" }, unique: true, name: "IX_TemplateVersions_TemplateId_VersionNumber");

            CreateTable(
                "dbo.AuditLogs",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    EntityType = c.String(nullable: false, maxLength: 20),
                    EntityId = c.Int(nullable: false),
                    Action = c.String(nullable: false, maxLength: 40),
                    Actor = c.String(nullable: false, maxLength: 200),
                    OccurredAt = c.DateTime(nullable: false),
                    BeforeState = c.String(maxLength: 4000),
                    AfterState = c.String(maxLength: 4000),
                    Comment = c.String(maxLength: 1000)
                })
                .PrimaryKey(t => t.Id);
            CreateIndex("dbo.AuditLogs", new[] { "EntityType", "EntityId", "OccurredAt" }, name: "IX_AuditLogs_Entity");
            CreateIndex("dbo.AuditLogs", new[] { "OccurredAt" }, name: "IX_AuditLogs_OccurredAt");

            CreateTable(
                "dbo.SnippetVersions",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    SnippetId = c.Int(nullable: false),
                    VersionNumber = c.Int(nullable: false),
                    Body = c.String(nullable: false),
                    ChangeComment = c.String(maxLength: 500),
                    CreatedAt = c.DateTime(nullable: false),
                    CreatedBy = c.String(maxLength: 200)
                })
                .PrimaryKey(t => t.Id);
            CreateIndex("dbo.SnippetVersions", new[] { "SnippetId", "VersionNumber" }, unique: true, name: "IX_SnippetVersions_SnippetId_VersionNumber");

            CreateTable(
                "dbo.SnippetUsages",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    SnippetId = c.Int(nullable: false),
                    TemplateId = c.Int(nullable: false),
                    UsedAt = c.DateTime(nullable: false),
                    UsedBy = c.String(maxLength: 200)
                })
                .PrimaryKey(t => t.Id);
            CreateIndex("dbo.SnippetUsages", new[] { "SnippetId" }, name: "IX_SnippetUsages_SnippetId");
            CreateIndex("dbo.SnippetUsages", new[] { "TemplateId" }, name: "IX_SnippetUsages_TemplateId");

            Sql(@"INSERT INTO dbo.SnippetVersions (SnippetId, VersionNumber, Body, ChangeComment, CreatedAt, CreatedBy)
                 SELECT Id, 1, Body, 'Initial version', CreatedAt, NULL FROM dbo.Snippets");
        }

        public override void Down()
        {
            DropIndex("dbo.SnippetUsages", "IX_SnippetUsages_TemplateId");
            DropIndex("dbo.SnippetUsages", "IX_SnippetUsages_SnippetId");
            DropTable("dbo.SnippetUsages");
            DropIndex("dbo.SnippetVersions", "IX_SnippetVersions_SnippetId_VersionNumber");
            DropTable("dbo.SnippetVersions");
            DropIndex("dbo.AuditLogs", "IX_AuditLogs_OccurredAt");
            DropIndex("dbo.AuditLogs", "IX_AuditLogs_Entity");
            DropTable("dbo.AuditLogs");
            DropIndex("dbo.TemplateVersions", "IX_TemplateVersions_TemplateId_VersionNumber");
            DropColumn("dbo.Snippets", "RowVersion");
            DropColumn("dbo.Templates", "ReviewComment");
            DropColumn("dbo.Templates", "DraftBody");
            DropColumn("dbo.Templates", "Status");
        }
    }
}
```

**TemplateRepository** — replace `PublishVersionAsync` (lines 62–74) with the atomic, transactional version; keep everything else:

```csharp
    public async Task<TemplateVersion> PublishVersionAsync(int templateId, TemplateVersion version, Action<Template>? updateTemplate = null, CancellationToken ct = default)
    {
        version.CreatedAt = DateTime.UtcNow;

        for (var attempt = 0; ; attempt++)
        {
            var max = await _db.TemplateVersions.Where(v => v.TemplateId == templateId)
                .Select(v => (int?)v.VersionNumber).MaxAsync(ct);
            version.VersionNumber = (max ?? 0) + 1;

            using var tx = _db.Database.BeginTransaction();   // fresh transaction per attempt
            try
            {
                _db.TemplateVersions.Add(version);
                await _db.SaveChangesAsync(ct);

                var template = await _db.Templates.FirstAsync(t => t.Id == templateId, ct);
                template.CurrentVersionId = version.Id;
                template.UpdatedAt = DateTime.UtcNow;
                updateTemplate?.Invoke(template);
                await _db.SaveChangesAsync(ct);

                tx.Commit();
                return version;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // Unique (TemplateId, VersionNumber) violation from a concurrent publish — retry with a fresh number.
                _db.Entry(version).State = EntityState.Detached;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
```

Add the `using` for `System.Data.Entity` if not present at the top of the file.

**SnippetRepository** — replace the whole file body with:

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

    public async Task<Snippet> UpdateWithVersionAsync(Snippet snippet, string oldBody, string? changeComment, string actor, CancellationToken ct = default)
    {
        snippet.UpdatedAt = DateTime.UtcNow;

        using var tx = _db.Database.BeginTransaction();
        try
        {
            if (!string.Equals(snippet.Body, oldBody, StringComparison.Ordinal))
            {
                var max = await _db.SnippetVersions.Where(v => v.SnippetId == snippet.Id)
                    .Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0;
                _db.SnippetVersions.Add(new SnippetVersion
                {
                    SnippetId = snippet.Id,
                    VersionNumber = max + 1,
                    Body = snippet.Body,
                    ChangeComment = changeComment,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = actor
                });
            }

            _db.Entry(snippet).State = EntityState.Modified;
            await _db.SaveChangesAsync(ct);
            tx.Commit();
            return snippet;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<SnippetVersion>> GetVersionHistoryAsync(int snippetId, CancellationToken ct = default)
        => await _db.SnippetVersions
            .Where(v => v.SnippetId == snippetId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync(ct);

    public async Task<SnippetVersion?> GetVersionAsync(int snippetId, int versionId, CancellationToken ct = default)
        => await _db.SnippetVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.SnippetId == snippetId, ct);

    public async Task<Snippet> RestoreVersionAsync(int snippetId, int sourceVersionId, string actor, CancellationToken ct = default)
    {
        var source = await GetVersionAsync(snippetId, sourceVersionId, ct);
        if (source is null)
            throw new InvalidOperationException($"Version {sourceVersionId} not found for snippet {snippetId}.");

        var snippet = await GetByIdAsync(snippetId, ct);
        if (snippet is null)
            throw new InvalidOperationException($"Snippet {snippetId} not found.");

        var oldBody = snippet.Body;
        snippet.Body = source.Body;
        return await UpdateWithVersionAsync(snippet, oldBody, $"Restored from v{source.VersionNumber}", actor, ct);
    }

    public async Task RecordUsageAsync(int snippetId, int templateId, string actor, CancellationToken ct = default)
    {
        _db.SnippetUsages.Add(new SnippetUsage
        {
            SnippetId = snippetId,
            TemplateId = templateId,
            UsedAt = DateTime.UtcNow,
            UsedBy = actor
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SnippetUsageStats>> GetUsageStatsAsync(CancellationToken ct = default)
    {
        var grouped = await _db.SnippetUsages
            .GroupBy(u => u.SnippetId)
            .Select(g => new
            {
                SnippetId = g.Key,
                UsageCount = g.Count(),
                TemplateCount = g.Select(x => x.TemplateId).Distinct().Count(),
                LastUsedAt = (DateTime?)g.Max(x => x.UsedAt)
            })
            .ToListAsync(ct);

        return grouped.Select(g => new SnippetUsageStats(g.SnippetId, g.UsageCount, g.TemplateCount, g.LastUsedAt))
            .Cast<SnippetUsageStats>()
            .ToList();
    }
}
```

**AuditRepository** — create `src/TemplateBuilder.Infrastructure.EF6/Repositories/AuditRepository.cs`:

```csharp
using System.Data.Entity;
using System.Linq;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly TemplateBuilderDbContext _db;

    public AuditRepository(TemplateBuilderDbContext db) => _db = db;

    public async Task AddAsync(AuditLog entry, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DateTime?> GetLastOccurrenceAsync(string entityType, int entityId, string action, CancellationToken ct = default)
        => await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId && a.Action == action)
            .Select(a => (DateTime?)a.OccurredAt)
            .OrderByDescending(a => a)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AuditLog>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var q = ApplyFilters(query);
        q = q.OrderByDescending(a => a.OccurredAt);
        var rows = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return rows;
    }

    public async Task<int> CountAsync(AuditQuery query, CancellationToken ct = default)
        => await ApplyFilters(query).CountAsync(ct);

    private IQueryable<AuditLog> ApplyFilters(AuditQuery query)
    {
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (query.EntityId.HasValue) q = q.Where(a => a.EntityId == query.EntityId.Value);
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.Actor)) q = q.Where(a => a.Actor.Contains(query.Actor));
        if (query.From.HasValue) q = q.Where(a => a.OccurredAt >= query.From.Value);
        if (query.To.HasValue) q = q.Where(a => a.OccurredAt <= query.To.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.Action.Contains(query.Search) || a.Actor.Contains(query.Search) || (a.Comment != null && a.Comment.Contains(query.Search)));
        return q;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TemplateBuilder.Infrastructure.EF6.Tests/TemplateBuilder.Infrastructure.EF6.Tests.csproj`
Expected: PASS. Note: `DropCreateDatabaseAlways` recreates the schema from the model each run, so the migration path itself is NOT exercised by these tests — the migration is validated by the sample-host startup (`MigrateDatabaseToLatestVersion`) in Task 9.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Infrastructure.EF6 tests/TemplateBuilder.Infrastructure.EF6.Tests
git commit -m "feat: EF6 persistence for governance (audit, snippet versions/usage, atomic publish)"
```

---

### Task 5: TemplatesController — workflow endpoints, server draft, audit wiring, actor helper

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplateBuilderControllerBase.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/TemplatesController.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs`

**Interfaces:**
- Consumes: `ITemplateWorkflowService`, `IAuditService` (Tasks 2–3).
- Produces: HTTP endpoints consumed by the editor JS (Task 7).

- [ ] **Step 1: Write the failing tests**

There is no Editor test project in this repo (controllers are verified by browser smoke, per repo convention). Write the endpoints, then verify in Task 9. For this task, "failing" is defined as the project not compiling against the new interfaces — so write the implementation next and rely on the Task 9 regression. (This is an explicit, documented deviation from strict TDD for the MVC5 controller layer only.)

- [ ] **Step 2: Implement the actor helper**

In `TemplateBuilderControllerBase.cs`, add after the constructor area (new member):

```csharp
    protected string CurrentActor => User?.Identity?.Name ?? "anonymous";
```

- [ ] **Step 3: Wire dependency injection**

In `UnityContainerExtensions.cs`, add registrations alongside the existing ones:

```csharp
        container.RegisterType<IAuditRepository, AuditRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<IAuditService, AuditService>(new ContainerControlledLifetimeManager());
        container.RegisterType<ITemplateWorkflowService, TemplateWorkflowService>(new HierarchicalLifetimeManager());
```

- [ ] **Step 4: Implement workflow + draft endpoints in TemplatesController**

Inject `ITemplateWorkflowService`, `IAuditService`, and `IAuditRepository` via the constructor (extend the existing constructor). Add these actions (routes match the spec). The controller already imports `System.Data.Entity.Infrastructure` (existing SaveVersion uses it) — concurrency conflicts from the workflow's repository calls are caught here, since the Application layer cannot reference EF6:

```csharp
    [Route("Templates/{id:int}/Draft")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> SaveDraft(int id)
    {
        var request = await Request.ReadJsonBodyAsync<SaveDraftRequest>();
        return await RunWorkflow(() => _workflow.SaveDraftAsync(id, request?.Body ?? string.Empty, CurrentActor));
    }

    [Route("Templates/{id:int}/SubmitForReview")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> SubmitForReview(int id)
    {
        var request = await Request.ReadJsonBodyAsync<SubmitForReviewRequest>();
        return await RunWorkflow(() => _workflow.SubmitForReviewAsync(id, request?.Body ?? string.Empty, CurrentActor));
    }

    [Route("Templates/{id:int}/Approve")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Approve(int id)
        => await RunWorkflow(() => _workflow.ApproveAsync(id, CurrentActor));

    [Route("Templates/{id:int}/Reject")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Reject(int id)
    {
        var request = await Request.ReadJsonBodyAsync<RejectRequest>();
        return await RunWorkflow(() => _workflow.RejectAsync(id, request?.Comment ?? string.Empty, CurrentActor));
    }

    [Route("Templates/{id:int}/CancelReview")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> CancelReview(int id)
        => await RunWorkflow(() => _workflow.CancelReviewAsync(id, CurrentActor));

    [Route("Templates/{id:int}/Publish")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Publish(int id)
        => await RunWorkflow(() => _workflow.PublishAsync(id, CurrentActor));

    [Route("Templates/{id:int}/Audit")]
    [HttpGet]
    public async Task<ActionResult> GetAuditTimeline(int id)
    {
        var rows = await _auditRepository.QueryAsync(new AuditQuery { EntityType = "Template", EntityId = id, PageSize = 100 });
        return JsonOk(rows.Select(a => new { a.Id, a.Action, a.Actor, a.OccurredAt, a.Comment }));
    }

    private async Task<ActionResult> RunWorkflow(Func<Task<TemplateWorkflowResult>> action)
    {
        try
        {
            return MapWorkflowResult(await action());
        }
        catch (DbUpdateConcurrencyException)
        {
            return JsonError(409, new ErrorResult("CONFLICT", "This template was modified by another user. Please refresh and try again."));
        }
    }

    private ActionResult MapWorkflowResult(TemplateWorkflowResult result)
    {
        if (result.Success) return JsonOk(new { status = result.Template?.Status.ToString() });
        return result.ErrorCode switch
        {
            "NOT_FOUND" => JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", result.ErrorMessage!)),
            "CONFLICT" => JsonError(409, new ErrorResult("CONFLICT", result.ErrorMessage!)),
            _ => JsonError(400, new ErrorResult("VALIDATION_ERROR", result.ErrorMessage!))
        };
    }
```

Note: `_auditRepository` is needed in the controller for the timeline query — inject `IAuditRepository` (not just `IAuditService`) into the controller.

Add the request DTOs to the controller file's Models section (the file uses inline request classes):

```csharp
public class SaveDraftRequest { public string? Body { get; set; } }
public class SubmitForReviewRequest { public string? Body { get; set; } }
public class RejectRequest { public string? Comment { get; set; } }
```

- [ ] **Step 5: Wire audit recording into existing actions**

In `TemplatesController`:
- `CreateTemplateJson` — after successful create, before returning: `await _audit.RecordAsync("Template", template.Id, AuditActions.Created, CurrentActor, afterState: $"{{\"name\":\"{template.Name}\"}}");`
- `SaveVersion` — after `PublishVersionAsync` succeeds: `await _audit.RecordAsync("Template", id, AuditActions.Published, CurrentActor, afterState: $"{{\"versionNumber\":{version.VersionNumber},\"versionId\":{version.Id}}}");`
- `RestoreVersion` — after publish succeeds: `await _audit.RecordAsync("Template", id, AuditActions.Restored, CurrentActor, comment: $"Restored from v{sourceVersionNumber}");`
- `ToggleActive` — after update: `await _audit.RecordAsync("Template", id, AuditActions.ToggledActive, CurrentActor, afterState: $"{{\"isActive\":{template.IsActive.ToString().ToLowerInvariant()}}}");`
- `Duplicate` — after create: `await _audit.RecordAsync("Template", newTemplate.Id, AuditActions.Duplicated, CurrentActor, comment: $"Duplicated from template {source.Id}");`

- [ ] **Step 6: Edit GET exposes workflow state**

Modify the `Edit(int id)` action to populate the view model with workflow fields. Extend `TemplateEditorViewModel` (in `src/TemplateBuilder.Editor.Mvc5/Models/`):

```csharp
    public string Status { get; set; } = "Draft";
    public string? DraftBody { get; set; }
    public string? ReviewComment { get; set; }
```

and set them in `Edit`:

```csharp
            Status = template.Status.ToString(),
            DraftBody = template.DraftBody,
            ReviewComment = template.ReviewComment,
            Body = template.Status == TemplateStatus.Review || template.Status == TemplateStatus.Approved
                ? template.DraftBody ?? template.CurrentVersion?.Body ?? string.Empty
                : template.DraftBody ?? template.CurrentVersion?.Body ?? string.Empty,
```

- [ ] **Step 7: Build**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`
Expected: compiles. Fix any namespace/using gaps (the controller needs `using TemplateBuilder.Application.Services;` for `TemplateWorkflowResult` and `TemplateStatus` from Domain).

- [ ] **Step 8: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5
git commit -m "feat: workflow endpoints, server-side draft, and audit wiring on template actions"
```

---

### Task 6: SnippetsController — edit with versioning, history, restore, usage

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Controllers/SnippetsController.cs`
- Modify: `src/TemplateBuilder.Editor.Mvc5/UnityContainerExtensions.cs` (if actor/audit needs registration — audit service already registered in Task 5)
- Test: `tests/TemplateBuilder.Infrastructure.EF6.Tests/SnippetRepositoryTests.cs` (already covered repository behavior in Task 4)

**Interfaces:**
- Consumes: extended `ISnippetRepository` (Task 4), `IAuditService` (Task 2).
- Produces: endpoints consumed by the editor snippet list (Task 7).

- [ ] **Step 1: Implement the endpoints**

Add to `SnippetsController` (extend the constructor with `IAuditService`):

```csharp
    [Route("Templates/Api/Snippets/{id:int}")]
    [HttpPut, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Update(int id)
    {
        var request = await Request.ReadJsonBodyAsync<UpdateSnippetRequest>();
        if (string.IsNullOrWhiteSpace(request.Name))
            return JsonError(400, new Models.ErrorResult("INVALID_NAME", "Snippet name is required."));
        if (string.IsNullOrWhiteSpace(request.Body))
            return JsonError(400, new Models.ErrorResult("INVALID_BODY", "Snippet content cannot be empty."));

        var snippet = await _snippets.GetByIdAsync(id);
        if (snippet is null) return JsonError(404, new Models.ErrorResult("NOT_FOUND", "Snippet not found."));

        var oldBody = snippet.Body;
        snippet.Name = request.Name.Trim();
        snippet.Description = request.Description?.Trim();
        snippet.Body = request.Body;

        try
        {
            var updated = await _snippets.UpdateWithVersionAsync(snippet, oldBody, request.ChangeComment, CurrentActor);
            await _audit.RecordAsync("Snippet", id, AuditActions.SnippetEdited, CurrentActor, comment: request.ChangeComment);
            return JsonOk(new { id = updated.Id, updated.Name });
        }
        catch (DbUpdateConcurrencyException)
        {
            return JsonError(409, new Models.ErrorResult("CONFLICT", "This snippet was modified by another user. Please refresh and try again."));
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new Models.ErrorResult("DUPLICATE_NAME", $"A snippet named '{request.Name.Trim()}' already exists."));
        }
    }

    [Route("Templates/Api/Snippets/{id:int}/Versions")]
    [HttpGet]
    public async Task<ActionResult> GetVersions(int id)
    {
        var versions = await _snippets.GetVersionHistoryAsync(id);
        return JsonOk(versions.Select(v => new { v.Id, v.VersionNumber, v.Body, v.ChangeComment, v.CreatedAt, v.CreatedBy }));
    }

    [Route("Templates/Api/Snippets/{id:int}/Restore/{versionId:int}")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> RestoreVersion(int id, int versionId)
    {
        try
        {
            var restored = await _snippets.RestoreVersionAsync(id, versionId, CurrentActor);
            await _audit.RecordAsync("Snippet", id, AuditActions.SnippetRestored, CurrentActor, comment: $"Restored version {versionId}");
            return JsonOk(new { id = restored.Id, restored.Name });
        }
        catch (InvalidOperationException)
        {
            return JsonError(404, new Models.ErrorResult("NOT_FOUND", "Snippet or version not found."));
        }
    }

    [Route("Templates/Api/Snippets/{id:int}/Usage")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> RecordUsage(int id, int templateId)
    {
        // MVC5 binds int templateId from the query string automatically (no [FromUri] — that's Web API)
        await _snippets.RecordUsageAsync(id, templateId, CurrentActor);
        return NoContentResult();
    }
```

Add the DTOs at the end of the file:

```csharp
public class UpdateSnippetRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Body { get; set; }
    public string? ChangeComment { get; set; }
}
```

Wire audit into the existing `Create` and `Delete` actions:
- Create: after `CreateAsync` succeeds: `await _audit.RecordAsync("Snippet", created.Id, AuditActions.SnippetCreated, CurrentActor);`
- Delete: after `DeleteAsync`: `await _audit.RecordAsync("Snippet", id, AuditActions.SnippetDeleted, CurrentActor);`

- [ ] **Step 2: Build**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`
Expected: compiles.

- [ ] **Step 3: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Controllers/SnippetsController.cs
git commit -m "feat: snippet editing with versioning, history, restore, and usage tracking"
```

---

### Task 7: Editor UI — status pill, lock banners, workflow actions, server draft, timeline

**Files:**
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml`
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.js`
- Modify: `src/TemplateBuilder.Editor.Mvc5/StaticAssets/template-editor.css`

**Interfaces:**
- Consumes: all endpoints from Tasks 5–6; view-model fields from Task 5 Step 6.
- Produces: the interactive editor experience.

- [ ] **Step 1: Edit.cshtml — status pill, banner, workflow buttons, timeline container**

In the editor header/toolbar area (near the existing Save/Preview buttons), add:

```html
<span id="tb-status-pill" class="tb-status-pill" data-status="@Model.Status">@Model.Status</span>
```

Above the editor body, add the lock banner container (JS fills it; server-side `hidden` until JS decides):

```html
<div id="tb-lock-banner" class="tb-banner tb-banner--lock" hidden></div>
<div id="tb-review-comment" class="tb-banner tb-banner--info" hidden></div>
```

Near the create/save buttons, add the workflow action group (visible per status; JS toggles `hidden`):

```html
<div id="tb-workflow-actions" class="tb-workflow-actions" data-template-id="@Model.Id" data-status="@Model.Status" hidden>
    <button type="button" id="btn-submit-review" class="btn btn-primary">Submit for review</button>
    <button type="button" id="btn-approve" class="btn btn-success">Approve</button>
    <button type="button" id="btn-reject" class="btn btn-danger">Reject with feedback</button>
    <button type="button" id="btn-cancel-review" class="btn btn-ghost">Cancel review</button>
    <button type="button" id="btn-publish" class="btn btn-primary">Publish</button>
</div>
```

In the sidebar (next to the version history panel), add the timeline container:

```html
<div class="tb-panel" id="tb-timeline-panel" hidden>
    <h4 class="tb-panel-title">Activity timeline</h4>
    <div id="tb-timeline" class="tb-timeline"></div>
</div>
```

- [ ] **Step 2: template-editor.js — server draft autosave**

Replace the localStorage-only `saveDraft()` (lines ~2278–2289) with a server-first version. Keep the localStorage write as a crash-recovery cache:

```js
async function saveDraft() {
    if (!_isDirty || !isAutoSaveEnabled() || !_editor) return;
    const body = _editor.getContents();
    const sampleData = document.getElementById('preview-json')?.value ?? null;
    try {
        localStorage.setItem(DRAFT_KEY, JSON.stringify({ body, sampleData, timestamp: Date.now(), versionNumber: currentVersionNumber }));
        updateDraftStatus();
        if (templateId !== null && tbStatus !== 'Review' && tbStatus !== 'Approved') {
            const res = await fetch(`/Templates/${templateId}/Draft`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _csrf },
                body: JSON.stringify({ body })
            });
            if (!res.ok && res.status !== 409) {
                const err = await res.json().catch(() => ({}));
                console.warn('Draft save failed:', err.message || res.status);
            }
        }
    } catch { /* storage or network failure — silently skip */ }
}
```

At the top of the draft section, add the status globals read from the view model (Razor renders these into the page):

```html
<script>window.tbStatus = '@Model.Status'; window.tbTemplateId = @Model.Id; window.tbReviewComment = '@(Html.Raw(System.Web.HttpUtility.JavaScriptStringEncode(Model.ReviewComment ?? string.Empty)))'; window.tbIsCreate = @(Model.Id == 0 ? "true" : "false");</script>
```

In the editor init (`loadDraft`), prefer the server body over localStorage: the body is already server-rendered via `@Model.Body`; only use localStorage to *restore* a crash draft (existing banner flow), never to override the server body on a normal load. Remove the `loadDraft()` auto-application; keep the "restore draft" banner as an explicit user action.

- [ ] **Step 3: template-editor.js — workflow buttons + lock mode**

Add after the snippet wiring:

```js
// ── Workflow ────────────────────────────────────────────────────────────────
const tbStatus = window.tbStatus || 'Draft';
const isLocked = tbStatus === 'Review' || tbStatus === 'Approved';

function updateWorkflowUI() {
    const group = document.getElementById('tb-workflow-actions');
    if (!group) return;
    group.hidden = window.tbIsCreate;
    group.dataset.status = tbStatus;
    document.getElementById('btn-submit-review')?.toggleAttribute('hidden', tbStatus !== 'Draft');
    document.getElementById('btn-approve')?.toggleAttribute('hidden', tbStatus !== 'Review');
    document.getElementById('btn-reject')?.toggleAttribute('hidden', tbStatus !== 'Review');
    document.getElementById('btn-cancel-review')?.toggleAttribute('hidden', !(tbStatus === 'Review' || tbStatus === 'Approved'));
    document.getElementById('btn-publish')?.toggleAttribute('hidden', tbStatus !== 'Approved');

    const pill = document.getElementById('tb-status-pill');
    if (pill) { pill.dataset.status = tbStatus; pill.textContent = tbStatus; }

    const banner = document.getElementById('tb-lock-banner');
    const comment = document.getElementById('tb-review-comment');
    if (isLocked) {
        if (banner) {
            banner.hidden = false;
            banner.textContent = tbStatus === 'Review'
                ? 'This template is under review — editing is locked until it is approved or rejected.'
                : 'This template is approved — editing is locked. Publish to make it live.';
        }
        if (comment && window.tbReviewComment) {
            comment.hidden = false;
            comment.textContent = 'Review feedback: ' + window.tbReviewComment;
        }
        if (typeof _editor?.setReadOnly === 'function') _editor.setReadOnly(true);
        else document.querySelector('.sun-editor')?.classList.add('tb-editor-locked');
    } else {
        if (typeof _editor?.setReadOnly === 'function') _editor.setReadOnly(false);
        else document.querySelector('.sun-editor')?.classList.remove('tb-editor-locked');
    }
    document.getElementById('btn-create-submit')?.toggleAttribute('hidden', window.tbIsCreate || isLocked);
    document.getElementById('btn-save-version')?.toggleAttribute('hidden', isLocked);
    document.getElementById('btn-preview')?.toggleAttribute('hidden', isLocked);
}

async function workflowFetch(url, body) {
    const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': _csrf },
        body: body ? JSON.stringify(body) : undefined
    });
    if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        showToast(err.message || 'Action failed');
        return null;
    }
    return res.json();
}

document.getElementById('btn-submit-review')?.addEventListener('click', async () => {
    const body = _editor?.getContents() ?? '';
    if (!body.trim()) { showToast('Template body is empty.'); return; }
    const ok = await workflowFetch(`/Templates/${templateId}/SubmitForReview`, { body });
    if (ok) { showToast('Submitted for review.'); location.reload(); }
});

document.getElementById('btn-approve')?.addEventListener('click', async () => {
    const ok = await workflowFetch(`/Templates/${templateId}/Approve`);
    if (ok) { showToast('Approved — ready to publish.'); location.reload(); }
});

document.getElementById('btn-reject')?.addEventListener('click', async () => {
    const comment = prompt('Rejection feedback (optional):');
    if (comment === null) return;
    const ok = await workflowFetch(`/Templates/${templateId}/Reject`, { comment });
    if (ok) { showToast('Rejected — template returned to draft.'); location.reload(); }
});

document.getElementById('btn-cancel-review')?.addEventListener('click', async () => {
    const ok = await workflowFetch(`/Templates/${templateId}/CancelReview`);
    if (ok) { showToast('Review cancelled — editing unlocked.'); location.reload(); }
});

document.getElementById('btn-publish')?.addEventListener('click', async () => {
    if (!confirm('Publish the approved body as a new version?')) return;
    const ok = await workflowFetch(`/Templates/${templateId}/Publish`);
    if (ok) { showToast('Published.'); location.reload(); }
});

// Timeline
async function loadTimeline() {
    if (templateId === null) return;
    const res = await fetch(`/Templates/${templateId}/Audit`);
    if (!res.ok) return;
    const rows = await res.json();
    const panel = document.getElementById('tb-timeline-panel');
    const list = document.getElementById('tb-timeline');
    if (!panel || !list || !rows.length) { if (panel) panel.hidden = true; return; }
    panel.hidden = false;
    list.innerHTML = rows.map(r => `
        <div class="tb-timeline-item">
            <span class="tb-timeline-action">${escapeHtml(r.action)}</span>
            <span class="tb-timeline-meta">${escapeHtml(r.actor)} · ${new Date(r.occurredAt).toLocaleString()}</span>
            ${r.comment ? `<div class="tb-timeline-comment">${escapeHtml(r.comment)}</div>` : ''}
        </div>`).join('');
}

updateWorkflowUI();
loadTimeline();
```

Call `updateWorkflowUI()` after the editor initializes, and gate `markDirty()`/`saveDraft()` so locked templates never trigger a draft save or status change.

- [ ] **Step 4: template-editor.js — snippet history, usage, edit**

Replace the snippet `renderSnippets` list items to include usage + actions (history/edit), and add handlers:

```js
snippetList.innerHTML = snippets.map(s => `
    <div class="tb-snippet-item">
        <span class="tb-snippet-name" title="${escapeHtml(s.description || s.name)}">${escapeHtml(s.name)}</span>
        <span class="tb-snippet-meta">used ${s.usageCount ?? 0}x in ${s.templateCount ?? 0} templates</span>
        <div class="tb-snippet-actions">
            <button type="button" class="tb-snippet-insert" data-snippet-id="${s.id}" aria-label="Insert ${escapeHtml(s.name)}">Insert</button>
            <button type="button" class="tb-snippet-edit" data-snippet-id="${s.id}" aria-label="Edit ${escapeHtml(s.name)}">Edit</button>
            <button type="button" class="tb-snippet-history" data-snippet-id="${s.id}" aria-label="History ${escapeHtml(s.name)}">History</button>
            <button type="button" class="tb-snippet-delete" data-snippet-id="${s.id}" aria-label="Delete ${escapeHtml(s.name)}">✕</button>
        </div>
    </div>`).join('');
```

Usage data comes from a new endpoint the list load calls: change the snippet fetch to hit a combined endpoint that includes stats — add to SnippetsController a `GetAllWithStats` route (or extend `GetAll` to include stats). Implementation: in `GetAll`, fetch `GetUsageStatsAsync()` and join on snippet id. Update `GetAll` to return `{ id, name, description, body, usageCount, templateCount, lastUsedAt }`.

Insert handler (existing, ~line 1907) additionally records usage:

```js
if (snippet && _editor) {
    _editor.insertHTML(snippet.body);
    document.querySelector('.sun-editor-editable')?.focus();
    markDirty();
    if (templateId !== null) {
        fetch(`/Templates/Api/Snippets/${id}/Usage?templateId=${templateId}`, {
            method: 'POST', headers: { 'RequestVerificationToken': _csrf }
        }).catch(() => {});
    }
}
```

Snippet edit: open the existing save-snippet modal pre-filled; on save call the PUT endpoint. Snippet history: open a modal listing versions with a "Restore" button calling the restore endpoint. (Reuse the existing `save-snippet-modal` markup; add a `versions-modal` sibling.)

- [ ] **Step 5: template-editor.css — lock, banner, pill, timeline styles**

Append to `template-editor.css` (all rules under the existing `#tb-editor-host` scoping):

```css
#tb-editor-host .tb-status-pill { display:inline-block; padding:2px 10px; border-radius:10px; font-size:12px; font-weight:700; text-transform:uppercase; letter-spacing:.5px; }
#tb-editor-host .tb-status-pill[data-status="Draft"]     { background:#33507a; color:#cfe0ff; }
#tb-editor-host .tb-status-pill[data-status="Review"]    { background:#7a5c2a; color:#ffe2b0; }
#tb-editor-host .tb-status-pill[data-status="Approved"]  { background:#2a6a4a; color:#c0ffdd; }
#tb-editor-host .tb-status-pill[data-status="Published"] { background:#2a6a4a; color:#c0ffdd; }
#tb-editor-host .tb-banner { border-radius:6px; padding:8px 12px; font-size:13px; margin:8px 0; }
#tb-editor-host .tb-banner--lock { background:#3a2a10; border:1px solid #e8a13c; color:#e8c07a; }
#tb-editor-host .tb-banner--info { background:#10243e; border:1px solid #2a5a8a; color:#bcd8f5; }
#tb-editor-host .tb-editor-locked { pointer-events:none; opacity:.7; }
#tb-editor-host .tb-workflow-actions { display:flex; gap:8px; flex-wrap:wrap; margin:8px 0; }
#tb-editor-host .tb-timeline { display:flex; flex-direction:column; gap:8px; padding-left:4px; }
#tb-editor-host .tb-timeline-item { border-left:2px solid #24406e; padding-left:10px; }
#tb-editor-host .tb-timeline-action { font-weight:600; color:#dbe6ff; display:block; }
#tb-editor-host .tb-timeline-meta { font-size:11px; color:#6f84a8; }
#tb-editor-host .tb-timeline-comment { font-size:12px; color:#e8c07a; font-style:italic; }
#tb-editor-host .tb-snippet-meta { font-size:11px; color:#6f84a8; display:block; }
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`
Expected: compiles (RazorGenerator regenerates `obj/CodeGen` for the modified `.cshtml`).

- [ ] **Step 7: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Views/Templates/Edit.cshtml src/TemplateBuilder.Editor.Mvc5/StaticAssets
git commit -m "feat: editor UI for workflow, lock mode, server draft, timeline, and snippet governance"
```

---

### Task 8: Audit page — AuditController, view, CSV export

**Files:**
- Create: `src/TemplateBuilder.Editor.Mvc5/Controllers/AuditController.cs`
- Create: `src/TemplateBuilder.Editor.Mvc5/Views/Audit/Audit.cshtml`
- Modify: `src/TemplateBuilder.Editor.Mvc5/Views/Setup/_Setup.cshtml` (add a link)

**Interfaces:**
- Consumes: `IAuditRepository`, `AuditQuery` (Tasks 1, 4).

- [ ] **Step 1: Write the controller**

`src/TemplateBuilder.Editor.Mvc5/Controllers/AuditController.cs`:

```csharp
using System.Text;
using System.Web.Mvc;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class AuditController : TemplateBuilderControllerBase
{
    private readonly IAuditRepository _audit;

    public AuditController(IAuditRepository audit) => _audit = audit;

    [Route("Audit")]
    [HttpGet]
    public async Task<ActionResult> Index(string? entityType, string? action, string? actor,
        string? from, string? to, string? search, int page = 1)
    {
        var query = new AuditQuery
        {
            EntityType = entityType,
            Action = action,
            Actor = actor,
            From = ParseDate(from),
            To = ParseDate(to),
            Search = search,
            Page = page,
            PageSize = 25
        };

        var rows = await _audit.QueryAsync(query);
        var total = await _audit.CountAsync(query);

        return View(new AuditIndexViewModel
        {
            Rows = rows,
            Total = total,
            Page = page,
            PageSize = 25,
            EntityType = entityType,
            Action = action,
            Actor = actor,
            From = from,
            To = to,
            Search = search
        });
    }

    [Route("Audit/Export")]
    [HttpGet]
    public async Task<ActionResult> Export(string? entityType, string? action, string? actor,
        string? from, string? to, string? search)
    {
        var query = new AuditQuery
        {
            EntityType = entityType,
            Action = action,
            Actor = actor,
            From = ParseDate(from),
            To = ParseDate(to),
            Search = search,
            Page = 1,
            PageSize = 50000
        };

        var rows = await _audit.QueryAsync(query);

        var sb = new StringBuilder();
        sb.AppendLine("OccurredAt,EntityType,EntityId,Action,Actor,Comment");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                Quote(r.OccurredAt.ToString("u")),
                Quote(r.EntityType), r.EntityId.ToString(),
                Quote(r.Action), Quote(r.Actor), Quote(r.Comment ?? string.Empty)));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        Response.AddHeader("Content-Disposition", "attachment; filename=template-builder-audit.csv");
        return File(bytes, "text/csv");
    }

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, out var parsed) ? parsed : (DateTime?)null;

    private static string Quote(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
```

Add the view model to `src/TemplateBuilder.Editor.Mvc5/Models/` (or inline in the controller file):

```csharp
public class AuditIndexViewModel
{
    public IReadOnlyList<Domain.Entities.AuditLog> Rows { get; set; } = new List<Domain.Entities.AuditLog>();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? EntityType { get; set; }
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Search { get; set; }
}
```

- [ ] **Step 2: Write the view**

`src/TemplateBuilder.Editor.Mvc5/Views/Audit/Audit.cshtml`:

```html
@model TemplateBuilder.Editor.Mvc5.Models.AuditIndexViewModel
@{
    ViewBag.Title = "Audit Log";
}

<h2>Audit Log</h2>
<p class="subtitle">Append-only record of who did what and when. @Model.Total total events.</p>

<form method="get" action="@Url.Action("Index")" class="tb-audit-filters">
    <input type="text" name="search" value="@Model.Search" placeholder="Search action / actor / comment" />
    <select name="entityType">
        <option value="">All types</option>
        <option value="Template" selected='@(Model.EntityType == "Template")'>Templates</option>
        <option value="Snippet" selected='@(Model.EntityType == "Snippet")'>Snippets</option>
    </select>
    <input type="text" name="from" placeholder="From (yyyy-MM-dd)" value="@Model.From" />
    <input type="text" name="to" placeholder="To (yyyy-MM-dd)" value="@Model.To" />
    <button type="submit" class="btn btn-primary">Filter</button>
    <a class="btn btn-ghost" href="@Url.Action("Export", "Audit", new { search = Model.Search, entityType = Model.EntityType, from = Model.From, to = Model.To })">⬇ Export CSV</a>
</form>

<table class="tb-audit-table">
    <thead>
        <tr><th>When</th><th>Who</th><th>Action</th><th>Target</th><th>Comment</th></tr>
    </thead>
    <tbody>
    @foreach (var r in Model.Rows)
    {
        <tr>
            <td>@r.OccurredAt.ToLocalTime().ToString("g")</td>
            <td>@r.Actor</td>
            <td>@r.Action</td>
            <td>@r.EntityType #@r.EntityId</td>
            <td>@r.Comment</td>
        </tr>
    }
    </tbody>
</table>

@if (Model.Page > 1)
{
    <a class="btn btn-ghost" href="@Url.Action("Index", new { page = Model.Page - 1, search = Model.Search, entityType = Model.EntityType, from = Model.From, to = Model.To })">← Prev</a>
}
@if (Model.Page * Model.PageSize < Model.Total)
{
    <a class="btn btn-ghost" href="@Url.Action("Index", new { page = Model.Page + 1, search = Model.Search, entityType = Model.EntityType, from = Model.From, to = Model.To })">Next →</a>
}
```

- [ ] **Step 3: Link from Setup**

In `src/TemplateBuilder.Editor.Mvc5/Views/Setup/_Setup.cshtml`, add a navigation link to `/Audit` alongside the existing setup links.

- [ ] **Step 4: Build**

Run: `dotnet build src/TemplateBuilder.Editor.Mvc5/TemplateBuilder.Editor.Mvc5.csproj`
Expected: compiles.

- [ ] **Step 5: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/Controllers/AuditController.cs src/TemplateBuilder.Editor.Mvc5/Views/Audit src/TemplateBuilder.Editor.Mvc5/Views/Setup/_Setup.cshtml
git commit -m "feat: global audit view with filters and CSV export"
```

---

### Task 9: Regression — full suite + browser smoke

**Files:** none (verification only).

- [ ] **Step 1: Full build + test suite**

Run: `dotnet build`
Run: `dotnet test`
Expected: all suites pass. The EF6 suite recreates the schema from the model (not migrations), so also verify the migration path by starting the sample host (Step 2) — `MigrateDatabaseToLatestVersion` runs `AddGovernance` against the sample DB.

- [ ] **Step 2: Start xsp4 and smoke the migration + workflow**

Run the sample host on xsp4 (see repo docs for the start script; restart with `kill $(pgrep -x mono)` first).

Expected: sample host starts; `AddGovernance` applies to the sample DB (status column + new tables exist — check via sqlcmd or the app's setup probe). Existing templates appear as `Published`.

- [ ] **Step 3: Browser smoke — workflow happy path**

Using the headless Playwright script pattern from `/tmp/opencode/ui-create-test.js`:
1. Create a template → status pill shows `Draft`.
2. Enter body → Submit for review → status `Review`; editor read-only; banner visible.
3. Approve → status `Approved`; banner updates; Publish button visible.
4. Publish → status `Published`; a new version exists in history; timeline shows created → draft_saved → submitted → approved → published.
5. Edit the published template (change body, let autosave fire) → status returns to `Draft`.
6. Reject path: submit → reject with feedback → status `Draft`, feedback banner visible.

- [ ] **Step 4: Browser smoke — conflict + snippets + audit page**

1. Open the same template in two tabs; approve in one; in the other, attempt approve → 409 toast "modified by another user".
2. Snippet: create, edit (creates v2), restore v1; history shows 3 versions; insert into a template → usage count increments.
3. `/Audit` renders events; export downloads a CSV with the expected columns and rows.

- [ ] **Step 5: Update the package README**

Add a "Governance & Compliance" section to `src/TemplateBuilder.Editor.Mvc5/README.md` describing the workflow, audit log, and snippet governance; note the new endpoints.

- [ ] **Step 6: Commit**

```bash
git add src/TemplateBuilder.Editor.Mvc5/README.md
git commit -m "docs: document governance & compliance feature in README"
```

---

## Self-review checklist (executor)

1. **Spec coverage:** workflow transitions (Task 3/5/7) ✓ — server draft (Task 5/7) ✓ — lock semantics (Task 7) ✓ — audit entity/events/throttle/surfaces/CSV (Task 2/4/5/6/8) ✓ — atomic publish + snippet RowVersion + transition conflicts (Task 4) ✓ — snippet versions/usage (Task 4/6/7) ✓ — back-compat migration (Task 4) ✓ — non-goals honored (no role gating, no read tracking, no soft deletes) ✓.
2. **Placeholder scan:** no TBD/TODO; every step has concrete code.
3. **Type consistency:** `TemplateWorkflowResult`, `AuditQuery`, `SnippetUsageStats`, `UpdateWithVersionAsync`, `PublishVersionAsync(templateId, version, Action<Template>?, ct)` are defined once (Task 1/2/3) and consumed consistently later.