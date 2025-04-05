using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class _011 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadedFromUserEmail",
                table: "DumpFileInfo",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UploadedFromUsername",
                table: "DumpFileInfo",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadedFromUserEmail",
                table: "DumpFileInfo");

            migrationBuilder.DropColumn(
                name: "UploadedFromUsername",
                table: "DumpFileInfo");
        }
    }
}
