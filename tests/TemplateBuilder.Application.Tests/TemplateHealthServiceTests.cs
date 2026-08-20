using FluentAssertions;
using Newtonsoft.Json;
using NSubstitute;
using TemplateBuilder.Application.DTOs;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Application.Tests;

public class TemplateHealthServiceTests
{
    [Fact]
    public async Task Extract_handles_nested_loops_conditionals_and_ignores_literals()
    {
        var svc = new TemplateHealthService(Substitute.For<ITemplateRepository>(), Substitute.For<ISqlViewDiscoveryService>());
        const string body = @"
<p>{{ model.FirstName }} {{ model.User.Name }}</p>
{{ for item in model.Items }}{{ item.Qty }}{{ end }}
{{ if model.HasDiscount }}yes{{ end }}
{{ ""literal model.Nope"" }} {{ 'model.Single' }}";
        var paths = await svc.ExtractModelPathsAsync(body);
        paths.Should().BeEquivalentTo("FirstName", "User.Name", "Items", "HasDiscount");
    }

    [Fact]
    public async Task Check_reports_missing_view_and_missing_column()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var discovery = Substitute.For<ISqlViewDiscoveryService>();
        repo.GetByIdAsync(1).Returns(new Template
        {
            Id = 1, Name = "T", SourceView = "v_Gone", SourceViewSnapshot = JsonConvert.SerializeObject(new { takenAt = DateTime.UtcNow, columns = new List<SqlColumnInfo> { new SqlColumnInfo { Name = "FirstName", DataType = "nvarchar", MaxLength = 100, IsNullable = false } } }),
            CurrentVersion = new TemplateVersion { Body = "<p>{{ model.FirstName }}</p><p>{{ model.Nope }}</p>" }
        });
        discovery.GetViewColumnsAsync("v_Gone").Returns(new List<SqlColumnInfo>());

        var svc = new TemplateHealthService(repo, discovery);
        var report = await svc.CheckAsync(1);

        report.ViewMissing.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "view_missing" && f.Severity == HealthSeverity.Critical);
    }

    [Fact]
    public async Task Check_reports_type_and_length_drift_from_snapshot()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var discovery = Substitute.For<ISqlViewDiscoveryService>();
        repo.GetByIdAsync(1).Returns(new Template
        {
            Id = 1, Name = "T", SourceView = "v_Cust",
            SourceViewSnapshot = JsonConvert.SerializeObject(new { takenAt = DateTime.UtcNow, columns = new List<SqlColumnInfo> { new SqlColumnInfo { Name = "CustomerName", DataType = "nvarchar", MaxLength = 100, IsNullable = true } } }),
            CurrentVersion = new TemplateVersion { Body = "<p>{{ model.CustomerName }}</p>" }
        });
        discovery.GetViewColumnsAsync("v_Cust").Returns(new List<SqlColumnInfo> { new SqlColumnInfo { Name = "CustomerName", DataType = "nvarchar", MaxLength = 500, IsNullable = false } });

        var svc = new TemplateHealthService(repo, discovery);
        var report = await svc.CheckAsync(1);

        report.Findings.Should().Contain(f => f.Code == "column_length_changed" && f.Severity == HealthSeverity.Warning);
        report.Findings.Should().Contain(f => f.Code == "column_nullability_changed" && f.Severity == HealthSeverity.Warning);
    }

    [Fact]
    public async Task Check_reports_type_change_without_redundant_length_finding()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var discovery = Substitute.For<ISqlViewDiscoveryService>();
        repo.GetByIdAsync(1).Returns(new Template
        {
            Id = 1, Name = "T", SourceView = "v_C",
            SourceViewSnapshot = JsonConvert.SerializeObject(new { takenAt = DateTime.UtcNow, columns = new List<SqlColumnInfo> { new SqlColumnInfo { Name = "Amount", DataType = "nvarchar", MaxLength = 100, IsNullable = true } } }),
            CurrentVersion = new TemplateVersion { Body = "<p>{{ model.Amount }}</p>" }
        });
        discovery.GetViewColumnsAsync("v_C").Returns(new List<SqlColumnInfo> { new SqlColumnInfo { Name = "Amount", DataType = "int", MaxLength = null, IsNullable = false } });

        var report = await new TemplateHealthService(repo, discovery).CheckAsync(1);

        report.Findings.Should().Contain(f => f.Code == "column_type_changed");
        report.Findings.Should().NotContain(f => f.Code == "column_length_changed");
    }

    [Fact]
    public async Task Check_unbound_template_with_tokens_reports_warning()
    {
        var repo = Substitute.For<ITemplateRepository>();
        var discovery = Substitute.For<ISqlViewDiscoveryService>();
        repo.GetByIdAsync(1).Returns(new Template { Id = 1, Name = "T", SourceView = null, CurrentVersion = new TemplateVersion { Body = "<p>{{ model.FirstName }}</p>" } });
        var report = await new TemplateHealthService(repo, discovery).CheckAsync(1);
        report.Findings.Should().Contain(f => f.Code == "unbound_tokens" && f.Severity == HealthSeverity.Warning);
    }
}
