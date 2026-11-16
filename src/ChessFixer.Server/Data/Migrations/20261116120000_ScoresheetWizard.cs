using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScoresheetWizard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReadError",
                table: "GameScoresheets",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadModel",
                table: "GameScoresheets",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadRemarks",
                table: "GameScoresheets",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadStartedAt",
                table: "GameScoresheets",
                type: "TEXT",
                nullable: true);

            // Existing photos were kept only (or read with the old dialog): not read in a wizard.
            migrationBuilder.AddColumn<string>(
                name: "ReadStatus",
                table: "GameScoresheets",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "Review",
                table: "GameScoresheets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewOpen",
                table: "GameScoresheets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReadError", table: "GameScoresheets");
            migrationBuilder.DropColumn(name: "ReadModel", table: "GameScoresheets");
            migrationBuilder.DropColumn(name: "ReadRemarks", table: "GameScoresheets");
            migrationBuilder.DropColumn(name: "ReadStartedAt", table: "GameScoresheets");
            migrationBuilder.DropColumn(name: "ReadStatus", table: "GameScoresheets");
            migrationBuilder.DropColumn(name: "Review", table: "GameScoresheets");
            migrationBuilder.DropColumn(name: "ReviewOpen", table: "GameScoresheets");
        }
    }
}
