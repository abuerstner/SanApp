using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanApp.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124114000_SharedCreationOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Type",
            table: "UserCreationOperations",
            type: "text",
            nullable: false,
            defaultValue: "benutzer");

        migrationBuilder.RenameColumn(
            name: "UserId",
            table: "UserCreationOperations",
            newName: "ResourceId");

        migrationBuilder.DropPrimaryKey(
            name: "PK_UserCreationOperations",
            table: "UserCreationOperations");

        migrationBuilder.RenameTable(
            name: "UserCreationOperations",
            newName: "CreationOperations");

        migrationBuilder.AddPrimaryKey(
            name: "PK_CreationOperations",
            table: "CreationOperations",
            columns: new[] { "Type", "OperationId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"CreationOperations\" WHERE \"Type\" <> 'benutzer') THEN RAISE EXCEPTION 'Cannot downgrade non-user creation operations.'; END IF; END $$;");

        migrationBuilder.DropPrimaryKey(
            name: "PK_CreationOperations",
            table: "CreationOperations");

        migrationBuilder.RenameTable(
            name: "CreationOperations",
            newName: "UserCreationOperations");

        migrationBuilder.RenameColumn(
            name: "ResourceId",
            table: "UserCreationOperations",
            newName: "UserId");

        migrationBuilder.DropColumn(
            name: "Type",
            table: "UserCreationOperations");

        migrationBuilder.AddPrimaryKey(
            name: "PK_UserCreationOperations",
            table: "UserCreationOperations",
            column: "OperationId");
    }
}
