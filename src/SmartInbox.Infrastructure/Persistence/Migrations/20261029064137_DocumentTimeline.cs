using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_DocumentId_At",
                table: "DocumentEvents",
                columns: new[] { "DocumentId", "At" });

            // Backfill a timeline for documents that existed before history was recorded.
            migrationBuilder.Sql("""
                INSERT INTO DocumentEvents (DocumentId, Type, Actor, DepartmentId, Note, At)
                SELECT Id, 'Uploaded', UploadedBy, NULL, NULL, UploadedAt FROM Documents;

                INSERT INTO DocumentEvents (DocumentId, Type, Actor, DepartmentId, Note, At)
                SELECT DocumentId, 'Analyzed', ModelUsed, SuggestedDepartmentId,
                       CONCAT('confidence ', FORMAT(Confidence, '0.00')), CreatedAt
                FROM Analyses;

                INSERT INTO DocumentEvents (DocumentId, Type, Actor, DepartmentId, Note, At)
                SELECT DocumentId, 'Routed', DecidedBy, FinalDepartmentId, Note, DecidedAt FROM RoutingDecisions;

                INSERT INTO DocumentEvents (DocumentId, Type, Actor, DepartmentId, Note, At)
                SELECT DocumentId, 'EmailSent', 'system', DepartmentId, [To], SentAt FROM Notifications WHERE Status = 'Sent';

                -- Only the latest department action is known for old documents.
                INSERT INTO DocumentEvents (DocumentId, Type, Actor, DepartmentId, Note, At)
                SELECT Id, Handling, HandlingBy, AssignedDepartmentId, HandlingNote, HandlingUpdatedAt
                FROM Documents WHERE Handling IN ('Acknowledged', 'Closed') AND HandlingUpdatedAt IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentEvents");
        }
    }
}
