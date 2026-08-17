using System.Data.Entity.Infrastructure;
using TemplateBuilder.Infrastructure.EF6.Data;

namespace TemplateBuilder.Infrastructure.EF6;

public class TemplateBuilderDbContextFactory : IDbContextFactory<TemplateBuilderDbContext>
{
    public TemplateBuilderDbContext Create()
        => new TemplateBuilderDbContext("name=TemplateBuilderDbContext");
}