using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124124217_DraftExpirationReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsExpirationReceipt",
            table: "FormDrafts",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsExpirationReceipt",
            table: "FormDrafts");
    }
}
