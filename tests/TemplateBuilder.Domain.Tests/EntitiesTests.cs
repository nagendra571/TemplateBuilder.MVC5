using FluentAssertions;
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Domain.Tests;

public class TemplateTests
{
    [Fact]
    public void Template_defaults_are_sane()
    {
        var t = new Template();

        t.Id.Should().Be(0);
        t.Name.Should().BeEmpty();
        t.TemplateType.Should().BeEmpty();
        t.Description.Should().BeNull();
        t.CurrentVersionId.Should().BeNull();
        t.IsActive.Should().BeTrue();
        t.CreatedAt.Should().Be(default);
        t.UpdatedAt.Should().Be(default);
        t.RowVersion.Should().BeEmpty();
        t.Versions.Should().BeEmpty();
        t.CurrentVersion.Should().BeNull();
    }

    [Fact]
    public void Template_round_trips_all_properties()
    {
        var version = new TemplateVersion { Id = 7, VersionNumber = 3 };
        var t = new Template
        {
            Id = 1,
            Name = "Welcome Email",
            TemplateType = "Email",
            Description = "Onboarding",
            CurrentVersionId = 7,
            IsActive = false,
            CurrentVersion = version
        };
        t.Versions.Add(version);

        t.Id.Should().Be(1);
        t.Name.Should().Be("Welcome Email");
        t.TemplateType.Should().Be("Email");
        t.Description.Should().Be("Onboarding");
        t.CurrentVersionId.Should().Be(7);
        t.IsActive.Should().BeFalse();
        t.CurrentVersion.Should().BeSameAs(version);
        t.Versions.Should().ContainSingle().Which.Should().BeSameAs(version);
    }
}

public class TemplateVersionTests
{
    [Fact]
    public void TemplateVersion_defaults_are_sane()
    {
        var v = new TemplateVersion();

        v.Id.Should().Be(0);
        v.TemplateId.Should().Be(0);
        v.VersionNumber.Should().Be(0);
        v.Body.Should().BeEmpty();
        v.ChangeComment.Should().BeNull();
        v.CreatedAt.Should().Be(default);
        v.CreatedBy.Should().BeNull();
        v.Template.Should().BeNull();
    }

    [Fact]
    public void TemplateVersion_round_trips_all_properties()
    {
        var template = new Template { Id = 5 };
        var v = new TemplateVersion
        {
            Id = 9,
            TemplateId = 5,
            VersionNumber = 2,
            Body = "<p>Hi {{name}}</p>",
            ChangeComment = "Updated greeting",
            CreatedBy = "alice@example.com",
            CreatedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            Template = template
        };

        v.TemplateId.Should().Be(5);
        v.VersionNumber.Should().Be(2);
        v.Body.Should().Be("<p>Hi {{name}}</p>");
        v.ChangeComment.Should().Be("Updated greeting");
        v.CreatedBy.Should().Be("alice@example.com");
        v.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        v.Template.Should().BeSameAs(template);
    }
}

public class SnippetTests
{
    [Fact]
    public void Snippet_defaults_are_sane()
    {
        var s = new Snippet();

        s.Id.Should().Be(0);
        s.Name.Should().BeEmpty();
        s.Description.Should().BeNull();
        s.Body.Should().BeEmpty();
        s.CreatedAt.Should().Be(default);
        s.UpdatedAt.Should().Be(default);
    }

    [Fact]
    public void Snippet_round_trips_all_properties()
    {
        var s = new Snippet
        {
            Id = 3,
            Name = "Footer",
            Description = "Standard footer",
            Body = "<footer>Thanks</footer>",
            CreatedAt = new DateTime(2026, 8, 2),
            UpdatedAt = new DateTime(2026, 8, 3)
        };

        s.Name.Should().Be("Footer");
        s.Description.Should().Be("Standard footer");
        s.Body.Should().Be("<footer>Thanks</footer>");
        s.CreatedAt.Should().Be(new DateTime(2026, 8, 2));
        s.UpdatedAt.Should().Be(new DateTime(2026, 8, 3));
    }
}