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