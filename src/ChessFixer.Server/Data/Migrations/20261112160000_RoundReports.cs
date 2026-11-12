using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>Written by hand (App Control blocked dotnet ef locally): the AI-written report of one round.</remarks>
    public partial class RoundReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoundReports",
                columns: table => new
                {
                    RoundId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    SystemPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    UserPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    Events = table.Column<string>(type: "TEXT", nullable: false),
                    WritingStyleId = table.Column<int>(type: "INTEGER", nullable: true),
                    WritingStyleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    WritingStyleDescription = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundReports", x => x.RoundId);
                    table.ForeignKey(
                        name: "FK_RoundReports_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "Rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoundReports");
        }
    }
}
