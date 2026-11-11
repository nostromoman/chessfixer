using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportPromptSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportDefaultStyle",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportSystemPrompt",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportUpsetRatingDifference",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportDefaultStyle",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ReportSystemPrompt",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ReportUpsetRatingDifference",
                table: "AppSettings");
        }
    }
}
