using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Data.Entity.Infrastructure.Annotations;
using TemplateBuilder.Domain.Entities;

namespace TemplateBuilder.Infrastructure.EF6.Data;

public class TemplateBuilderDbContext : DbContext
{
    // Set by RegisterTemplateBuilderEditor to honor options.ApplyMigrations. When false,
    // NO database initializer is installed, so EF6 never attempts DDL (CREATE TABLE, ALTER,
    // indexes) — required for DBA-managed databases where the app login is DML-only and the
    // schema is provisioned by the shipped schema script (content/Scripts in the package).
    // The flag is read in the constructor because every context construction re-applies the
    // initializer; a registration-time SetInitializer override alone would not survive it.
    public static bool MigrationsEnabled { get; set; } = true;

    public TemplateBuilderDbContext(string connectionString) : base(connectionString)
    {
        Database.SetInitializer(MigrationsEnabled
            ? new MigrateDatabaseToLatestVersion<TemplateBuilderDbContext, Migrations.Configuration>()
            : null);
    }

    public DbSet<Template> Templates { get; set; } = null!;
    public DbSet<TemplateVersion> TemplateVersions { get; set; } = null!;
    public DbSet<Snippet> Snippets { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<SnippetVersion> SnippetVersions { get; set; } = null!;
    public DbSet<SnippetUsage> SnippetUsages { get; set; } = null!;

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
        template.Property(t => t.ExternalKey).IsRequired().HasColumnAnnotation(
            IndexAnnotation.AnnotationName,
            new IndexAnnotation(new IndexAttribute("IX_Templates_ExternalKey") { IsUnique = true }));
        template.Property(t => t.SourceView).HasMaxLength(200);
        template.Property(t => t.SourceViewSnapshot).IsMaxLength();
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
        version.Property(v => v.TemplateId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_TemplateVersions_TemplateId_VersionNumber", 0) { IsUnique = true }));
        version.Property(v => v.VersionNumber)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_TemplateVersions_TemplateId_VersionNumber", 1) { IsUnique = true }));

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
        snippet.Property(s => s.RowVersion).IsRowVersion();
        snippet.HasMany(s => s.Versions)
            .WithRequired(v => v.Snippet)
            .HasForeignKey(v => v.SnippetId)
            .WillCascadeOnDelete(false);

        var snippetVersion = modelBuilder.Entity<SnippetVersion>();
        snippetVersion.ToTable("SnippetVersions");
        snippetVersion.HasKey(v => v.Id);
        snippetVersion.Property(v => v.Body).IsRequired();
        snippetVersion.Property(v => v.ChangeComment).HasMaxLength(500);
        snippetVersion.Property(v => v.CreatedBy).HasMaxLength(200);
        snippetVersion.Property(v => v.SnippetId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetVersions_SnippetId_VersionNumber", 0) { IsUnique = true }));
        snippetVersion.Property(v => v.VersionNumber)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetVersions_SnippetId_VersionNumber", 1) { IsUnique = true }));

        var audit = modelBuilder.Entity<AuditLog>();
        audit.ToTable("AuditLogs");
        audit.HasKey(a => a.Id);
        audit.Property(a => a.EntityType).IsRequired().HasMaxLength(20);
        audit.Property(a => a.Action).IsRequired().HasMaxLength(40);
        audit.Property(a => a.Actor).IsRequired().HasMaxLength(200);
        audit.Property(a => a.BeforeState).HasMaxLength(4000);
        audit.Property(a => a.AfterState).HasMaxLength(4000);
        audit.Property(a => a.Comment).HasMaxLength(1000);
        audit.Property(a => a.EntityId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_Entity", 0)));
        audit.Property(a => a.EntityType)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_Entity", 1)));
        audit.Property(a => a.OccurredAt)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_Entity", 2)));
        audit.Property(a => a.OccurredAt)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_AuditLogs_OccurredAt")));

        var usage = modelBuilder.Entity<SnippetUsage>();
        usage.ToTable("SnippetUsages");
        usage.HasKey(u => u.Id);
        usage.Property(u => u.UsedBy).HasMaxLength(200);
        usage.Property(u => u.SnippetId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetUsages_SnippetId")));
        usage.Property(u => u.TemplateId)
            .HasColumnAnnotation(
                IndexAnnotation.AnnotationName,
                new IndexAnnotation(new IndexAttribute("IX_SnippetUsages_TemplateId")));
    }
}