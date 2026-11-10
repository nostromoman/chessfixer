using System.Globalization;
using System.Text;
using ChessFixer.Server.Data;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Services.Reports;

/// <param name="Players">Player details from the central player list, by player id.</param>
/// <param name="FirstRoundStartedAt">UTC; null when no round has been played.</param>
public record TournamentReportData(
    TournamentDto Tournament,
    IReadOnlyList<RoundDto> Rounds,
    IReadOnlyList<StandingDto> Standings,
    IReadOnlyDictionary<int, Player> Players,
    DateTime? FirstRoundStartedAt,
    DateTime? LastRoundStartedAt);

/// <summary>Builds the (Dutch) prompt that asks the AI for a report of a tournament.</summary>
public static class TournamentReportPrompt
{
    /// <summary>A win against a player rated at least this much higher is mentioned as an upset.</summary>
    private const int UpsetRatingDifference = 100;

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    private const string SystemPrompt = """
        Je bent een enthousiaste verslaggever van een schaakclub. Je schrijft in het Nederlands een levendig verslag
        van een rapidschaaktoernooi, voor de website of nieuwsbrief van de club.

        Regels:
        - Gebruik alleen de feiten uit de gegevens. Verzin geen zetten, openingen, partijverloop, citaten of andere feiten.
        - Noem de winnaar en de nummers twee en drie, met hun punten.
        - Besteed aandacht aan opvallende uitslagen (bijvoorbeeld een speler die wint van een veel hoger gerateerde tegenstander),
          aan spannende ontknopingen in de laatste rondes en aan spelers die het goed deden.
        - Gebruik de locatie en de omschrijving van het toernooi als die gegeven zijn.
        - Schrijf platte tekst zonder Markdown, opsommingstekens of tabellen.
        - Begin met een pakkende titel op de eerste regel. Schrijf daarna 4 tot 6 alinea's, gescheiden door een lege regel.
        """;

    public static AiPrompt Build(TournamentReportData data)
    {
        var t = data.Tournament;
        var s = t.Settings;
        var text = new StringBuilder();

        text.AppendLine("Schrijf een verslag van dit toernooi.").AppendLine();
        text.AppendLine("TOERNOOI");
        text.AppendLine($"Naam: {t.Title}");
        if (!string.IsNullOrWhiteSpace(t.Location)) text.AppendLine($"Locatie: {t.Location}");
        if (!string.IsNullOrWhiteSpace(t.Description)) text.AppendLine($"Omschrijving: {t.Description}");
        if (FormatDate(data.FirstRoundStartedAt, data.LastRoundStartedAt) is { } date) text.AppendLine($"Datum: {date}");
        text.AppendLine($"Indelingssysteem: {(s.PairingSystem == PairingSystem.Swiss ? "Zwitsers" : "Round robin (iedereen tegen iedereen)")}");
        text.AppendLine($"Tempo: {s.GameTimeMinutes} minuten per speler + {s.IncrementSeconds} seconden per zet");
        text.AppendLine($"Puntentelling: winst {Points(s.PointsForWin)}, remise {Points(s.PointsForDraw)}, verlies {Points(s.PointsForLoss)}, bye {Points(s.PointsForBye)}");
        text.AppendLine($"Aantal gespeelde rondes: {t.RoundsStarted}");
        text.AppendLine($"Aantal spelers: {data.Standings.Count}");

        text.AppendLine().AppendLine("SPELERS");
        foreach (var standing in data.Standings.OrderBy(p => p.Name))
            text.AppendLine($"- {DescribePlayer(standing, data)}");

        text.AppendLine().AppendLine("EINDSTAND (tiebreaks: Buchholz, daarna Sonneborn-Berger)");
        foreach (var row in data.Standings.OrderBy(r => r.Rank))
        {
            text.AppendLine(
                $"{row.Rank}. {row.Name}: {Points(row.Points)} punten uit {row.Played} partijen " +
                $"({row.Wins} winst, {row.Draws} remise, {row.Losses} verlies; " +
                $"Buchholz {Points(row.Buchholz)}, Sonneborn-Berger {Points(row.SonnebornBerger)})");
        }

        text.AppendLine().AppendLine("PARTIJEN");
        var upsets = new List<string>();
        foreach (var round in data.Rounds.Where(r => r.IsStarted).OrderBy(r => r.Number))
        {
            text.AppendLine($"Ronde {round.Number}:");
            foreach (var game in round.Games.OrderBy(g => g.Number))
            {
                if (game.Black is null)
                {
                    text.AppendLine($"- {game.White.Name} had een bye (vrij)");
                    continue;
                }

                text.AppendLine($"- {NameWithRating(game.White, data)} (wit) tegen {NameWithRating(game.Black, data)} (zwart): {Result(game.Result)}");
                if (Upset(game, round.Number, data) is { } upset) upsets.Add(upset);
            }
        }

        if (upsets.Count > 0)
        {
            text.AppendLine().AppendLine($"OPVALLENDE UITSLAGEN (winst tegen een minstens {UpsetRatingDifference} punten hoger gerateerde tegenstander)");
            upsets.ForEach(u => text.AppendLine($"- {u}"));
        }

        return new AiPrompt(SystemPrompt, text.ToString());
    }

    private static string DescribePlayer(StandingDto standing, TournamentReportData data)
    {
        var details = new List<string>();
        if (data.Players.TryGetValue(standing.PlayerId, out var player))
        {
            if (player.Rating is { } rating) details.Add($"rating {rating}");
            if (Age(player.BirthDate, data.FirstRoundStartedAt) is { } age) details.Add($"{age} jaar");
            if (!string.IsNullOrWhiteSpace(player.Club)) details.Add($"club {player.Club}");
            if (!string.IsNullOrWhiteSpace(player.City)) details.Add($"uit {player.City}");
        }
        if (!standing.IsActive) details.Add("tijdens het toernooi gestopt");

        return details.Count == 0 ? standing.Name : $"{standing.Name} ({string.Join(", ", details)})";
    }

    private static string NameWithRating(GamePlayerDto player, TournamentReportData data) =>
        Rating(player, data) is { } rating ? $"{player.Name} ({rating})" : player.Name;

    private static int? Rating(GamePlayerDto player, TournamentReportData data) =>
        data.Players.TryGetValue(player.PlayerId, out var p) ? p.Rating : null;

    private static string? Upset(GameDto game, int round, TournamentReportData data)
    {
        if (game.Black is null || Rating(game.White, data) is not { } white || Rating(game.Black, data) is not { } black)
            return null;

        return game.Result switch
        {
            GameResult.WhiteWins when black - white >= UpsetRatingDifference =>
                $"Ronde {round}: {game.White.Name} ({white}) won van {game.Black.Name} ({black})",
            GameResult.BlackWins when white - black >= UpsetRatingDifference =>
                $"Ronde {round}: {game.Black.Name} ({black}) won van {game.White.Name} ({white})",
            GameResult.Draw when Math.Abs(white - black) >= UpsetRatingDifference * 2 =>
                $"Ronde {round}: {game.White.Name} ({white}) speelde remise tegen {game.Black.Name} ({black})",
            _ => null
        };
    }

    private static string Result(GameResult result) => result switch
    {
        GameResult.WhiteWins => "1-0, wit wint",
        GameResult.BlackWins => "0-1, zwart wint",
        GameResult.Draw => "½-½, remise",
        GameResult.WhiteWinsByForfeit => "1-0, wit wint reglementair (niet gespeeld)",
        GameResult.BlackWinsByForfeit => "0-1, zwart wint reglementair (niet gespeeld)",
        _ => "geen uitslag"
    };

    private static string? FormatDate(DateTime? first, DateTime? last)
    {
        if (first is null) return null;
        var from = first.Value.ToLocalTime();
        var to = (last ?? first).Value.ToLocalTime();
        return from.Date == to.Date
            ? from.ToString("dddd d MMMM yyyy", Dutch)
            : $"{from.ToString("d MMMM yyyy", Dutch)} tot en met {to.ToString("d MMMM yyyy", Dutch)}";
    }

    private static int? Age(DateOnly? birthDate, DateTime? at)
    {
        if (birthDate is not { } born) return null;
        var today = DateOnly.FromDateTime((at ?? DateTime.UtcNow).ToLocalTime());
        var age = today.Year - born.Year;
        if (born > today.AddYears(-age)) age--;
        return age is >= 0 and < 120 ? age : null;
    }

    private static string Points(double value) => value.ToString("0.##", Dutch);
}
