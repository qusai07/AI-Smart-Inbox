using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentHandling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedDepartmentId",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Handling",
                table: "Documents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandlingNote",
                table: "Documents",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HandlingUpdatedAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_AssignedDepartmentId_Handling",
                table: "Documents",
                columns: new[] { "AssignedDepartmentId", "Handling" });

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Departments_AssignedDepartmentId",
                table: "Documents",
                column: "AssignedDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill: documents routed before this migration go to their department's inbox as New.
            migrationBuilder.Sql("""
                UPDATE d
                SET d.AssignedDepartmentId = r.FinalDepartmentId,
                    d.Handling = 'New',
                    d.HandlingUpdatedAt = r.DecidedAt
                FROM Documents d
                CROSS APPLY (SELECT TOP 1 FinalDepartmentId, DecidedAt FROM RoutingDecisions
                             WHERE DocumentId = d.Id ORDER BY DecidedAt DESC) r
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Departments_AssignedDepartmentId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_AssignedDepartmentId_Handling",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AssignedDepartmentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Handling",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "HandlingNote",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "HandlingUpdatedAt",
                table: "Documents");
        }
    }
}
