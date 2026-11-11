using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class DefaultWritingStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportDefaultStyle",
                table: "AppSettings");

            migrationBuilder.AddColumn<int>(
                name: "DefaultWritingStyleId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_DefaultWritingStyleId",
                table: "AppSettings",
                column: "DefaultWritingStyleId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppSettings_WritingStyles_DefaultWritingStyleId",
                table: "AppSettings",
                column: "DefaultWritingStyleId",
                principalTable: "WritingStyles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppSettings_WritingStyles_DefaultWritingStyleId",
                table: "AppSettings");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_DefaultWritingStyleId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultWritingStyleId",
                table: "AppSettings");

            migrationBuilder.AddColumn<string>(
                name: "ReportDefaultStyle",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }
    }
}
