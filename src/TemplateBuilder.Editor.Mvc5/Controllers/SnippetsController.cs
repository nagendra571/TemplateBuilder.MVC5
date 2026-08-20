using System;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Models;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class SnippetsController : TemplateBuilderControllerBase
{
    private readonly ISnippetRepository _snippets;
    private readonly IAuditService _audit;

    public SnippetsController(ISnippetRepository snippets, IAuditService audit)
    {
        _snippets = snippets;
        _audit = audit;
    }

    [Route("Templates/Api/Snippets")]
    [HttpGet]
    public async Task<ActionResult> GetAll()
    {
        var snippets = await _snippets.GetAllAsync();
        var stats = (await _snippets.GetUsageStatsAsync()).ToDictionary(x => x.SnippetId, x => x);
        return JsonOk(snippets.Select(s =>
        {
            stats.TryGetValue(s.Id, out var st);
            return new
            {
                id = s.Id,
                name = s.Name,
                description = s.Description,
                body = s.Body,
                usageCount = st?.UsageCount ?? 0,
                templateCount = st?.TemplateCount ?? 0,
                lastUsedAt = st?.LastUsedAt
            };
        }));
    }

    [Route("Templates/Api/Snippets")]
    [HttpPost, ValidateJsonAntiForgeryToken]
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
            await _audit.RecordAsync("Snippet", created.Id, AuditActions.SnippetCreated, CurrentActor);
            return JsonOk(new { id = created.Id, created.Name });
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new Models.ErrorResult("DUPLICATE_NAME", $"A snippet named '{request.Name.Trim()}' already exists."));
        }
    }

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
        return JsonOk(versions.Select(v => new { id = v.Id, versionNumber = v.VersionNumber, body = v.Body, changeComment = v.ChangeComment, createdAt = v.CreatedAt.ToString("o"), createdBy = v.CreatedBy }));
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

    [Route("Templates/Api/Snippets/{id:int}")]
    [HttpDelete, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Delete(int id)
    {
        var snippet = await _snippets.GetByIdAsync(id);
        if (snippet is null) return JsonError(404, new Models.ErrorResult("NOT_FOUND", "Snippet not found."));
        await _snippets.DeleteAsync(id);
        await _audit.RecordAsync("Snippet", id, AuditActions.SnippetDeleted, CurrentActor);
        return NoContentResult();
    }
}

public class UpdateSnippetRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Body { get; set; }
    public string? ChangeComment { get; set; }
}