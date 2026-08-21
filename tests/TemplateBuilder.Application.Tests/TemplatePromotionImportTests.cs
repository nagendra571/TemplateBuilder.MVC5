using System.Text;
using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Application.Tests;

public class TemplatePromotionImportTests
{
    [Fact]
    public async Task Import_rejects_legacy_schema_v1_file()
    {
        var (svc, _, _, _) = Create();
        var json = "{ \"schemaVersion\": 1, \"template\": { \"name\": \"X\" } }";
        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(json), "bob");
        result.Errors.Should().ContainSingle(e => e.Reason.Contains("schemaVersion"));
        result.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_creates_template_preserving_version_and_template_flags()
    {
        var (svc, _, promo, audit) = Create();
        var key = Guid.NewGuid();
        promo.GetByExternalKeyAsync(key).Returns((Template?)null);
        Template captured = null!;
        promo.AddWithVersionsAsync(Arg.Do<Template>(t => captured = t), Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Template>());
        var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", IsActive = false, Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>ok</p>", IsActive = true }, new TemplateExportVersion { VersionNumber = 2, Body = "<p>draft</p>", IsActive = false } } } };

        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");

        result.Created.Should().ContainSingle(c => c.Name == "X");
        captured.IsActive.Should().BeFalse();
        captured.ExternalKey.Should().Be(key);
        await promo.Received(1).AddWithVersionsAsync(captured, Arg.Is<IReadOnlyList<TemplateVersion>>(vs => vs.Select(v => v.IsActive).SequenceEqual(new[] { true, false })), Arg.Any<CancellationToken>());
        await audit.Received(1).RecordAsync("Template", Arg.Any<int>(), AuditActions.Imported, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_updates_existing_target_preserving_version_flags()
    {
        var (svc, _, promo, audit) = Create();
        var key = Guid.NewGuid();
        var existing = new Template { Id = 9, Name = "Old", TemplateType = "Email", IsActive = true };
        promo.GetByExternalKeyAsync(key).Returns(existing);
        promo.UpdateFromImportAsync(existing, Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { 2, 3 });
        var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", IsActive = true, Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>a</p>", IsActive = false }, new TemplateExportVersion { VersionNumber = 2, Body = "<p>b</p>", IsActive = true } } } };

        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");

        result.Updated.Should().ContainSingle(u => u.Name == "X" && u.VersionsAppended == 2);
        result.Created.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
        existing.Name.Should().Be("X");
        await promo.Received(1).UpdateFromImportAsync(existing, Arg.Is<IReadOnlyList<TemplateVersion>>(vs => vs.Select(v => v.IsActive).SequenceEqual(new[] { false, true })), Arg.Any<CancellationToken>());
        await audit.Received(1).RecordAsync("Template", 9, AuditActions.Imported, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_rejects_scriban_invalid_body()
    {
        var (svc, _, _, _) = Create();
        var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = Guid.NewGuid(), Name = "X", TemplateType = "Email", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "{{ end }}" } } } };
        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
        result.Errors.Should().ContainSingle(e => e.Reason.Contains("Version 1"));
    }

    private static (TemplatePromotionService svc, ITemplateRepository repo, ITemplatePromotionRepository promo, IAuditService audit) Create()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var promo = Substitute.For<ITemplatePromotionRepository>();
        var audit = Substitute.For<IAuditService>();
        return (new TemplatePromotionService(repo, promo, audit), repo, promo, audit);
    }
}
