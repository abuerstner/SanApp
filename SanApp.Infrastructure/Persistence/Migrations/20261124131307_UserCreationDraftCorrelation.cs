using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124131307_UserCreationDraftCorrelation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "DraftAcknowledgementDeleted",
            table: "CreationOperations",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DraftAcknowledgementExpiresAtUtc",
            table: "CreationOperations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DraftId",
            table: "CreationOperations",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DraftOwnerUserId",
            table: "CreationOperations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DraftVersion",
            table: "CreationOperations",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DraftAcknowledgementDeleted",
            table: "CreationOperations");

        migrationBuilder.DropColumn(
            name: "DraftAcknowledgementExpiresAtUtc",
            table: "CreationOperations");

        migrationBuilder.DropColumn(
            name: "DraftId",
            table: "CreationOperations");

        migrationBuilder.DropColumn(
            name: "DraftOwnerUserId",
            table: "CreationOperations");

        migrationBuilder.DropColumn(
            name: "DraftVersion",
            table: "CreationOperations");
    }
}
