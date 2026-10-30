using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelatedLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InReplyTo",
                table: "InboundEmails",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "References",
                table: "InboundEmails",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Documents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceKey",
                table: "Documents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextHash",
                table: "Documents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelatedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OutgoingLetterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentLinks_Documents_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentLinks_OutgoingLetters_OutgoingLetterId",
                        column: x => x.OutgoingLetterId,
                        principalTable: "OutgoingLetters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ContentHash",
                table: "Documents",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ReferenceKey",
                table: "Documents",
                column: "ReferenceKey");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TextHash",
                table: "Documents",
                column: "TextHash");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_DocumentId_RelatedDocumentId",
                table: "DocumentLinks",
                columns: new[] { "DocumentId", "RelatedDocumentId" },
                unique: true,
                filter: "[RelatedDocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_OutgoingLetterId",
                table: "DocumentLinks",
                column: "OutgoingLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_RelatedDocumentId",
                table: "DocumentLinks",
                column: "RelatedDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentLinks");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ContentHash",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ReferenceKey",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_TextHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "InReplyTo",
                table: "InboundEmails");

            migrationBuilder.DropColumn(
                name: "References",
                table: "InboundEmails");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ReferenceKey",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TextHash",
                table: "Documents");
        }
    }
}
