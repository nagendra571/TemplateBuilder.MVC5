using System.IO.Compression;
using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Application.Tests;

public class TemplatePromotionBulkZipTests
{
    [Fact]
    public async Task Bulk_zip_contains_per_template_files_and_summary_manifest()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var promo = Substitute.For<ITemplatePromotionRepository>();
        var audit = Substitute.For<IAuditService>();
        var svc = new TemplatePromotionService(repo, promo, audit);

        repo.GetByIdAsync(1).Returns(new Template { Id = 1, ExternalKey = Guid.NewGuid(), Name = "Invoice v3", TemplateType = "Email", Status = TemplateStatus.Published });
        repo.GetVersionHistoryAsync(1).Returns(new List<TemplateVersion> { new TemplateVersion { VersionNumber = 1, Body = "<p>one</p>" } });
        repo.GetByIdAsync(2).Returns((Template?)null);

        var zip = await svc.BuildBulkZipAsync(new[] { 1, 2 });
        zip.Should().NotBeEmpty();

        using var ms = new MemoryStream(zip);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.Entries.Select(e => e.Name).Should().Contain("Invoice_v3.template.json");
        archive.Entries.Select(e => e.Name).Should().Contain("_summary.json");
        var summaryEntry = archive.GetEntry("_summary.json");
        using var sr = new StreamReader(summaryEntry!.Open());
        var summary = sr.ReadToEnd();
        summary.Should().Contain("\"Invoice v3\"");
        summary.Should().Contain("not found");
    }
}
