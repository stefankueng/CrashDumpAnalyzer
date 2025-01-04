using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class _008 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogSummary",
                table: "DumpFileInfo",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "LogFileLine",
                columns: table => new
                {
                    LogFileLineId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LineNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DumpFileInfoId = table.Column<int>(type: "INTEGER", nullable: true),
                    DumpCallstackId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogFileLine", x => x.LogFileLineId);
                    table.ForeignKey(
                        name: "FK_LogFileLine_DumpCallstack_DumpCallstackId",
                        column: x => x.DumpCallstackId,
                        principalTable: "DumpCallstack",
                        principalColumn: "DumpCallstackId");
                    table.ForeignKey(
                        name: "FK_LogFileLine_DumpFileInfo_DumpFileInfoId",
                        column: x => x.DumpFileInfoId,
                        principalTable: "DumpFileInfo",
                        principalColumn: "DumpFileInfoId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogFileLine_DumpCallstackId",
                table: "LogFileLine",
                column: "DumpCallstackId");

            migrationBuilder.CreateIndex(
                name: "IX_LogFileLine_DumpFileInfoId",
                table: "LogFileLine",
                column: "DumpFileInfoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogFileLine");

            migrationBuilder.DropColumn(
                name: "LogSummary",
                table: "DumpFileInfo");
        }
    }
}
