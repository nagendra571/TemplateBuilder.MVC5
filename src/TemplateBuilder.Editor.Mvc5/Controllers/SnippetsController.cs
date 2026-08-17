using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Models;

namespace TemplateBuilder.Editor.Mvc5.Controllers;

public class SnippetsController : TemplateBuilderControllerBase
{
    private readonly ISnippetRepository _snippets;

    public SnippetsController(ISnippetRepository snippets) => _snippets = snippets;

    [Route("Templates/Api/Snippets")]
    [HttpGet]
    public async Task<ActionResult> GetAll()
    {
        var snippets = await _snippets.GetAllAsync();
        return JsonOk(snippets.Select(s => new { s.Id, s.Name, s.Description, s.Body }));
    }

    [Route("Templates/Api/Snippets")]
    [HttpPost, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Create()
    {
        var request = await Request.ReadJsonBodyAsync<CreateSnippetRequest>();

        if (string.IsNullOrWhiteSpace(request.Name))
            return JsonError(400, new Models.ErrorResult("INVALID_NAME", "Snippet name is required."));
        if (string.IsNullOrWhiteSpace(request.Body))
            return JsonError(400, new Models.ErrorResult("INVALID_BODY", "Snippet content cannot be empty."));

        var snippet = new Snippet
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Body = request.Body
        };

        try
        {
            var created = await _snippets.CreateAsync(snippet);
            return JsonOk(new { id = created.Id, created.Name });
        }
        catch (DbUpdateException)
        {
            return JsonError(400, new Models.ErrorResult("DUPLICATE_NAME", $"A snippet named '{request.Name.Trim()}' already exists."));
        }
    }

    [Route("Templates/Api/Snippets/{id:int}")]
    [HttpDelete, ValidateJsonAntiForgeryToken]
    public async Task<ActionResult> Delete(int id)
    {
        var snippet = await _snippets.GetByIdAsync(id);
        if (snippet is null) return JsonError(404, new Models.ErrorResult("NOT_FOUND", "Snippet not found."));
        await _snippets.DeleteAsync(id);
        return NoContentResult();
    }
}