using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplyDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatoryNameAr",
                table: "Departments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatoryNameEn",
                table: "Departments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatoryTitleAr",
                table: "Departments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatoryTitleEn",
                table: "Departments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReplyDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Engine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubjectEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BodyEn = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    SubjectAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BodyAr = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    MissingInfo = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Commitments = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EditedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EditedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplyDrafts_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "SignatoryNameAr", "SignatoryNameEn", "SignatoryTitleAr", "SignatoryTitleEn" },
                values: new object[] { null, null, "مدير مكتب الوزير", "Director, Minister's Office" });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "SignatoryNameAr", "SignatoryNameEn", "SignatoryTitleAr", "SignatoryTitleEn" },
                values: new object[] { null, null, "مدير إدارة الشؤون القانونية", "Director, Legal Affairs" });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "SignatoryNameAr", "SignatoryNameEn", "SignatoryTitleAr", "SignatoryTitleEn" },
                values: new object[] { null, null, "مدير إدارة المالية والمشتريات", "Director, Finance & Procurement" });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "SignatoryNameAr", "SignatoryNameEn", "SignatoryTitleAr", "SignatoryTitleEn" },
                values: new object[] { null, null, "مدير إدارة الموارد البشرية", "Director, Human Resources" });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "SignatoryNameAr", "SignatoryNameEn", "SignatoryTitleAr", "SignatoryTitleEn" },
                values: new object[] { null, null, "مدير إدارة تقنية المعلومات", "Director, Information Technology" });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "SignatoryNameAr", "SignatoryNameEn", "SignatoryTitleAr", "SignatoryTitleEn" },
                values: new object[] { null, null, "مدير إدارة إسعاد المتعاملين", "Director, Customer Happiness" });

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "SignatoryNameAr", "SignatoryNameEn", "SignatoryTitleAr", "SignatoryTitleEn" },
                values: new object[] { null, null, "مدير إدارة الاستراتيجية والأداء", "Director, Strategy & Performance" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyDrafts_DocumentId_Version",
                table: "ReplyDrafts",
                columns: new[] { "DocumentId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplyDrafts");

            migrationBuilder.DropColumn(
                name: "SignatoryNameAr",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "SignatoryNameEn",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "SignatoryTitleAr",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "SignatoryTitleEn",
                table: "Departments");
        }
    }
}
