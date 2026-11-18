using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class IgnoreDuplicateDumpFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Checksum",
                table: "DumpFileInfo",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DumpFileInfo_Checksum",
                table: "DumpFileInfo",
                column: "Checksum",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DumpFileInfo_Checksum",
                table: "DumpFileInfo");

            migrationBuilder.DropColumn(
                name: "Checksum",
                table: "DumpFileInfo");
        }
    }
}
