using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>Stockfish analysis per game; games that already have moves get a pending analysis.</remarks>
    public partial class GameAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Pgn = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PositionsDone = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionsTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    Positions = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameAnalyses_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalyses_GameId",
                table: "GameAnalyses",
                column: "GameId",
                unique: true);

            // Games that already have moves are analysed after the update (the worker picks up pending analyses).
            migrationBuilder.Sql(
                "INSERT INTO \"GameAnalyses\" (\"GameId\", \"Status\", \"Pgn\", \"Depth\", \"PositionsDone\", \"PositionsTotal\", \"RequestedAt\") " +
                "SELECT \"Id\", 'Pending', \"Pgn\", 16, 0, 0, strftime('%Y-%m-%d %H:%M:%S', 'now') FROM \"Games\" WHERE \"Pgn\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameAnalyses");
        }
    }
}
