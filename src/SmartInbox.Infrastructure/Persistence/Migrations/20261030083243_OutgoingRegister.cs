using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutgoingRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Departments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutgoingLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SubjectEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SubjectAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplyDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentVia = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SentNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingLetters_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutgoingLetters_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RegisterCounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Next = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisterCounters", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 1,
                column: "Code",
                value: "MIN");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 2,
                column: "Code",
                value: "LEG");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 3,
                column: "Code",
                value: "FIN");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 4,
                column: "Code",
                value: "HR");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 5,
                column: "Code",
                value: "IT");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 6,
                column: "Code",
                value: "CX");

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 7,
                column: "Code",
                value: "STR");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_DepartmentId",
                table: "OutgoingLetters",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_DocumentId",
                table: "OutgoingLetters",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_Number",
                table: "OutgoingLetters",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingLetters_Year_Sequence",
                table: "OutgoingLetters",
                columns: new[] { "Year", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_RegisterCounters_Year_Scope",
                table: "RegisterCounters",
                columns: new[] { "Year", "Scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutgoingLetters");

            migrationBuilder.DropTable(
                name: "RegisterCounters");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Departments");
        }
    }
}
