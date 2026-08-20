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
    private static (TemplatePromotionService svc, ITemplateRepository repo, ITemplatePromotionRepository promo, IAuditService audit) Create()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var promo = Substitute.For<ITemplatePromotionRepository>();
        var audit = Substitute.For<IAuditService>();
        return (new TemplatePromotionService(repo, promo, audit), repo, promo, audit);
    }

    [Fact]
    public async Task Import_rejects_unknown_schema_version()
    {
        var (svc, _, _, _) = Create();
        var json = "{ \"schemaVersion\": 99, \"template\": { \"name\": \"X\" } }";
        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(json), "bob");
        result.Errors.Should().ContainSingle(e => e.Reason.Contains("schemaVersion"));
        result.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_rejects_scriban_invalid_body()
    {
        var (svc, _, _, _) = Create();
        var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = Guid.NewGuid(), Name = "X", TemplateType = "Email", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "{{ end }}" } } } };
        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
        result.Errors.Should().ContainSingle(e => e.Reason.Contains("Version 1"));
    }

    [Theory]
    [InlineData("Draft", "Draft")]
    [InlineData("Published", "Published")]
    [InlineData("Review", "Draft")]
    [InlineData("Approved", "Draft")]
    public void CollapseStatus_maps_correctly(string exported, string expected)
    {
        TemplatePromotionService.CollapseStatus(exported).Should().Be(expected);
    }

    [Fact]
    public async Task Import_skips_locked_target()
    {
        var (svc, _, promo, _) = Create();
        var key = Guid.NewGuid();
        promo.GetByExternalKeyAsync(key).Returns(new Template { Id = 3, Name = "Old", Status = TemplateStatus.Review });
        var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", Status = "Published", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>ok</p>" } } } };
        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
        result.Skipped.Should().ContainSingle(s => s.Reason.Contains("Review"));
        await promo.DidNotReceive().UpdateFromImportAsync(Arg.Any<Template>(), Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_creates_new_template_and_audits()
    {
        var (svc, _, promo, audit) = Create();
        var key = Guid.NewGuid();
        promo.GetByExternalKeyAsync(key).Returns((Template?)null);
        Template captured = null!;
        promo.AddWithVersionsAsync(Arg.Do<Template>(t => captured = t), Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Template>());
        var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", Status = "Published", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>ok</p>" } } } };
        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
        result.Created.Should().ContainSingle(c => c.Name == "X");
        captured.Status.Should().Be(TemplateStatus.Published);
        captured.ExternalKey.Should().Be(key);
        await audit.Received(1).RecordAsync("Template", Arg.Any<int>(), AuditActions.Imported, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_updates_existing_target_and_audits()
    {
        var (svc, _, promo, audit) = Create();
        var key = Guid.NewGuid();
        var existing = new Template { Id = 9, Name = "Old", TemplateType = "Email", Status = TemplateStatus.Draft };
        promo.GetByExternalKeyAsync(key).Returns(existing);
        promo.UpdateFromImportAsync(existing, Arg.Any<IReadOnlyList<TemplateVersion>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { 2, 3 });
        var doc = new TemplateExportDocument { Template = new TemplateExportTemplate { ExternalKey = key, Name = "X", TemplateType = "Email", Status = "Draft", Versions = { new TemplateExportVersion { VersionNumber = 1, Body = "<p>a</p>" }, new TemplateExportVersion { VersionNumber = 2, Body = "<p>b</p>" } } } };
        var result = await svc.ImportAsync(Encoding.UTF8.GetBytes(svc.SerializeExport(doc)), "bob");
        result.Updated.Should().ContainSingle(u => u.Name == "X" && u.VersionsAppended == 2);
        result.Created.Should().BeEmpty();
        existing.Name.Should().Be("X");
        await audit.Received(1).RecordAsync("Template", 9, AuditActions.Imported, "bob",
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
