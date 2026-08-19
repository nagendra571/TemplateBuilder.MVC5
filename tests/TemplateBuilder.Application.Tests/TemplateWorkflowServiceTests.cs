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