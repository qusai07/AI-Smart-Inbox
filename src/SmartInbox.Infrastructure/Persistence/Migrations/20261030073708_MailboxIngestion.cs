using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MailboxIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InboundEmailId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Documents",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Upload");

            migrationBuilder.CreateTable(
                name: "InboundEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Mailbox = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BodyText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    DocumentIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SkippedAttachments = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundEmails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_InboundEmailId",
                table: "Documents",
                column: "InboundEmailId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Source",
                table: "Documents",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_InboundEmails_MessageId",
                table: "InboundEmails",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundEmails_ReceivedAt",
                table: "InboundEmails",
                column: "ReceivedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_InboundEmails_InboundEmailId",
                table: "Documents",
                column: "InboundEmailId",
                principalTable: "InboundEmails",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_InboundEmails_InboundEmailId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "InboundEmails");

            migrationBuilder.DropIndex(
                name: "IX_Documents_InboundEmailId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_Source",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "InboundEmailId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Documents");
        }
    }
}
