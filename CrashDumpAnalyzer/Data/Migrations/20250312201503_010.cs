using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class _010 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LogFileData",
                columns: table => new
                {
                    LogFileDataId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LineNumberString = table.Column<string>(type: "TEXT", nullable: true),
                    LatestTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DumpFileInfoId = table.Column<int>(type: "INTEGER", nullable: true),
                    DumpCallstackId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogFileData", x => x.LogFileDataId);
                    table.ForeignKey(
                        name: "FK_LogFileData_DumpCallstack_DumpCallstackId",
                        column: x => x.DumpCallstackId,
                        principalTable: "DumpCallstack",
                        principalColumn: "DumpCallstackId");
                    table.ForeignKey(
                        name: "FK_LogFileData_DumpFileInfo_DumpFileInfoId",
                        column: x => x.DumpFileInfoId,
                        principalTable: "DumpFileInfo",
                        principalColumn: "DumpFileInfoId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogFileData_DumpCallstackId",
                table: "LogFileData",
                column: "DumpCallstackId");

            migrationBuilder.CreateIndex(
                name: "IX_LogFileData_DumpFileInfoId",
                table: "LogFileData",
                column: "DumpFileInfoId");

            // Insert combined data from LogFileLine into LogFileData
            migrationBuilder.Sql(@"
                INSERT INTO LogFileData (LineNumberString, LatestTime, DumpFileInfoId, DumpCallstackId)
                SELECT 
                    GROUP_CONCAT(LineNumber, ',') AS LineNumberString,
                    MAX(Time) AS LatestTime,
                    DumpFileInfoId,
                    DumpCallstackId
                FROM LogFileLine
                GROUP BY DumpFileInfoId, DumpCallstackId
            ");

            // Drop the old LogFileLine table
            migrationBuilder.DropTable(
                name: "LogFileLine");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogFileData");

            migrationBuilder.CreateTable(
                name: "LogFileLine",
                columns: table => new
                {
                    LogFileLineId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DumpFileInfoId = table.Column<int>(type: "INTEGER", nullable: true),
                    DumpCallstackId = table.Column<int>(type: "INTEGER", nullable: true),
                    LineNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false)
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
    }
}
