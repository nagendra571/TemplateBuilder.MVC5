namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddVersionIsActive : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.TemplateVersions", "IsActive", c => c.Boolean(nullable: false, defaultValue: true));
        }
        
        public override void Down()
        {
            DropColumn("dbo.TemplateVersions", "IsActive");
        }
    }
}
