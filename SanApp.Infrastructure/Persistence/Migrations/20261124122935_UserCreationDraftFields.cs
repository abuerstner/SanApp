using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124122935_UserCreationDraftFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Email",
            table: "FormDrafts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RolesJson",
            table: "FormDrafts",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Email",
            table: "FormDrafts");

        migrationBuilder.DropColumn(
            name: "RolesJson",
            table: "FormDrafts");
    }
}
