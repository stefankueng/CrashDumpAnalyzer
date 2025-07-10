using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class onCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LogFileData_DumpCallstack_DumpCallstackId",
                table: "LogFileData");

            migrationBuilder.DropForeignKey(
                name: "FK_LogFileData_DumpFileInfo_DumpFileInfoId",
                table: "LogFileData");

            migrationBuilder.AddForeignKey(
                name: "FK_LogFileData_DumpCallstack_DumpCallstackId",
                table: "LogFileData",
                column: "DumpCallstackId",
                principalTable: "DumpCallstack",
                principalColumn: "DumpCallstackId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LogFileData_DumpFileInfo_DumpFileInfoId",
                table: "LogFileData",
                column: "DumpFileInfoId",
                principalTable: "DumpFileInfo",
                principalColumn: "DumpFileInfoId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LogFileData_DumpCallstack_DumpCallstackId",
                table: "LogFileData");

            migrationBuilder.DropForeignKey(
                name: "FK_LogFileData_DumpFileInfo_DumpFileInfoId",
                table: "LogFileData");

            migrationBuilder.AddForeignKey(
                name: "FK_LogFileData_DumpCallstack_DumpCallstackId",
                table: "LogFileData",
                column: "DumpCallstackId",
                principalTable: "DumpCallstack",
                principalColumn: "DumpCallstackId");

            migrationBuilder.AddForeignKey(
                name: "FK_LogFileData_DumpFileInfo_DumpFileInfoId",
                table: "LogFileData",
                column: "DumpFileInfoId",
                principalTable: "DumpFileInfo",
                principalColumn: "DumpFileInfoId");
        }
    }
}
