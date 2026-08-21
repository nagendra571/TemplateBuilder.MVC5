using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TemplateBuilder.Editor.Mvc5.Models;

public class TemplateEditorViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string TemplateType { get; set; } = "Email";

    [StringLength(500)]
    public string? Description { get; set; }

    public string? SampleData { get; set; }

    public string? SourceView { get; set; }
    public string? Body { get; set; }
    public int? CurrentVersionId { get; set; }
    public int CurrentVersionNumber { get; set; }
    public List<string> AvailableViews { get; set; } = new();
}