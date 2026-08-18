namespace TemplateBuilder.Infrastructure.EF6.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddSampleDataToTemplates : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Templates", "SampleData", c => c.String());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Templates", "SampleData");
        }
    }
}
