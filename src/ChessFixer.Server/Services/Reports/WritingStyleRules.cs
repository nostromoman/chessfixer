using ChessFixer.Server.Data;

namespace ChessFixer.Server.Services.Reports;

/// <summary>Limits and the default writing styles of the AI report.</summary>
public static class WritingStyleRules
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 2000;

    /// <summary>At most this many noteworthy events per report, each at most <see cref="MaxEventLength"/> characters.</summary>
    public const int MaxEvents = 50;
    public const int MaxEventLength = 500;

    /// <summary>Seeded once (by the migration); afterwards the leader manages them in the settings.</summary>
    public static readonly WritingStyle[] Defaults =
    [
        new()
        {
            Id = 1,
            Name = "Clubverslag",
            Description = "Schrijf als een enthousiaste verslaggever van de schaakclub, levendig en met oog voor de spannende momenten, " +
                          "geschikt voor de website of nieuwsbrief van de club. Schrijf 4 tot 6 alinea's."
        },
        new()
        {
            Id = 2,
            Name = "Voor basisschool kind niveau",
            Description = "Schrijf zodat het geschikt is om te plaatsen in een nieuwsbrief van een basisschool, voor kinderen van groep 6 en hoger. " +
                          "Gebruik korte zinnen en eenvoudige woorden, leg schaaktermen kort uit en houd het vrolijk en positief."
        },
        new()
        {
            Id = 3,
            Name = "NOS nieuws",
            Description = "Schrijf als een nieuwsbericht van de NOS: zakelijk, feitelijk en neutraal. Begin met de belangrijkste uitkomst " +
                          "(wie heeft gewonnen), daarna de details. Korte alinea's, geen overdreven bijvoeglijke naamwoorden."
        },
        new()
        {
            Id = 4,
            Name = "Kort en bondig",
            Description = "Schrijf kort en bondig: één of twee korte alinea's met alleen de winnaar, de top drie en het meest opvallende moment."
        }
    ];
}
