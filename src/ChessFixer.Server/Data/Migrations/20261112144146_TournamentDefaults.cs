using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>The existing settings row gets the values new tournaments started with until now.</remarks>
    public partial class TournamentDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultBoards",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<int>(
                name: "DefaultGameTimeMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "DefaultIncrementSeconds",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultIsRated",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<double>(
                name: "DefaultPointsForAbsence",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultPointsForBye",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultPointsForDraw",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.5);

            migrationBuilder.AddColumn<double>(
                name: "DefaultPointsForLoss",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultPointsForWin",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultBoards",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultGameTimeMinutes",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultIncrementSeconds",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultIsRated",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPointsForAbsence",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPointsForBye",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPointsForDraw",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPointsForLoss",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPointsForWin",
                table: "AppSettings");
        }
    }
}
