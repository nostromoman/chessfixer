using ChessFixer.Server.Data;

namespace ChessFixer.Server.Services.Development;

/// <summary>
/// The clubs and players a reset database starts with (Beheer, Development): the own club with 23 members (5 of them
/// youth players) and two other clubs with 7 guest players. No tournaments.
/// </summary>
public static class DevelopmentSeed
{
    public static Club OwnClub() => new() { Name = "Schaakvereniging Oisterwijk", City = "Oisterwijk", IsOwnClub = true };

    public static List<Club> OtherClubs() =>
    [
        new() { Name = "Schaakclub Moergestel", City = "Moergestel" },
        new() { Name = "Schaakvereniging Boxtel", City = "Boxtel" }
    ];

    private sealed record Seed(string FirstName, string LastName, int Rating, int BirthYear, string City);

    /// <summary>Youth members of the own club.</summary>
    private static readonly Seed[] Youth =
    [
        new("Daan", "van Dongen", 1150, 2012, "Oisterwijk"),
        new("Sanne", "Verhoeven", 1080, 2013, "Oisterwijk"),
        new("Luuk", "Schellekens", 1320, 2010, "Moergestel"),
        new("Noor", "de Kort", 990, 2014, "Haaren"),
        new("Sem", "Pijnenburg", 1210, 2011, "Oisterwijk")
    ];

    /// <summary>Adult members of the own club.</summary>
    private static readonly Seed[] Adults =
    [
        new("Jan", "van den Berg", 1840, 1958, "Oisterwijk"),
        new("Peter", "Smulders", 1720, 1965, "Oisterwijk"),
        new("Marieke", "van Gils", 1560, 1979, "Oisterwijk"),
        new("Henk", "Brouwers", 1650, 1952, "Moergestel"),
        new("Kees", "de Laat", 1480, 1961, "Oisterwijk"),
        new("Ruud", "Vermeulen", 1910, 1970, "Tilburg"),
        new("Anja", "Kuijpers", 1390, 1984, "Oisterwijk"),
        new("Bart", "van Hal", 1610, 1988, "Haaren"),
        new("Wim", "Panken", 1530, 1949, "Oisterwijk"),
        new("Thijs", "Mutsaers", 1770, 1992, "Tilburg"),
        new("Frank", "Hendriks", 1450, 1975, "Oisterwijk"),
        new("Ellen", "Swinkels", 1340, 1990, "Moergestel"),
        new("Gerard", "Timmermans", 1690, 1956, "Oisterwijk"),
        new("Rob", "van Iersel", 1580, 1968, "Haaren"),
        new("Joost", "Leijten", 2010, 1983, "Oisterwijk"),
        new("Hans", "Bekkers", 1420, 1947, "Oisterwijk"),
        new("Lotte", "de Jong", 1500, 1996, "Tilburg"),
        new("Martijn", "Willems", 1620, 1986, "Oisterwijk")
    ];

    /// <summary>Guest players: the first four play for the first other club, the last three for the second.</summary>
    private static readonly Seed[] Guests =
    [
        new("Erik", "van Rooij", 1760, 1972, "Moergestel"),
        new("Sjoerd", "Maas", 1540, 1981, "Moergestel"),
        new("Ingrid", "van der Heijden", 1470, 1966, "Moergestel"),
        new("Tom", "Raaijmakers", 1680, 1993, "Oisterwijk"),
        new("Paul", "van Esch", 1820, 1963, "Boxtel"),
        new("Femke", "Sterken", 1410, 1989, "Boxtel"),
        new("Niels", "Habraken", 1590, 1977, "Esch")
    ];

    /// <summary>All 30 players: own members first, then the guest players of the two other clubs.</summary>
    public static List<Player> Players(IReadOnlyList<Club> otherClubs) =>
    [
        .. Youth.Concat(Adults).Select(s => ToPlayer(s, null)),
        .. Guests.Select((s, i) => ToPlayer(s, otherClubs[i < 4 ? 0 : 1]))
    ];

    private static Player ToPlayer(Seed seed, Club? guestClub) => new()
    {
        FirstName = seed.FirstName,
        LastName = seed.LastName,
        Rating = seed.Rating,
        // A spread of birthdays, the same on every reset.
        BirthDate = new DateOnly(seed.BirthYear, 1 + seed.Rating % 12, 1 + seed.Rating % 28),
        City = seed.City,
        IsGuest = guestClub is not null,
        Club = guestClub
    };
}
