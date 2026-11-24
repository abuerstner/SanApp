using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SanAppDbContext))]
[Migration("20261124113000_BindDraftAccountSwitchTarget")]
public partial class BindDraftAccountSwitchTarget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AccountSwitchTargetHash",
            table: "FormDrafts",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AccountSwitchTargetHash",
            table: "FormDrafts");
    }
}
