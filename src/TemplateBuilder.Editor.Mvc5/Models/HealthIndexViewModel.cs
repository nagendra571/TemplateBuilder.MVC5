using System.Collections.Generic;
using TemplateBuilder.Application.Services;

namespace TemplateBuilder.Editor.Mvc5.Models;

public class HealthRowViewModel
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = "";
    public TemplateHealthReport Report { get; set; } = new();
}

public class HealthIndexViewModel
{
    public List<HealthRowViewModel> Rows { get; set; } = new();
}
