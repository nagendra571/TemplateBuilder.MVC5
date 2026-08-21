using System;
using System.Data.Entity.Infrastructure;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6;

public class TemplateBuilderDbContextFactory : IDbContextFactory<TemplateBuilderDbContext>
{
    // Runtime bridge set by TemplateBuilder.Editor.Mvc5's RegisterTemplateBuilderEditor with
    // the consumer's options.ConnectionString. Without it, EF6's DbMigrator (inside
    // MigrateDatabaseToLatestVersion) discovers this factory by convention ({ContextName}Factory)
    // at runtime, and Create() would resolve a NAMED connection string "TemplateBuilderDbContext"
    // from the app config — a consumer who only sets options.ConnectionString (e.g. a name like
    // "TemplateDb") gets "No connection string named 'TemplateBuilderDbContext' could be found in
    // the application config file." With the provider set, runtime migrations run against the
    // explicit connection string. Null at design time (Package Manager Console), where the named
    // connection string is still the intended way for tooling to create a context.
    public static Func<string>? ConnectionStringProvider { get; set; }

    public TemplateBuilderDbContext Create()
    {
        var connectionString = ConnectionStringProvider?.Invoke();
        return new TemplateBuilderDbContext(
            string.IsNullOrWhiteSpace(connectionString)
                ? "name=TemplateBuilderDbContext"
                : connectionString!);
    }
}
