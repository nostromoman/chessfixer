using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tournaments_Points",
                table: "Tournaments");

            migrationBuilder.AddColumn<double>(
                name: "PointsForAbsence",
                table: "Tournaments",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPresent",
                table: "TournamentPlayers",
                type: "INTEGER",
                nullable: false,
                // Everyone already in a tournament is present.
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tournaments_Points",
                table: "Tournaments",
                sql: "\"PointsForWin\" >= 0 AND \"PointsForDraw\" >= 0 AND \"PointsForLoss\" >= 0 AND \"PointsForBye\" >= 0 AND \"PointsForAbsence\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tournaments_Points",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "PointsForAbsence",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "IsPresent",
                table: "TournamentPlayers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tournaments_Points",
                table: "Tournaments",
                sql: "\"PointsForWin\" >= 0 AND \"PointsForDraw\" >= 0 AND \"PointsForLoss\" >= 0 AND \"PointsForBye\" >= 0");
        }
    }
}
