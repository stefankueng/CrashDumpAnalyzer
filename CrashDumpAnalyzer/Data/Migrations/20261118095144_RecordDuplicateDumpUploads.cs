using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecordDuplicateDumpUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DuplicateDumpUpload",
                columns: table => new
                {
                    DuplicateDumpUploadId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DumpFileInfoId = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UploadedFromIp = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateDumpUpload", x => x.DuplicateDumpUploadId);
                    table.ForeignKey(
                        name: "FK_DuplicateDumpUpload_DumpFileInfo_DumpFileInfoId",
                        column: x => x.DumpFileInfoId,
                        principalTable: "DumpFileInfo",
                        principalColumn: "DumpFileInfoId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateDumpUpload_DumpFileInfoId",
                table: "DuplicateDumpUpload",
                column: "DumpFileInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateDumpUpload_UploadDate",
                table: "DuplicateDumpUpload",
                column: "UploadDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateDumpUpload");
        }
    }
}
