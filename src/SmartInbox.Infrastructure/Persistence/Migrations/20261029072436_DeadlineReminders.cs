using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeadlineReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Documents_DocumentId",
                table: "Notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "Notifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeadEmail",
                table: "Departments",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 1,
                column: "HeadEmail",
                value: "chief.of.staff@mds.demo");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 2,
                column: "HeadEmail",
                value: "legal.head@mds.demo");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 3,
                column: "HeadEmail",
                value: "finance.head@mds.demo");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 4,
                column: "HeadEmail",
                value: "hr.head@mds.demo");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 5,
                column: "HeadEmail",
                value: "it.head@mds.demo");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 6,
                column: "HeadEmail",
                value: "customer.head@mds.demo");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 7,
                column: "HeadEmail",
                value: "strategy.head@mds.demo");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DedupKey",
                table: "Notifications",
                column: "DedupKey",
                unique: true,
                filter: "[DedupKey] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Documents_DocumentId",
                table: "Notifications",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Documents_DocumentId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_DedupKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "HeadEmail",
                table: "Departments");

            migrationBuilder.AlterColumn<Guid>(
                name: "DocumentId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Documents_DocumentId",
                table: "Notifications",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
