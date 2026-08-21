using System;
using System.Data.Entity.Infrastructure;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
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
    private readonly ISampleDataGenerator _sampleDataGenerator;
    private readonly IAuditService _audit;
    private readonly IAuditRepository _auditRepository;
    private readonly ITemplatePromotionService _promotion;
    private readonly ITemplateHealthService _health;

    public TemplatesController(ITemplateRepository repository, ISqlViewDiscoveryService viewDiscovery, ITemplateEngine engine, IHtmlSanitizerService sanitizer, ISampleDataGenerator sampleDataGenerator, IAuditService audit, IAuditRepository auditRepository, ITemplatePromotionService promotion, ITemplateHealthService health)
    {
        _repository = repository;
        _viewDiscovery = viewDiscovery;
        _engine = engine;
        _sanitizer = sanitizer;
        _sampleDataGenerator = sampleDataGenerator;
        _audit = audit;
        _auditRepository = auditRepository;
        _promotion = promotion;
        _health = health;
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

    [HttpPost, ValidateJsonAntiForgeryToken, ActionName("Create")]
    public async Task<ActionResult> CreateTemplateJson()
    {
        var model = await Request.ReadJsonBodyAsync<TemplateEditorViewModel>();
        if (string.IsNullOrWhiteSpace(model.Name))
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", "Template name is required."));
        int createdId;
        try
        {
            var template = await _repository.CreateAsync(new Template
            {
                Name = model.Name.Trim(),
                TemplateType = model.TemplateType,
                Description = model.Description
            });
            createdId = template.Id;
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", $"A template named '{model.Name.Trim()}' already exists."));
        }
        await _audit.RecordAsync("Template", createdId, AuditActions.Created, CurrentActor, afterState: JsonConvert.SerializeObject(new { name = model.Name.Trim() }));
        return JsonOk(new { templateId = createdId });
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
            LatestVersionIsActive = template.CurrentVersion?.IsActive ?? true,
            AvailableViews = views.ToList(),
            SampleData = template.SampleData,
            SourceView = template.SourceView
        });
    }

    [Route("Templates/{id:int}/SaveVersion")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> SaveVersion(int id)
    {
        var request = await Request.ReadJsonBodyAsync<SaveVersionRequest>();
        if (string.IsNullOrWhiteSpace(request.Name))
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", "Template name is required."));
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Template {id} not found."));
        TemplateVersion published;
        try
        {
            template.Name = request.Name.Trim();
            template.TemplateType = request.TemplateType;
            template.Description = request.Description;
            var previousSourceView = template.SourceView;
            template.SourceView = string.IsNullOrWhiteSpace(request.SourceView) ? null : request.SourceView.Trim();
            if (!string.Equals(previousSourceView, template.SourceView, StringComparison.OrdinalIgnoreCase))
                template.SourceViewSnapshot = template.SourceView is null ? null : await _health.BuildSnapshotJsonAsync(template.SourceView);
            await _repository.UpdateTemplateAsync(template);
            var nextNumber = await _repository.GetNextVersionNumberAsync(id);
            published = await _repository.PublishVersionAsync(id, new TemplateVersion
            {
                TemplateId = id,
                VersionNumber = nextNumber,
                Body = request.Body,
                ChangeComment = request.ChangeComment,
                IsActive = request.IsActive,
                CreatedBy = CurrentActor
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return JsonError(409, new ErrorResult("CONFLICT", "This template was modified by another user while you were editing. Please refresh and try again."));
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", $"A template named '{request.Name.Trim()}' already exists."));
        }
        await _audit.RecordAsync("Template", id, request.IsActive ? AuditActions.Published : AuditActions.DraftSaved, CurrentActor,
            afterState: JsonConvert.SerializeObject(new { versionNumber = published.VersionNumber, versionId = published.Id, isActive = published.IsActive }));
        return JsonOk(new { versionId = published.Id, versionNumber = published.VersionNumber, isActive = published.IsActive });
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
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> RestoreVersion(int id, int versionId, int sourceVersionNumber)
    {
        TemplateVersion published;
        try
        {
            var oldBody = await _repository.GetVersionBodyAsync(versionId);
            if (oldBody is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Version {versionId} not found."));
            var source = (await _repository.GetVersionHistoryAsync(id)).FirstOrDefault(v => v.Id == versionId);
            var nextNumber = await _repository.GetNextVersionNumberAsync(id);
            published = await _repository.PublishVersionAsync(id, new TemplateVersion
            {
                TemplateId = id,
                VersionNumber = nextNumber,
                Body = oldBody,
                ChangeComment = $"Restored from v{sourceVersionNumber}",
                IsActive = source?.IsActive ?? true,
                CreatedBy = CurrentActor
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return JsonError(409, new ErrorResult("CONFLICT", "This template was modified by another user while you were editing. Please refresh and try again."));
        }
        await _audit.RecordAsync("Template", id, AuditActions.Restored, CurrentActor, comment: $"Restored from v{sourceVersionNumber}");
        return JsonOk(new { versionId = published.Id, versionNumber = published.VersionNumber });
    }

    [Route("Templates/Api/Views/{viewName}/Columns")]
    [HttpGet]
    public async Task<ActionResult> GetViewColumns(string viewName)
    {
        var columns = await _viewDiscovery.GetViewColumnsAsync(viewName);
        return JsonOk(columns);
    }

    [Route("Templates/{id:int}/Preview")]
    [HttpPost, ValidateJsonAntiForgeryToken]
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

    [Route("Templates/Api/SampleData/Generate")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> GenerateSampleData()
    {
        var request = await Request.ReadJsonBodyAsync<GenerateSampleDataRequest>();
        var data = await _sampleDataGenerator.GenerateAsync(request?.ViewName, request?.TemplateBody);
        return Content(JsonConvert.SerializeObject(new { sampleData = data }), "application/json");
    }

    [Route("Templates/{id:int}/SampleData")]
    [HttpPut, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> SaveSampleData(int id)
    {
        var request = await Request.ReadJsonBodyAsync<SaveSampleDataRequest>();
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Template {id} not found."));
        template.SampleData = string.IsNullOrWhiteSpace(request?.SampleData) ? null : request.SampleData;
        await _repository.UpdateTemplateAsync(template);
        return JsonOk(new { saved = true });
    }

    [Route("Templates/{id:int}/ToggleActive")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> ToggleActive(int id)
    {
        var template = await _repository.GetByIdAsync(id);
        if (template is null) return JsonError(404, new ErrorResult("TEMPLATE_NOT_FOUND", $"Template {id} not found."));
        template.IsActive = !template.IsActive;
        await _repository.UpdateTemplateAsync(template);
        await _audit.RecordAsync("Template", id, AuditActions.ToggledActive, CurrentActor, afterState: JsonConvert.SerializeObject(new { isActive = template.IsActive }));
        return JsonOk(new { isActive = template.IsActive });
    }

    [Route("Templates/{id:int}/Validate")]
    [HttpPost, ValidateJsonAntiForgeryToken]
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
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Duplicate(int id)
    {
        var request = await Request.ReadJsonBodyAsync<DuplicateRequest>();
        var source = await _repository.GetByIdAsync(id);
        if (source is null) return HttpNotFound();

        var body = source.CurrentVersion?.Body ?? string.Empty;
        var isActive = source.CurrentVersion?.IsActive ?? true;
        int newId;
        try
        {
            var newTemplate = await _repository.CreateAsync(new Template
            {
                Name = request.NewName.Trim(),
                TemplateType = source.TemplateType,
                Description = source.Description
            });
            newId = newTemplate.Id;

            await _repository.PublishVersionAsync(newTemplate.Id, new TemplateVersion
            {
                TemplateId = newTemplate.Id,
                VersionNumber = 1,
                Body = body,
                ChangeComment = $"Duplicated from '{source.Name}'",
                IsActive = isActive,
                CreatedBy = CurrentActor
            });
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new ErrorResult("VALIDATION_ERROR", $"A template named '{request.NewName.Trim()}' already exists."));
        }
        await _audit.RecordAsync("Template", newId, AuditActions.Duplicated, CurrentActor, comment: $"Duplicated from template {source.Id}");
        return JsonOk(new { id = newId });
    }

    [Route("Templates/Export/{id:int}")]
    [HttpGet]
    public async Task<ActionResult> ExportTemplate(int id)
    {
        var doc = await _promotion.BuildExportAsync(id);
        if (doc is null) return HttpNotFound();
        var bytes = Encoding.UTF8.GetBytes(_promotion.SerializeExport(doc));
        Response.AddHeader("Content-Disposition", $"attachment; filename={_promotion.SanitizeFileName(doc.Template.Name)}.template.json");
        return File(bytes, "application/json");
    }

    [Route("Templates/Import")]
    [HttpPost]
    public async Task<ActionResult> Import(HttpPostedFileBase file)
    {
        if (file is null || file.ContentLength == 0)
            return Content(JsonConvert.SerializeObject(new { errors = new[] { new { reason = "No file selected." } } }), "application/json");
        using var ms = new MemoryStream();
        await file.InputStream.CopyToAsync(ms);
        var result = await _promotion.ImportAsync(ms.ToArray(), CurrentActor);
        return Content(JsonConvert.SerializeObject(result, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        }), "application/json");
    }

    [Route("Templates/BulkActivate")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> BulkActivate(BulkIdsRequest request)
        => await BulkToggle(request.Ids, active: true);

    [Route("Templates/BulkDeactivate")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> BulkDeactivate(BulkIdsRequest request)
        => await BulkToggle(request.Ids, active: false);

    private async Task<ActionResult> BulkToggle(IReadOnlyList<int> ids, bool active)
    {
        var succeeded = new List<int>();
        var failed = new List<object>();
        foreach (var id in ids)
        {
            try
            {
                var t = await _repository.GetByIdAsync(id);
                if (t is null) { failed.Add(new { id, reason = "NOT_FOUND" }); continue; }
                if (t.IsActive == active) { succeeded.Add(id); continue; }
                t.IsActive = active;
                await _repository.UpdateTemplateAsync(t);
                await _audit.RecordAsync("Template", id, AuditActions.ToggledActive, CurrentActor, afterState: JsonConvert.SerializeObject(new { isActive = active }));
                succeeded.Add(id);
            }
            catch (Exception) { failed.Add(new { id, reason = "ERROR" }); }
        }
        return Content(JsonConvert.SerializeObject(new { succeeded, failed }), "application/json");
    }

    [Route("Templates/BulkExport")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> BulkExport(BulkIdsRequest request)
    {
        var zip = await _promotion.BuildBulkZipAsync(request.Ids);
        Response.AddHeader("Content-Disposition", "attachment; filename=template-builder-export.zip");
        return File(zip, "application/zip");
    }

    [Route("Templates/BulkDelete")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> BulkDelete(BulkIdsRequest request)
    {
        var succeeded = new List<int>();
        var failed = new List<object>();
        foreach (var id in request.Ids)
        {
            try
            {
                var t = await _repository.GetByIdAsync(id);
                if (t is null) { failed.Add(new { id, reason = "NOT_FOUND" }); continue; }
                await _audit.RecordAsync("Template", id, AuditActions.Deleted, CurrentActor, beforeState: JsonConvert.SerializeObject(new { name = t.Name }));
                if (await _repository.DeleteAsync(id)) succeeded.Add(id); else failed.Add(new { id, reason = "NOT_FOUND" });
            }
            catch (Exception) { failed.Add(new { id, reason = "ERROR" }); }
        }
        return Content(JsonConvert.SerializeObject(new { succeeded, failed }), "application/json");
    }

    [Route("Templates/{id:int}/Health")]
    [HttpGet]
    public async Task<ActionResult> GetHealth(int id)
    {
        var report = await _health.CheckAsync(id);
        return Content(JsonConvert.SerializeObject(report, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        }), "application/json");
    }

    [Route("Templates/{id:int}/Audit")]
    [HttpGet]
    public async Task<ActionResult> GetAuditTimeline(int id)
    {
        var rows = await _auditRepository.QueryAsync(new AuditQuery { EntityType = "Template", EntityId = id, PageSize = 100 });
        return JsonOk(rows.Select(a => new { id = a.Id, action = a.Action, actor = a.Actor, occurredAt = a.OccurredAt.ToString("o"), comment = a.Comment }));
    }
}

public class BulkIdsRequest
{
    public List<int> Ids { get; set; } = new List<int>();
}