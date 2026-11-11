using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClubsGuestsRatingLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "Players",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsOwnClub = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                });

            // Keep the club names that were typed on players: each distinct name becomes a club, linked to its players.
            migrationBuilder.Sql("""
                INSERT INTO "Clubs" ("Name", "IsOwnClub")
                SELECT DISTINCT trim("Club"), 0 FROM "Players" WHERE "Club" IS NOT NULL AND trim("Club") <> '';
                UPDATE "Players"
                SET "ClubId" = (SELECT c."Id" FROM "Clubs" c WHERE c."Name" = trim("Players"."Club"))
                WHERE "Club" IS NOT NULL AND trim("Club") <> '';
                """);

            migrationBuilder.DropColumn(
                name: "Club",
                table: "Players");

            migrationBuilder.CreateTable(
                name: "RatingLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    RatingBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    RatingAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: true),
                    TournamentTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Round = table.Column<int>(type: "INTEGER", nullable: true),
                    OpponentId = table.Column<int>(type: "INTEGER", nullable: true),
                    OpponentRating = table.Column<int>(type: "INTEGER", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    IsWhite = table.Column<bool>(type: "INTEGER", nullable: true),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatingLog_Players_OpponentId",
                        column: x => x.OpponentId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RatingLog_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RatingLog_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_ClubId",
                table: "Players",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_Name",
                table: "Clubs",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_OwnClub",
                table: "Clubs",
                column: "IsOwnClub",
                unique: true,
                filter: "\"IsOwnClub\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RatingLog_At",
                table: "RatingLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_RatingLog_OpponentId",
                table: "RatingLog",
                column: "OpponentId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingLog_PlayerId_At",
                table: "RatingLog",
                columns: new[] { "PlayerId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_RatingLog_TournamentId",
                table: "RatingLog",
                column: "TournamentId");

            // The rating log starts with the rating changes of the games rated so far.
            migrationBuilder.Sql("""
                INSERT INTO "RatingLog" ("At", "PlayerId", "RatingBefore", "RatingAfter", "Reason", "TournamentId",
                    "TournamentTitle", "Round", "OpponentId", "OpponentRating", "Result", "IsWhite")
                SELECT "At", "PlayerId", "Before", "Before" + "Change", 'Game', "TournamentId", "Title", "Number",
                    "OpponentId", "OpponentRating", "Result", "IsWhite"
                FROM (
                    SELECT g."RatedAt" AS "At", g."Id" AS "GameId", w."PlayerId", g."WhiteRatingBefore" AS "Before",
                        g."WhiteRatingChange" AS "Change", r."TournamentId", t."Title", r."Number", b."PlayerId" AS "OpponentId",
                        g."BlackRatingBefore" AS "OpponentRating", g."Result", 1 AS "IsWhite"
                    FROM "Games" g
                    JOIN "Rounds" r ON r."Id" = g."RoundId"
                    JOIN "Tournaments" t ON t."Id" = r."TournamentId"
                    JOIN "TournamentPlayers" w ON w."Id" = g."WhiteId"
                    JOIN "TournamentPlayers" b ON b."Id" = g."BlackId"
                    WHERE g."RatedAt" IS NOT NULL
                    UNION ALL
                    SELECT g."RatedAt", g."Id", b."PlayerId", g."BlackRatingBefore", g."BlackRatingChange", r."TournamentId",
                        t."Title", r."Number", w."PlayerId", g."WhiteRatingBefore", g."Result", 0
                    FROM "Games" g
                    JOIN "Rounds" r ON r."Id" = g."RoundId"
                    JOIN "Tournaments" t ON t."Id" = r."TournamentId"
                    JOIN "TournamentPlayers" w ON w."Id" = g."WhiteId"
                    JOIN "TournamentPlayers" b ON b."Id" = g."BlackId"
                    WHERE g."RatedAt" IS NOT NULL
                )
                ORDER BY "At", "GameId", "IsWhite" DESC;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Clubs_ClubId",
                table: "Players",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Club",
                table: "Players",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Players" SET "Club" = (SELECT c."Name" FROM "Clubs" c WHERE c."Id" = "Players"."ClubId")
                WHERE "ClubId" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Players_Clubs_ClubId",
                table: "Players");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "RatingLog");

            migrationBuilder.DropIndex(
                name: "IX_Players_ClubId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "IsGuest",
                table: "Players");

        }
    }
}
