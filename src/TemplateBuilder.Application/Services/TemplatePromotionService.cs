using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Services;

public class TemplatePromotionService : ITemplatePromotionService
{
    private static readonly JsonSerializerSettings CamelJson = new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented
    };

    private readonly ITemplateRepository _repository;
    private readonly ITemplatePromotionRepository _promotion;
    private readonly IAuditService _audit;

    public TemplatePromotionService(ITemplateRepository repository, ITemplatePromotionRepository promotion, IAuditService audit)
    {
        _repository = repository;
        _promotion = promotion;
        _audit = audit;
    }

    public async Task<TemplateExportDocument?> BuildExportAsync(int templateId, CancellationToken ct = default)
    {
        var template = await _repository.GetByIdAsync(templateId, ct);
        if (template is null) return null;
        var history = await _repository.GetVersionHistoryAsync(templateId, ct);
        return new TemplateExportDocument
        {
            SchemaVersion = 1,
            Exporter = new ExporterInfo(),
            ExportedAt = DateTime.UtcNow,
            Template = new TemplateExportTemplate
            {
                ExternalKey = template.ExternalKey,
                Name = template.Name,
                TemplateType = template.TemplateType,
                Description = template.Description,
                SampleData = template.SampleData,
                IsActive = template.IsActive,
                Status = template.Status.ToString(),
                Versions = history.OrderBy(v => v.VersionNumber).Select(v => new TemplateExportVersion
                {
                    VersionNumber = v.VersionNumber,
                    Body = v.Body,
                    ChangeComment = v.ChangeComment,
                    CreatedAt = v.CreatedAt,
                    CreatedBy = v.CreatedBy
                }).ToList()
            }
        };
    }

    public string SerializeExport(TemplateExportDocument document)
        => JsonConvert.SerializeObject(document, CamelJson);

    public string SanitizeFileName(string name)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"[^\w\-\.]", "_").Trim();
        if (cleaned.Length > 80) cleaned = cleaned.Substring(0, 80);
        return string.IsNullOrEmpty(cleaned) ? "template" : cleaned;
    }

    public static string CollapseStatus(string exported)
        => exported == "Published" ? "Published" : "Draft";

    public async Task<TemplateImportResult> ImportAsync(byte[] fileBytes, string actor, CancellationToken ct = default)
    {
        var result = new TemplateImportResult();
        TemplateExportDocument? doc;
        try
        {
            doc = JsonConvert.DeserializeObject<TemplateExportDocument>(Encoding.UTF8.GetString(fileBytes), CamelJson);
        }
        catch (Exception)
        {
            result.Errors.Add(new TemplateImportEntry { Reason = "Not a valid template export file (JSON parse failed)." });
            return result;
        }
        if (doc is null || doc.SchemaVersion > 1)
        {
            result.Errors.Add(new TemplateImportEntry { Reason = $"schemaVersion {doc?.SchemaVersion} not supported." });
            return result;
        }
        if (doc.Template is null || string.IsNullOrWhiteSpace(doc.Template.Name) || string.IsNullOrWhiteSpace(doc.Template.TemplateType) || doc.Template.Versions.Count == 0)
        {
            result.Errors.Add(new TemplateImportEntry { Name = doc?.Template?.Name, Reason = "File is missing template name/type or has no versions." });
            return result;
        }
        for (var i = 0; i < doc.Template.Versions.Count; i++)
        {
            var parsed = Scriban.Template.Parse(doc.Template.Versions[i].Body ?? string.Empty);
            if (parsed.HasErrors)
            {
                result.Errors.Add(new TemplateImportEntry { Name = doc.Template.Name, Reason = $"Version {doc.Template.Versions[i].VersionNumber} does not parse." });
                return result;
            }
        }

        var key = doc.Template.ExternalKey;
        var existing = key != Guid.Empty ? await _promotion.GetByExternalKeyAsync(key, ct) : null;

        if (existing is null)
        {
            var template = new Template
            {
                ExternalKey = key == Guid.Empty ? Guid.NewGuid() : key,
                Name = doc.Template.Name.Trim(),
                TemplateType = doc.Template.TemplateType,
                Description = doc.Template.Description,
                SampleData = doc.Template.SampleData,
                IsActive = doc.Template.IsActive,
                Status = Enum.TryParse<TemplateStatus>(CollapseStatus(doc.Template.Status), out var st) ? st : TemplateStatus.Draft
            };
            var versions = doc.Template.Versions.Select(v => new TemplateVersion
            {
                VersionNumber = v.VersionNumber,
                Body = v.Body,
                ChangeComment = v.ChangeComment,
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy
            }).ToList();
            var created = await _promotion.AddWithVersionsAsync(template, versions, ct);
            await _audit.RecordAsync("Template", created.Id, AuditActions.Imported, actor,
                afterState: JsonConvert.SerializeObject(new { file = doc.Template.Name, externalKey = created.ExternalKey, versionsImported = versions.Count }), ct: ct);
            result.Created.Add(new TemplateImportEntry { Name = created.Name, ExternalKey = created.ExternalKey });
            return result;
        }

        if (existing.Status == TemplateStatus.Review || existing.Status == TemplateStatus.Approved)
        {
            result.Skipped.Add(new TemplateImportEntry { Name = existing.Name, Reason = $"Target is {existing.Status} (locked)" });
            return result;
        }

        existing.Name = doc.Template.Name.Trim();
        existing.TemplateType = doc.Template.TemplateType;
        existing.Description = doc.Template.Description;
        existing.SampleData = doc.Template.SampleData;
        existing.IsActive = doc.Template.IsActive;
        existing.Status = Enum.TryParse<TemplateStatus>(CollapseStatus(doc.Template.Status), out var st2) ? st2 : TemplateStatus.Draft;

        var importedVersions = doc.Template.Versions.Select(v => new TemplateVersion
        {
            Body = v.Body,
            ChangeComment = v.ChangeComment is null ? $"Imported from {doc.Exporter.Name} ({doc.ExportedAt:u})" : $"{v.ChangeComment} — imported {doc.ExportedAt:u}",
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy
        }).ToList();

        var assigned = await _promotion.UpdateFromImportAsync(existing, importedVersions, ct);
        await _audit.RecordAsync("Template", existing.Id, AuditActions.Imported, actor,
            afterState: JsonConvert.SerializeObject(new { file = doc.Template.Name, externalKey = existing.ExternalKey, versionsImported = assigned.Length }), ct: ct);
        result.Updated.Add(new TemplateImportEntry { Name = existing.Name, ExternalKey = existing.ExternalKey, VersionsAppended = assigned.Length });
        return result;
    }

    public async Task<byte[]> BuildBulkZipAsync(IReadOnlyList<int> templateIds, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var summary = new List<object>();
            foreach (var id in templateIds)
            {
                var doc = await BuildExportAsync(id, ct);
                if (doc is null)
                {
                    summary.Add(new { id, name = (string?)null, status = "not found" });
                    continue;
                }
                var entry = archive.CreateEntry($"{SanitizeFileName(doc.Template.Name)}.template.json");
                using (var writer = new StreamWriter(entry.Open()))
                    await writer.WriteAsync(SerializeExport(doc));
                summary.Add(new { id, name = doc.Template.Name, status = "exported" });
            }
            var manifest = archive.CreateEntry("_summary.json");
            using (var writer = new StreamWriter(manifest.Open()))
                await writer.WriteAsync(JsonConvert.SerializeObject(new { schemaVersion = 1, exportedAt = DateTime.UtcNow, files = summary }, CamelJson));
        }
        return ms.ToArray();
    }
}
