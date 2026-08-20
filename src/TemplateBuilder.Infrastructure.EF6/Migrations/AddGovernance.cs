namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System;
    using System.Data.Entity.Migrations;

    public partial class AddGovernance : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Templates", "Status", c => c.Int(nullable: false, defaultValue: 3));
            AddColumn("dbo.Templates", "DraftBody", c => c.String());
            AddColumn("dbo.Templates", "ReviewComment", c => c.String(maxLength: 1000));
            AddColumn("dbo.Snippets", "RowVersion", c => c.Binary(nullable: false, defaultValueSql: "0x00000000000007D0"));

            // Pre-governance PublishVersionAsync computed MAX(VersionNumber)+1 then inserted in a
            // separate query, so a client DB may already contain duplicate (TemplateId, VersionNumber)
            // rows. Renumber every row beyond the first per (TemplateId, VersionNumber) to the next
            // free version number before the unique index below is created.
            Sql(@"DECLARE @rowId int
                  DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
                      SELECT tv.Id
                      FROM dbo.TemplateVersions tv
                      WHERE EXISTS (
                          SELECT 1 FROM dbo.TemplateVersions x
                          WHERE x.TemplateId = tv.TemplateId
                            AND x.VersionNumber = tv.VersionNumber
                            AND x.Id < tv.Id)
                      ORDER BY tv.TemplateId, tv.VersionNumber, tv.Id
                  OPEN cur
                  FETCH NEXT FROM cur INTO @rowId
                  WHILE @@FETCH_STATUS = 0
                  BEGIN
                      UPDATE dbo.TemplateVersions
                      SET VersionNumber = (
                          SELECT ISNULL(MAX(VersionNumber), 0) + 1
                          FROM dbo.TemplateVersions
                          WHERE TemplateId = (SELECT TemplateId FROM dbo.TemplateVersions WHERE Id = @rowId))
                      WHERE Id = @rowId
                      FETCH NEXT FROM cur INTO @rowId
                  END
                  CLOSE cur
                  DEALLOCATE cur");

            CreateIndex("dbo.TemplateVersions", new[] { "TemplateId", "VersionNumber" }, unique: true, name: "IX_TemplateVersions_TemplateId_VersionNumber");

            CreateTable(
                "dbo.AuditLogs",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    EntityType = c.String(nullable: false, maxLength: 20),
                    EntityId = c.Int(nullable: false),
                    Action = c.String(nullable: false, maxLength: 40),
                    Actor = c.String(nullable: false, maxLength: 200),
                    OccurredAt = c.DateTime(nullable: false),
                    BeforeState = c.String(maxLength: 4000),
                    AfterState = c.String(maxLength: 4000),
                    Comment = c.String(maxLength: 1000)
                })
                .PrimaryKey(t => t.Id);
            CreateIndex("dbo.AuditLogs", new[] { "EntityType", "EntityId", "OccurredAt" }, name: "IX_AuditLogs_Entity");
            CreateIndex("dbo.AuditLogs", new[] { "OccurredAt" }, name: "IX_AuditLogs_OccurredAt");

            CreateTable(
                "dbo.SnippetVersions",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    SnippetId = c.Int(nullable: false),
                    VersionNumber = c.Int(nullable: false),
                    Body = c.String(nullable: false),
                    ChangeComment = c.String(maxLength: 500),
                    CreatedAt = c.DateTime(nullable: false),
                    CreatedBy = c.String(maxLength: 200)
                })
                .PrimaryKey(t => t.Id);
            CreateIndex("dbo.SnippetVersions", new[] { "SnippetId", "VersionNumber" }, unique: true, name: "IX_SnippetVersions_SnippetId_VersionNumber");

            CreateTable(
                "dbo.SnippetUsages",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    SnippetId = c.Int(nullable: false),
                    TemplateId = c.Int(nullable: false),
                    UsedAt = c.DateTime(nullable: false),
                    UsedBy = c.String(maxLength: 200)
                })
                .PrimaryKey(t => t.Id);
            CreateIndex("dbo.SnippetUsages", new[] { "SnippetId" }, name: "IX_SnippetUsages_SnippetId");
            CreateIndex("dbo.SnippetUsages", new[] { "TemplateId" }, name: "IX_SnippetUsages_TemplateId");

            Sql(@"INSERT INTO dbo.SnippetVersions (SnippetId, VersionNumber, Body, ChangeComment, CreatedAt, CreatedBy)
                 SELECT Id, 1, Body, 'Initial version', CreatedAt, NULL FROM dbo.Snippets");
        }

        public override void Down()
        {
            DropIndex("dbo.SnippetUsages", "IX_SnippetUsages_TemplateId");
            DropIndex("dbo.SnippetUsages", "IX_SnippetUsages_SnippetId");
            DropTable("dbo.SnippetUsages");
            DropIndex("dbo.SnippetVersions", "IX_SnippetVersions_SnippetId_VersionNumber");
            DropTable("dbo.SnippetVersions");
            DropIndex("dbo.AuditLogs", "IX_AuditLogs_OccurredAt");
            DropIndex("dbo.AuditLogs", "IX_AuditLogs_Entity");
            DropTable("dbo.AuditLogs");
            DropIndex("dbo.TemplateVersions", "IX_TemplateVersions_TemplateId_VersionNumber");
            DropColumn("dbo.Snippets", "RowVersion");
            DropColumn("dbo.Templates", "ReviewComment");
            DropColumn("dbo.Templates", "DraftBody");
            DropColumn("dbo.Templates", "Status");
        }
    }
}