using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124100121_DraftLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FormDrafts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "text", nullable: false),
                FormType = table.Column<string>(type: "text", nullable: false),
                TargetId = table.Column<string>(type: "text", nullable: true),
                Version = table.Column<string>(type: "text", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeleteCapabilityHash = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: true),
                Location = table.Column<string>(type: "text", nullable: true),
                Customer = table.Column<string>(type: "text", nullable: true),
                StartLocal = table.Column<string>(type: "text", nullable: true),
                StartOffset = table.Column<string>(type: "text", nullable: true),
                EndLocal = table.Column<string>(type: "text", nullable: true),
                EndOffset = table.Column<string>(type: "text", nullable: true),
                DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FormDrafts", x => x.Id);
                table.ForeignKey(
                    name: "FK_FormDrafts_AspNetUsers_OwnerUserId",
                    column: x => x.OwnerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FormDrafts_ExpiresAtUtc",
            table: "FormDrafts",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_FormDrafts_OwnerUserId_ExpiresAtUtc",
            table: "FormDrafts",
            columns: new[] { "OwnerUserId", "ExpiresAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FormDrafts");
    }
}
