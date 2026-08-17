using System.Collections.Generic;
using System.Linq;
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Editor.Mvc5.Models;

public class TemplateListViewModel
{
    public List<Template> Templates { get; set; } = new();
    public string? Search { get; set; }
    public string? TypeFilter { get; set; }

    public Dictionary<string, int> CountByType => Templates
        .GroupBy(t => t.TemplateType)
        .ToDictionary(g => g.Key, g => g.Count());

    public static readonly string[] KnownTypes = { "Email", "Report", "Notice", "Custom" };
}