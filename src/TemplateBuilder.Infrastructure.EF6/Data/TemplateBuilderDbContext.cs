using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Data.Entity.Infrastructure.Annotations;
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Infrastructure.EF6.Data;

public class TemplateBuilderDbContext : DbContext
{
    public TemplateBuilderDbContext(string connectionString) : base(connectionString)
    {
        Database.SetInitializer(new MigrateDatabaseToLatestVersion<TemplateBuilderDbContext, Migrations.Configuration>());
    }

    public DbSet<Template> Templates { get; set; } = null!;
    public DbSet<TemplateVersion> TemplateVersions { get; set; } = null!;
    public DbSet<Snippet> Snippets { get; set; } = null!;

    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        var template = modelBuilder.Entity<Template>();
        template.ToTable("Templates");
        template.HasKey(t => t.Id);
        template.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_Templates_Name") { IsUnique = true }));
        template.Property(t => t.TemplateType).IsRequired().HasMaxLength(50);
        template.Property(t => t.Description).HasMaxLength(500);
        template.Property(t => t.RowVersion).IsRowVersion();
        template.HasMany(t => t.Versions)
            .WithRequired(v => v.Template)
            .HasForeignKey(v => v.TemplateId)
            .WillCascadeOnDelete(false);
        template.HasOptional(t => t.CurrentVersion)
            .WithMany()
            .HasForeignKey(t => t.CurrentVersionId)
            .WillCascadeOnDelete(false);

        var version = modelBuilder.Entity<TemplateVersion>();
        version.ToTable("TemplateVersions");
        version.HasKey(v => v.Id);
        version.Property(v => v.Body).IsRequired();
        version.Property(v => v.ChangeComment).HasMaxLength(500);
        version.Property(v => v.CreatedBy).HasMaxLength(200);

        var snippet = modelBuilder.Entity<Snippet>();
        snippet.ToTable("Snippets");
        snippet.HasKey(s => s.Id);
        snippet.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_Snippets_Name") { IsUnique = true }));
        snippet.Property(s => s.Description).HasMaxLength(500);
        snippet.Property(s => s.Body).IsRequired();
    }
}