using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using TemplateBuilder.Application.DTOs;
using TemplateBuilder.Application.Options;

namespace TemplateBuilder.Application.Services;

public class SqlViewDiscoveryService : ISqlViewDiscoveryService
{
    private const string NamesCacheKey = "TemplateBuilder.SqlViews.Names";
    private const string ColumnsCacheKeyPrefix = "TemplateBuilder.SqlViews.Columns.";

    private readonly string _connectionString;
    private readonly TemplateBuilderOptions _options;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public SqlViewDiscoveryService(string connectionString, TemplateBuilderOptions options)
    {
        _connectionString = connectionString;
        _options = options;
    }

    public async Task<IReadOnlyList<string>> GetViewNamesAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(NamesCacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var views = new List<string>();
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME",
            connection)
        {
            CommandTimeout = _options.SqlCommandTimeoutSeconds
        };
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            views.Add(reader.GetString(0));

        var result = views.AsReadOnly();
        _cache.Set(NamesCacheKey, result, _options.ViewDiscoveryCacheDuration);
        return result;
    }

    public async Task<IReadOnlyList<SqlColumnInfo>> GetViewColumnsAsync(string viewName, CancellationToken ct = default)
    {
        var cacheKey = ColumnsCacheKeyPrefix + viewName;
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SqlColumnInfo>? cached) && cached is not null)
            return cached;

        var columns = new List<SqlColumnInfo>();
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand(
            @"SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
              FROM INFORMATION_SCHEMA.COLUMNS
              WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @viewName
              ORDER BY ORDINAL_POSITION",
            connection)
        {
            CommandTimeout = _options.SqlCommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("@viewName", viewName);

        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new SqlColumnInfo
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                IsNullable = reader.GetString(3) == "YES"
            });
        }

        var result = columns.AsReadOnly();
        _cache.Set(cacheKey, result, _options.ViewDiscoveryCacheDuration);
        return result;
    }
}