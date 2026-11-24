using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124082033_PasswordRecoveryRateLimitExpiryIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_PasswordRecoveryRateLimitEntries_RequestedAtUtc",
            table: "PasswordRecoveryRateLimitEntries",
            column: "RequestedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PasswordRecoveryRateLimitEntries_RequestedAtUtc",
            table: "PasswordRecoveryRateLimitEntries");
    }
}
