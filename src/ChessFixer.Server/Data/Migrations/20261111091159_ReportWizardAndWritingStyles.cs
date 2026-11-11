using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ChessFixer.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportWizardAndWritingStyles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Events",
                table: "TournamentReports",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "WritingStyleDescription",
                table: "TournamentReports",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingStyleId",
                table: "TournamentReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WritingStyleName",
                table: "TournamentReports",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WritingStyles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingStyles", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "WritingStyles",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 1, "Schrijf als een enthousiaste verslaggever van de schaakclub, levendig en met oog voor de spannende momenten, geschikt voor de website of nieuwsbrief van de club. Schrijf 4 tot 6 alinea's.", "Clubverslag" },
                    { 2, "Schrijf zodat het geschikt is om te plaatsen in een nieuwsbrief van een basisschool, voor kinderen van groep 6 en hoger. Gebruik korte zinnen en eenvoudige woorden, leg schaaktermen kort uit en houd het vrolijk en positief.", "Voor basisschool kind niveau" },
                    { 3, "Schrijf als een nieuwsbericht van de NOS: zakelijk, feitelijk en neutraal. Begin met de belangrijkste uitkomst (wie heeft gewonnen), daarna de details. Korte alinea's, geen overdreven bijvoeglijke naamwoorden.", "NOS nieuws" },
                    { 4, "Schrijf kort en bondig: één of twee korte alinea's met alleen de winnaar, de top drie en het meest opvallende moment.", "Kort en bondig" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WritingStyles");

            migrationBuilder.DropColumn(
                name: "Events",
                table: "TournamentReports");

            migrationBuilder.DropColumn(
                name: "WritingStyleDescription",
                table: "TournamentReports");

            migrationBuilder.DropColumn(
                name: "WritingStyleId",
                table: "TournamentReports");

            migrationBuilder.DropColumn(
                name: "WritingStyleName",
                table: "TournamentReports");
        }
    }
}
