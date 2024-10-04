using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrashDumpAnalyzer.Data.Migrations
{
    /// <inheritdoc />
    public partial class _006 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // change all absolute DumpFileInfo.FilePath to relative paths in the db
            var builder = WebApplication.CreateBuilder();
            var dumpPath = builder.Configuration.GetValue<string>("DumpPath");
            dumpPath.TrimEnd('\\');
            dumpPath.TrimEnd('/');
            migrationBuilder.Sql(
                $"UPDATE DumpFileInfo SET FilePath = SUBSTRING(FilePath, {dumpPath.Length+2})"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
