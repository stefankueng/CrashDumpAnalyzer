using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixedVersionSetAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FixedVersionSetAt",
                table: "DumpCallstack",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"DumpCallstack\" SET \"FixedVersionSetAt\" = CURRENT_TIMESTAMP WHERE \"FixedVersion\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FixedVersionSetAt",
                table: "DumpCallstack");
        }
    }
}
