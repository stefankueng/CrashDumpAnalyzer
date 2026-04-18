using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class DumpTimeOffset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "DumpTimeOffset",
                table: "DumpFileInfo",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DumpTimeOffset",
                table: "DumpFileInfo");
        }
    }
}
