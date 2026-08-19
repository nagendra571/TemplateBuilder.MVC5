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