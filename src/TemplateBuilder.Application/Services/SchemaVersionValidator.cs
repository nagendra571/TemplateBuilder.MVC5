using System.Data.SqlClient;
using TemplateBuilder.Domain.Exceptions;

namespace TemplateBuilder.Application.Services;

public class SchemaVersionValidator
{
    public const int CurrentSchemaVersion = 1;
    public const string SchemaVersionViewName = "__TemplateBuilderSchemaVersion";

    public void ValidateSchemaVersion(int? discoveredVersion)
    {
        if (discoveredVersion is null)
            throw new SchemaVersionMismatchException(
                $"The '{SchemaVersionViewName}' view was not found in the database. " +
                "The TemplateBuilder schema has not been initialized for this database.");

        if (discoveredVersion < CurrentSchemaVersion)
            throw new SchemaVersionMismatchException(
                $"The database schema version ({discoveredVersion}) is older than the schema version this " +
                $"package requires ({CurrentSchemaVersion}). Run the latest migrations and retry.");
    }

    public async Task<int?> ReadSchemaVersionAsync(string connectionString, CancellationToken ct = default)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand(
            $"SELECT TOP 1 SchemaVersion FROM dbo.[{SchemaVersionViewName}]",
            connection);
        try
        {
            using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;
            return reader.IsDBNull(0) ? null : reader.GetInt32(0);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return null;
        }
    }
}