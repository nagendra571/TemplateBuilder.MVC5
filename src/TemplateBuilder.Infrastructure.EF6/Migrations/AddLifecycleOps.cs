namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddLifecycleOps : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Templates", "ExternalKey", c => c.Guid(nullable: false));
            AddColumn("dbo.Templates", "SourceView", c => c.String(maxLength: 200));
            AddColumn("dbo.Templates", "SourceViewSnapshot", c => c.String());
            Sql("UPDATE dbo.Templates SET ExternalKey = NEWID() WHERE ExternalKey = '00000000-0000-0000-0000-000000000000'");
            CreateIndex("dbo.Templates", "ExternalKey", unique: true, name: "IX_Templates_ExternalKey");
        }
        
        public override void Down()
        {
            DropIndex("dbo.Templates", "IX_Templates_ExternalKey");
            DropColumn("dbo.Templates", "SourceViewSnapshot");
            DropColumn("dbo.Templates", "SourceView");
            DropColumn("dbo.Templates", "ExternalKey");
        }
    }
}
