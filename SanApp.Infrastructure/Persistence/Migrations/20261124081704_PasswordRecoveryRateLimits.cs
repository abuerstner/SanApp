using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124081704_PasswordRecoveryRateLimits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PasswordRecoveryRateLimitEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                KeyHash = table.Column<string>(type: "text", nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PasswordRecoveryRateLimitEntries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PasswordRecoveryRateLimitEntries_KeyHash_RequestedAtUtc",
            table: "PasswordRecoveryRateLimitEntries",
            columns: new[] { "KeyHash", "RequestedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PasswordRecoveryRateLimitEntries");
    }
}
