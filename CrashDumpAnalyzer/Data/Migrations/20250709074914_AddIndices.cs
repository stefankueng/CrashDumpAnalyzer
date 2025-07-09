using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LogFileData_LatestTime",
                table: "LogFileData",
                column: "LatestTime");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_ApplicationName",
                table: "DumpFileInfo",
                column: "ApplicationName");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_ComputerName",
                table: "DumpFileInfo",
                column: "ComputerName");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_Domain",
                table: "DumpFileInfo",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_Environment",
                table: "DumpFileInfo",
                column: "Environment");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_FilePath",
                table: "DumpFileInfo",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_UploadDate",
                table: "DumpFileInfo",
                column: "UploadDate");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_UploadDate_DumpCallstackId",
                table: "DumpFileInfo",
                columns: new[] { "UploadDate", "DumpCallstackId" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_UploadedFromHostname",
                table: "DumpFileInfo",
                column: "UploadedFromHostname");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_UploadedFromIp",
                table: "DumpFileInfo",
                column: "UploadedFromIp");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_UploadedFromUserEmail",
                table: "DumpFileInfo",
                column: "UploadedFromUserEmail");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_UploadedFromUsername",
                table: "DumpFileInfo",
                column: "UploadedFromUsername");

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_ApplicationName",
                table: "DumpCallstack",
                column: "ApplicationName");

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_ApplicationName_Deleted",
                table: "DumpCallstack",
                columns: new[] { "ApplicationName", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_ApplicationName_ExceptionType",
                table: "DumpCallstack",
                columns: new[] { "ApplicationName", "ExceptionType" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_ApplicationVersion_BuildType",
                table: "DumpCallstack",
                columns: new[] { "ApplicationVersion", "BuildType" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_CleanCallstack",
                table: "DumpCallstack",
                column: "CleanCallstack");

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_Deleted",
                table: "DumpCallstack",
                column: "Deleted");

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_Deleted_FixedVersion_LinkedToDumpCallstackId",
                table: "DumpCallstack",
                columns: new[] { "Deleted", "FixedVersion", "LinkedToDumpCallstackId" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_Deleted_LinkedToDumpCallstackId",
                table: "DumpCallstack",
                columns: new[] { "Deleted", "LinkedToDumpCallstackId" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_ExceptionType",
                table: "DumpCallstack",
                column: "ExceptionType");

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_ExceptionType_Deleted",
                table: "DumpCallstack",
                columns: new[] { "ExceptionType", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_FixedVersion_Deleted",
                table: "DumpCallstack",
                columns: new[] { "FixedVersion", "Deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_FixedVersion_FixedBuildType",
                table: "DumpCallstack",
                columns: new[] { "FixedVersion", "FixedBuildType" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_LinkedToDumpCallstackId",
                table: "DumpCallstack",
                column: "LinkedToDumpCallstackId");

            migrationBuilder.CreateIndex(
                name: "IX_DumpCallstack_Ticket",
                table: "DumpCallstack",
                column: "Ticket");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogFileData_LatestTime",
                table: "LogFileData");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_ApplicationName",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_ComputerName",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_Domain",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_Environment",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_FilePath",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_UploadDate",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_UploadDate_DumpCallstackId",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_UploadedFromHostname",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_UploadedFromIp",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_UploadedFromUserEmail",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_UploadedFromUsername",
                table: "DumpFileInfo");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_ApplicationName",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_ApplicationName_Deleted",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_ApplicationName_ExceptionType",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_ApplicationVersion_BuildType",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_CleanCallstack",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_Deleted",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_Deleted_FixedVersion_LinkedToDumpCallstackId",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_Deleted_LinkedToDumpCallstackId",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_ExceptionType",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_ExceptionType_Deleted",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_FixedVersion_Deleted",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_FixedVersion_FixedBuildType",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_LinkedToDumpCallstackId",
                table: "DumpCallstack");

            migrationBuilder.DropIndex(
                name: "IX_DumpCallstack_Ticket",
                table: "DumpCallstack");
        }
    }
}
