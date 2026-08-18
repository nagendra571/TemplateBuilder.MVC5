using FluentAssertions;
using Moq;
using TemplateBuilder.Application.DTOs;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Interfaces;

namespace TemplateBuilder.Application.Tests;

public class SampleDataGeneratorTests
{
    private static Mock<ISqlViewDiscoveryService> ViewWith(params SqlColumnInfo[] columns)
    {
        var mock = new Mock<ISqlViewDiscoveryService>();
        mock.Setup(v => v.GetViewColumnsAsync("v_Test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(columns.ToList());
        return mock;
    }

    private static SampleDataGenerator Create(Mock<ISqlViewDiscoveryService>? views = null)
        => new(views?.Object ?? new Mock<ISqlViewDiscoveryService>().Object);

    [Fact]
    public async Task GenerateAsync_from_view_maps_column_types()
    {
        var gen = Create(ViewWith(
            new SqlColumnInfo { Name = "RecipientName", DataType = "nvarchar", MaxLength = 200 },
            new SqlColumnInfo { Name = "Qty", DataType = "int" },
            new SqlColumnInfo { Name = "Amount", DataType = "decimal" },
            new SqlColumnInfo { Name = "DueDate", DataType = "datetime" },
            new SqlColumnInfo { Name = "IsActive", DataType = "bit" },
            new SqlColumnInfo { Name = "EmailAddress", DataType = "nvarchar", MaxLength = 100 },
            new SqlColumnInfo { Name = "Id", DataType = "uniqueidentifier" }));

        var result = await gen.GenerateAsync("v_Test", null);

        result["RecipientName"].Should().Be("Jane Doe");
        result["Qty"].Should().Be(4);
        result["Amount"].Should().Be(1250.00m);
        result["DueDate"].Should().Be(DateTime.Today);
        result["IsActive"].Should().Be(true);
        result["EmailAddress"].Should().Be("jane.doe@agency.gov");
        result["Id"].Should().Be(Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301"));
    }

    [Fact]
    public async Task GenerateAsync_respects_max_length()
    {
        var gen = Create(ViewWith(new SqlColumnInfo { Name = "FirstName", DataType = "nvarchar", MaxLength = 6 }));

        var result = await gen.GenerateAsync("v_Test", null);

        result["FirstName"].Should().Be("Jane D");
    }

    [Fact]
    public async Task GenerateAsync_from_tokens_without_view()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "Dear {{model.RecipientName}}, total {{model.Amount}}");

        result["RecipientName"].Should().Be("Jane Doe");
        result["Amount"].Should().Be(1250.00m);
    }

    [Fact]
    public async Task GenerateAsync_detects_loops_with_item_fields()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "{{ for item in model.Items }}{{ item.Name }}: {{ item.Price }}{{ end }}");

        var items = result["Items"].Should().BeAssignableTo<IReadOnlyList<Dictionary<string, object?>>>().Subject;
        items.Should().HaveCount(3);
        items.Should().OnlyContain(i => i.ContainsKey("Name") && i.ContainsKey("Price"));
        items[0]["Name"].Should().Be("Jane Doe");
    }

    [Fact]
    public async Task GenerateAsync_bare_loop_falls_back_to_label_rows()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "{{ for i in model.Rows }}x{{ end }}");

        var rows = result["Rows"].Should().BeAssignableTo<IReadOnlyList<Dictionary<string, object?>>>().Subject;
        rows.Should().HaveCount(3);
        rows[0]["label"].Should().Be("Row 1");
        rows[2]["label"].Should().Be("Row 3");
    }

    [Fact]
    public async Task GenerateAsync_view_wins_over_tokens_for_same_key()
    {
        var gen = Create(ViewWith(new SqlColumnInfo { Name = "Qty", DataType = "int" }));
        var result = await gen.GenerateAsync("v_Test", "{{ model.Qty }} and {{ model.Notes }}");

        result["Qty"].Should().Be(4);
        result["Notes"].Should().Be("Sample Notes");
    }

    [Fact]
    public async Task GenerateAsync_loop_array_overrides_same_key_scalar()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, "{{ model.Items }}{{ for item in model.Items }}{{ item.Name }}{{ end }}");

        result["Items"].Should().BeAssignableTo<IReadOnlyList<Dictionary<string, object?>>>();
    }

    [Fact]
    public async Task GenerateAsync_empty_inputs_returns_empty_dictionary()
    {
        var gen = Create();
        var result = await gen.GenerateAsync(null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_invalid_template_body_does_not_throw()
    {
        var gen = Create(ViewWith(new SqlColumnInfo { Name = "Qty", DataType = "int" }));
        var result = await gen.GenerateAsync("v_Test", "{{ 1 + }} broken");

        result["Qty"].Should().Be(4);
    }
}