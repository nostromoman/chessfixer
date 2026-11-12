using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>Written by hand (App Control blocked dotnet ef locally): an absence, like a bye, is a game without Black.</remarks>
    public partial class AbsenceInByeConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games",
                sql: "(\"BlackId\" IS NULL) = (\"Result\" IN ('Bye', 'Absent')) AND (\"Result\" NOT IN ('Bye', 'Absent') OR \"Board\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Games_Bye",
                table: "Games",
                sql: "(\"BlackId\" IS NULL) = (\"Result\" = 'Bye') AND (\"Result\" <> 'Bye' OR \"Board\" IS NULL)");
        }
    }
}
