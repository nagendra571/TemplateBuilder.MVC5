using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Application.Tests;

public class TemplatePromotionServiceTests
{
    [Fact]
    public async Task BuildExport_shapes_document_with_ordered_versions()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var promo = Substitute.For<ITemplatePromotionRepository>();
        var audit = Substitute.For<IAuditService>();
        var svc = new TemplatePromotionService(repo, promo, audit);
        repo.GetByIdAsync(7).Returns(new Template { Id = 7, ExternalKey = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Invoice", TemplateType = "Email", Status = TemplateStatus.Published, IsActive = true });
        repo.GetVersionHistoryAsync(7).Returns(new List<TemplateVersion>
        {
            new TemplateVersion { VersionNumber = 2, Body = "<p>two</p>", ChangeComment = "c2" },
            new TemplateVersion { VersionNumber = 1, Body = "<p>one</p>" }
        });

        var doc = await svc.BuildExportAsync(7);

        doc.Should().NotBeNull();
        doc!.SchemaVersion.Should().Be(1);
        doc.Exporter.Name.Should().NotBeEmpty();
        doc.Template.Name.Should().Be("Invoice");
        doc.Template.Status.Should().Be("Published");
        doc.Template.Versions.Select(v => v.VersionNumber).Should().Equal(1, 2);
    }

    [Theory]
    [InlineData("Invoice v3", "Invoice_v3")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    public void SanitizeFileName_strips_invalid_chars(string input, string expected)
    {
        var svc = new TemplatePromotionService(Substitute.For<ITemplateRepository>(), Substitute.For<ITemplatePromotionRepository>(), Substitute.For<IAuditService>());
        svc.SanitizeFileName(input).Should().Be(expected);
    }

    [Fact]
    public void SerializeExport_uses_camel_case_json()
    {
        var svc = new TemplatePromotionService(Substitute.For<ITemplateRepository>(), Substitute.For<ITemplatePromotionRepository>(), Substitute.For<IAuditService>());
        var json = svc.SerializeExport(new TemplateExportDocument { Template = new TemplateExportTemplate { Name = "X", TemplateType = "Email" } });
        json.Should().Contain("\"schemaVersion\"");
        json.Should().Contain("\"externalKey\"");
    }
}
