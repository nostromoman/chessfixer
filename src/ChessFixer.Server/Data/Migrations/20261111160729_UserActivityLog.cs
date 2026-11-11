using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_At",
                table: "ActivityLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_UserId_At",
                table: "ActivityLog",
                columns: new[] { "UserId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLog");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Users");
        }
    }
}
