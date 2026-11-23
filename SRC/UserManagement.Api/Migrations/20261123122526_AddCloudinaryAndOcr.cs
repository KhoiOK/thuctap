using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagement.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudinaryAndOcr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FrontImagePath",
                table: "IdentityDocuments",
                newName: "FrontImagePublicId");

            migrationBuilder.RenameColumn(
                name: "BackImagePath",
                table: "IdentityDocuments",
                newName: "BackImagePublicId");

            migrationBuilder.AddColumn<int>(
                name: "BackImageHeight",
                table: "IdentityDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BackImageVersion",
                table: "IdentityDocuments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackImageWidth",
                table: "IdentityDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FrontImageHeight",
                table: "IdentityDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FrontImageVersion",
                table: "IdentityDocuments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FrontImageWidth",
                table: "IdentityDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OcrExtractionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    FrontImagePublicId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BackImagePublicId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RawResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractedFields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessingTimeMs = table.Column<int>(type: "int", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrExtractionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcrExtractionResults_IdentityDocuments",
                        column: x => x.DocumentId,
                        principalTable: "IdentityDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OcrExtractionResults_Users",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractionResults_DocumentId",
                table: "OcrExtractionResults",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractionResults_Status_ExpiresAt",
                table: "OcrExtractionResults",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OcrExtractionResults_UserId_CreatedAt",
                table: "OcrExtractionResults",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_OcrExtractionResults_ScanId",
                table: "OcrExtractionResults",
                column: "ScanId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OcrExtractionResults");

            migrationBuilder.DropColumn(
                name: "BackImageHeight",
                table: "IdentityDocuments");

            migrationBuilder.DropColumn(
                name: "BackImageVersion",
                table: "IdentityDocuments");

            migrationBuilder.DropColumn(
                name: "BackImageWidth",
                table: "IdentityDocuments");

            migrationBuilder.DropColumn(
                name: "FrontImageHeight",
                table: "IdentityDocuments");

            migrationBuilder.DropColumn(
                name: "FrontImageVersion",
                table: "IdentityDocuments");

            migrationBuilder.DropColumn(
                name: "FrontImageWidth",
                table: "IdentityDocuments");

            migrationBuilder.RenameColumn(
                name: "FrontImagePublicId",
                table: "IdentityDocuments",
                newName: "FrontImagePath");

            migrationBuilder.RenameColumn(
                name: "BackImagePublicId",
                table: "IdentityDocuments",
                newName: "BackImagePath");
        }
    }
}
