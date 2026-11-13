using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>Written by hand (App Control blocked dotnet ef locally): the competition replaces round robin
    /// (times against each other, catch-up games, rounds without opponent). Round robin tournaments become
    /// competitions in which everyone plays everyone once.</remarks>
    public partial class Competition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimesAgainstEachOther",
                table: "Tournaments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<bool>(
                name: "IsCatchUp",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlayedAt",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"Tournaments\" SET \"PairingSystem\" = 'Competition', \"TimesAgainstEachOther\" = 1 " +
                "WHERE \"PairingSystem\" = 'RoundRobin';");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games",
                sql: "(\"BlackId\" IS NULL) = (\"Result\" IN ('Bye', 'Absent', 'NoOpponent')) AND (\"Result\" NOT IN ('Bye', 'Absent', 'NoOpponent') OR \"Board\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Games_CatchUp",
                table: "Games",
                sql: "\"IsCatchUp\" = 0 OR (\"BlackId\" IS NOT NULL AND \"Board\" IS NULL AND \"Result\" <> 'NotPlayed' AND \"PlayedAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tournaments_TimesAgainstEachOther",
                table: "Tournaments",
                sql: "\"TimesAgainstEachOther\" BETWEEN 1 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tournaments_TimesAgainstEachOther",
                table: "Tournaments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Games_CatchUp",
                table: "Games");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games");

            migrationBuilder.Sql("DELETE FROM \"Games\" WHERE \"IsCatchUp\" = 1 OR \"Result\" = 'NoOpponent';");
            migrationBuilder.Sql(
                "UPDATE \"Tournaments\" SET \"PairingSystem\" = 'RoundRobin' WHERE \"PairingSystem\" = 'Competition';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games",
                sql: "(\"BlackId\" IS NULL) = (\"Result\" IN ('Bye', 'Absent')) AND (\"Result\" NOT IN ('Bye', 'Absent') OR \"Board\" IS NULL)");

            migrationBuilder.DropColumn(
                name: "TimesAgainstEachOther",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "IsCatchUp",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PlayedAt",
                table: "Games");
        }
    }
}
