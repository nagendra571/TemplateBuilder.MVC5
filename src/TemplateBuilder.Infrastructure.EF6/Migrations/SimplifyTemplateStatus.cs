namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class SimplifyTemplateStatus : DbMigration
    {
        public override void Up()
        {
            DropColumn("dbo.Templates", "Status");
            DropColumn("dbo.Templates", "DraftBody");
            DropColumn("dbo.Templates", "ReviewComment");
        }
        
        public override void Down()
        {
            AddColumn("dbo.Templates", "ReviewComment", c => c.String(maxLength: 1000));
            AddColumn("dbo.Templates", "DraftBody", c => c.String());
            AddColumn("dbo.Templates", "Status", c => c.Int(nullable: false));
        }
    }
}
