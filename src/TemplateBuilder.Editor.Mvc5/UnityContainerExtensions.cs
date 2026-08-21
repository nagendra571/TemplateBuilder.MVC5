using System;
using Unity;
using Unity.Lifetime;
using TemplateBuilder.Application.Options;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Interfaces;
using TemplateBuilder.Editor.Mvc5.Authorization;
using TemplateBuilder.Infrastructure.EF6;
using TemplateBuilder.Infrastructure.EF6.Data;
using TemplateBuilder.Infrastructure.EF6.Repositories;

namespace TemplateBuilder.Editor.Mvc5;

public static class UnityContainerExtensions
{
    public static IUnityContainer RegisterTemplateBuilderEditor(
        this IUnityContainer container,
        Action<TemplateBuilderEditorOptions> configure)
    {
        var options = new TemplateBuilderEditorOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException(
                "TemplateBuilder.Editor.Mvc5 requires a connection string. " +
                "Set options.ConnectionString in RegisterTemplateBuilderEditor().");

        var connectionString = options.ConnectionString;

        // HierarchicalLifetimeManager == Unity.Mvc5's per-request scope, via its
        // child-container-per-HTTP-request pattern (UnityPerRequestHttpModule).
        container.RegisterFactory<TemplateBuilderDbContext>(
            c => new TemplateBuilderDbContext(connectionString),
            new HierarchicalLifetimeManager());

        container.RegisterType<ITemplateRepository, TemplateRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<ISnippetRepository, SnippetRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<IHtmlSanitizerService, HtmlSanitizerService>(new ContainerControlledLifetimeManager());
        container.RegisterType<ITemplateEngine, TemplateEngine>(new HierarchicalLifetimeManager());
        container.RegisterType<ISampleDataGenerator, SampleDataGenerator>(new HierarchicalLifetimeManager());
        container.RegisterType<IAuditRepository, AuditRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<IAuditStatsRepository, AuditStatsRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<IAuditService, AuditService>(new ContainerControlledLifetimeManager());
        container.RegisterType<ITemplatePromotionRepository, TemplatePromotionRepository>(new HierarchicalLifetimeManager());
        container.RegisterType<ITemplatePromotionService, TemplatePromotionService>(new HierarchicalLifetimeManager());
        container.RegisterType<ITemplateHealthService, TemplateHealthService>(new HierarchicalLifetimeManager());
        container.RegisterInstance(new TemplateBuilderOptions());
        container.RegisterFactory<ISqlViewDiscoveryService>(
            c => new SqlViewDiscoveryService(connectionString, c.Resolve<TemplateBuilderOptions>()),
            new HierarchicalLifetimeManager());

        TemplateBuilderAuthorizationFilter.Configure(options.Authorization);
        TemplateBuilderEditorOptions.Current = options;

        // Runtime migrations must run against the consumer's explicit connection string.
        // Without this, EF6's DbMigrator discovers the design-time TemplateBuilderDbContextFactory
        // by convention ({ContextName}Factory) at runtime, and its Create() resolves a NAMED
        // connection string "TemplateBuilderDbContext" from the app config — a consumer who only
        // sets options.ConnectionString (e.g. a name like "TemplateDb") gets "No connection string
        // named 'TemplateBuilderDbContext' could be found in the application config file."
        TemplateBuilderDbContextFactory.ConnectionStringProvider = () => options.ConnectionString;

        // options.ApplyMigrations=false: DBA-managed database — the schema is provisioned by the
        // shipped script (content/Scripts), so no initializer is installed and DDL is never attempted.
        TemplateBuilderDbContext.MigrationsEnabled = options.ApplyMigrations;

        // Triggers EF6 MigrateDatabaseToLatestVersion on first access — mirrors the ASP.NET Core
        // MigrationHostedService's "migrate on startup" behavior without a hosted-service concept in MVC5.
        if (options.ApplyMigrations)
        {
            using var migrationContext = new TemplateBuilderDbContext(connectionString);
            migrationContext.Database.Initialize(force: false);
        }

        return container;
    }
}