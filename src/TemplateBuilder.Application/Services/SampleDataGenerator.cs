using TemplateBuilder.Application.DTOs;
using TemplateBuilder.Domain.Interfaces;
using Scriban;
using Scriban.Syntax;

namespace TemplateBuilder.Application.Services;

public interface ISampleDataGenerator
{
    Task<Dictionary<string, object?>> GenerateAsync(string? viewName, string? templateBody, CancellationToken ct = default);
}

public class SampleDataGenerator : ISampleDataGenerator
{
    private const int MaxColumns = 50;
    private const int MaxScalarKeys = 50;
    private const int LoopItems = 3;

    private readonly ISqlViewDiscoveryService _viewDiscovery;

    public SampleDataGenerator(ISqlViewDiscoveryService viewDiscovery)
        => _viewDiscovery = viewDiscovery;

    public async Task<Dictionary<string, object?>> GenerateAsync(string? viewName, string? templateBody, CancellationToken ct = default)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(viewName))
        {
            var columns = await _viewDiscovery.GetViewColumnsAsync(viewName!, ct);
            foreach (var column in columns.Take(MaxColumns))
                result[column.Name] = ValueForColumn(column);
        }

        if (!string.IsNullOrWhiteSpace(templateBody))
            ApplyTemplateTokens(result, templateBody!);

        return result;
    }

    private static void ApplyTemplateTokens(Dictionary<string, object?> result, string body)
    {
        var parsed = Template.Parse(body);
        if (parsed.HasErrors) return;

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var loopFields = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var scalars = new List<(string Key, string Kind)>();

        void Walk(ScriptNode node)
        {
            switch (node)
            {
                case ScriptForStatement forStmt:
                    var alias = (forStmt.Variable as ScriptVariable)?.Name;
                    var collection = MemberKey(forStmt.Iterator);
                    if (alias is not null && collection is not null)
                    {
                        aliases[alias] = collection;
                        if (!loopFields.TryGetValue(collection, out _))
                            loopFields[collection] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                    break;
                case ScriptMemberExpression member:
                    if (member.Target is ScriptVariable target && target.Name == "model" && member.Member.Name.Length > 0)
                        scalars.Add((member.Member.Name, InferKind(member.Member.Name)));
                    else if (member.Target is ScriptVariable local
                             && aliases.TryGetValue(local.Name, out var coll)
                             && member.Member.Name.Length > 0)
                    {
                        if (!loopFields.TryGetValue(coll, out var fields))
                            loopFields[coll] = fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        fields.Add(member.Member.Name);
                    }
                    break;
            }
            foreach (var child in node.Children)
                Walk(child);
        }

        Walk(parsed.Page);

        foreach (var (key, _) in scalars)
            if (!result.ContainsKey(key) && result.Count < MaxScalarKeys)
                result[key] = ValueForKind(key);

        foreach (var pair in loopFields)
        {
            var collection = pair.Key;
            var fields = pair.Value;
            var items = new List<Dictionary<string, object?>>();
            for (var i = 1; i <= LoopItems; i++)
            {
                if (fields.Count == 0)
                    items.Add(new Dictionary<string, object?> { ["label"] = $"Row {i}" });
                else
                    items.Add(fields.ToDictionary(f => f, ValueForKind, StringComparer.OrdinalIgnoreCase));
            }
            result[collection] = items;
        }
    }

    private static string? MemberKey(ScriptExpression expr)
        => expr is ScriptMemberExpression member
           && member.Target is ScriptVariable target
           && target.Name == "model"
           && member.Member.Name.Length > 0
            ? member.Member.Name
            : null;

    private static object? ValueForColumn(SqlColumnInfo column)
    {
        var type = column.DataType;
        var len = column.MaxLength;
        string Clip(string value) => len.HasValue && value.Length > len.Value ? value.Substring(0, len.Value) : value;

        if (type.StartsWith("nvarchar") || type.StartsWith("varchar") || type is "char" or "text")
            return Clip(NameAwareString(column.Name));
        if (type.StartsWith("int") || type is "smallint" or "bigint" or "tinyint")
            return NameAwareInt(column.Name);
        if (type.StartsWith("decimal") || type is "numeric" or "money" or "smallmoney")
            return NameAwareDecimal(column.Name);
        if (type.StartsWith("datetime") || type is "date" or "smalldatetime")
            return NameAwareDate(column.Name);
        if (type == "bit") return true;
        if (type == "uniqueidentifier") return Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
        return Clip($"Sample {column.Name}");
    }

    private static string NameAwareString(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("email")) return "jane.doe@agency.gov";
        if (lower.Contains("phone")) return "(860) 555-0142";
        if (lower.Contains("name")) return "Jane Doe";
        if (lower.Contains("address")) return "450 Columbus Blvd, Hartford, CT 06103";
        if (lower.Contains("city")) return "Hartford";
        if (lower.Contains("state")) return "CT";
        if (lower.Contains("zip")) return "06103";
        if (lower.Contains("url") || lower.Contains("website")) return "https://example.gov";
        return $"Sample {key}";
    }

    private static int NameAwareInt(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("qty") || lower.Contains("quantity") || lower.Contains("count")) return 4;
        if (lower.Contains("year")) return 2026;
        return 42;
    }

    private static decimal NameAwareDecimal(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("price") || lower.Contains("amount") || lower.Contains("total") || lower.Contains("cost"))
            return 1250.00m;
        if (lower.Contains("rate") || lower.Contains("tax")) return 0.06m;
        return 99.99m;
    }

    private static object NameAwareDate(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("dob") || lower.Contains("birth")) return new DateTime(1985, 3, 14);
        return DateTime.Today;
    }

    private static string InferKind(string key)
    {
        var lower = key.ToLowerInvariant();
        if (lower.Contains("email")) return "email";
        if (lower.Contains("phone")) return "phone";
        if (lower.EndsWith("date") || lower.EndsWith("time") || lower.EndsWith("day")) return "date";
        if (lower.Contains("amount") || lower.Contains("price") || lower.Contains("total")
            || lower.Contains("rate") || lower.Contains("cost") || lower.Contains("fee")
            || lower.Contains("balance") || lower.Contains("salary") || lower.Contains("tax")) return "decimal";
        if (lower.Contains("id") || lower.Contains("code") || lower.Contains("qty")
            || lower.Contains("quantity") || lower.Contains("count") || lower.Contains("number")
            || lower.Contains("year") || lower.Contains("age")) return "int";
        if (lower.Contains("active") || lower.Contains("enabled") || lower.Contains("approved")
            || lower.Contains("published") || lower.Contains("deleted") || lower.Contains("archived")
            || lower.Contains("is") || lower.Contains("has")) return "bool";
        return "string";
    }

    private static object? ValueForKind(string key)
    {
        var kind = InferKind(key);
        return kind switch
        {
            "email" or "phone" or "string" => NameAwareString(key),
            "date" => NameAwareDate(key),
            "decimal" => NameAwareDecimal(key),
            "int" => NameAwareInt(key),
            "bool" => true,
            _ => $"Sample {key}"
        };
    }
}