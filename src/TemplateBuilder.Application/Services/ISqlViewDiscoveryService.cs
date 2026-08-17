using TemplateBuilder.Application.DTOs;
namespace TemplateBuilder.Application.Services;

public interface ISqlViewDiscoveryService
{
    Task<IReadOnlyList<string>> GetViewNamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SqlColumnInfo>> GetViewColumnsAsync(string viewName, CancellationToken ct = default);
}