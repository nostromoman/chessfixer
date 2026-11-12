using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RevertOfId",
                table: "ActivityLog",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivityChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntryId = table.Column<long>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Properties = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityChanges_ActivityLog_EntryId",
                        column: x => x.EntryId,
                        principalTable: "ActivityLog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_RevertOfId",
                table: "ActivityLog",
                column: "RevertOfId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityChanges_EntryId",
                table: "ActivityChanges",
                column: "EntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityChanges");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLog_RevertOfId",
                table: "ActivityLog");

            migrationBuilder.DropColumn(
                name: "RevertOfId",
                table: "ActivityLog");
        }
    }
}
