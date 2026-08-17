namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialCreate : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Snippets",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        Name = c.String(nullable: false, maxLength: 200),
                        Description = c.String(maxLength: 500),
                        Body = c.String(nullable: false),
                        CreatedAt = c.DateTime(nullable: false),
                        UpdatedAt = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .Index(t => t.Name, unique: true, name: "IX_Snippets_Name");
            
            CreateTable(
                "dbo.Templates",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        Name = c.String(nullable: false, maxLength: 200),
                        TemplateType = c.String(nullable: false, maxLength: 50),
                        Description = c.String(maxLength: 500),
                        CurrentVersionId = c.Int(),
                        IsActive = c.Boolean(nullable: false),
                        CreatedAt = c.DateTime(nullable: false),
                        UpdatedAt = c.DateTime(nullable: false),
                        RowVersion = c.Binary(nullable: false, fixedLength: true, timestamp: true, storeType: "rowversion"),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.TemplateVersions", t => t.CurrentVersionId)
                .Index(t => t.Name, unique: true, name: "IX_Templates_Name")
                .Index(t => t.CurrentVersionId);
            
            CreateTable(
                "dbo.TemplateVersions",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        TemplateId = c.Int(nullable: false),
                        VersionNumber = c.Int(nullable: false),
                        Body = c.String(nullable: false),
                        ChangeComment = c.String(maxLength: 500),
                        CreatedAt = c.DateTime(nullable: false),
                        CreatedBy = c.String(maxLength: 200),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Templates", t => t.TemplateId)
                .Index(t => t.TemplateId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.TemplateVersions", "TemplateId", "dbo.Templates");
            DropForeignKey("dbo.Templates", "CurrentVersionId", "dbo.TemplateVersions");
            DropIndex("dbo.TemplateVersions", new[] { "TemplateId" });
            DropIndex("dbo.Templates", new[] { "CurrentVersionId" });
            DropIndex("dbo.Templates", "IX_Templates_Name");
            DropIndex("dbo.Snippets", "IX_Snippets_Name");
            DropTable("dbo.TemplateVersions");
            DropTable("dbo.Templates");
            DropTable("dbo.Snippets");
        }
    }
}
