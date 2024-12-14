using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class _007 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VersionResource",
                table: "DumpFileInfo",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "BuildType",
                table: "DumpCallstack",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.AddColumn<int>(
                name: "FixedBuildType",
                table: "DumpCallstack",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VersionResource",
                table: "DumpFileInfo");

            migrationBuilder.DropColumn(
                name: "BuildType",
                table: "DumpCallstack");

            migrationBuilder.DropColumn(
                name: "FixedBuildType",
                table: "DumpCallstack");
        }
    }
}
