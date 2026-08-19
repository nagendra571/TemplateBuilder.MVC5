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