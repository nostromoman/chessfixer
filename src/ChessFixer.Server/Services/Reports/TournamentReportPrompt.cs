using System.Globalization;
using System.Text;
using ChessFixer.Server.Data;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Services.Reports;

/// <param name="Players">Player details from the central player list, by player id.</param>
/// <param name="FirstRoundStartedAt">UTC; null when no round has been played.</param>
/// <param name="Events">Noteworthy events from the report wizard that are not in the results.</param>
/// <param name="WritingStyle">Instructions for the writing style; null for the default style.</param>
public record TournamentReportData(
    TournamentDto Tournament,
    IReadOnlyList<RoundDto> Rounds,
    IReadOnlyList<StandingDto> Standings,
    IReadOnlyDictionary<int, Player> Players,
    DateTime? FirstRoundStartedAt,
    DateTime? LastRoundStartedAt,
    IReadOnlyList<string>? Events = null,
    string? WritingStyle = null);

/// <summary>The configurable parts of the report prompt (Settings, AI-verslag).</summary>
/// <param name="SystemPrompt">The instructions for the AI; the writing style (if any) is added below it.</param>
/// <param name="UpsetRatingDifference">A win against a player rated at least this much higher is mentioned as an
/// upset (a draw at twice the difference); 0 mentions no upsets.</param>
public record ReportPromptOptions(string SystemPrompt, int UpsetRatingDifference)
{
    public static ReportPromptOptions Default { get; } = new(
        TournamentReportPrompt.DefaultSystemPrompt, TournamentReportPrompt.DefaultUpsetRatingDifference);
}

/// <summary>Builds the (Dutch) prompt that asks the AI for a report of a tournament.</summary>
public static class TournamentReportPrompt
{
    public const int DefaultUpsetRatingDifference = 100;
    public const int MaxUpsetRatingDifference = 1000;
    public const int MaxSystemPromptLength = 10000;

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    public const string DefaultSystemPrompt = """
        Je bent een verslaggever van een schaakclub. Je schrijft in het Nederlands een verslag van een rapidschaaktoernooi.

        Regels:
        - Gebruik alleen de feiten uit de gegevens. Verzin geen zetten, openingen, partijverloop, citaten of andere feiten.
        - Noem de winnaar en de nummers twee en drie, met hun punten.
        - Besteed aandacht aan opvallende uitslagen (bijvoorbeeld een speler die wint van een veel hoger gerateerde tegenstander),
          aan spannende ontknopingen in de laatste rondes en aan spelers die het goed deden.
        - Gebruik de locatie en de omschrijving van het toernooi als die gegeven zijn.
        - Zijn er bijzondere gebeurtenissen gegeven, verwerk die dan op een natuurlijke plek in het verslag. Ze maken het
          verslag persoonlijker, maar blijf bij wat er staat en dik ze niet aan.
        - Schrijf platte tekst zonder Markdown, opsommingstekens of tabellen.
        - Begin met een pakkende titel op de eerste regel. Schrijf daarna de alinea's, gescheiden door een lege regel.
        """;

    /// <summary>The system prompt, with the writing style below it. Without a style, no style is sent.</summary>
    private static string SystemPrompt(string? style, ReportPromptOptions options) =>
        string.IsNullOrWhiteSpace(style)
            ? options.SystemPrompt.Trim()
            : $"""
              {options.SystemPrompt.Trim()}

              Schrijfstijl (volg deze nauwkeurig, ook wat betreft lengte en toon):
              {style.Trim()}
              """;

    public static AiPrompt Build(TournamentReportData data, ReportPromptOptions? options = null)
    {
        options ??= ReportPromptOptions.Default;
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
        text.AppendLine($"Puntentelling: winst {Points(s.PointsForWin)}, remise {Points(s.PointsForDraw)}, verlies {Points(s.PointsForLoss)}, bye {Points(s.PointsForBye)}"
            + (s.PointsForAbsence > 0 ? $", afwezig {Points(s.PointsForAbsence)}" : ""));
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
                if (game.Result == GameResult.Absent)
                {
                    text.AppendLine($"- {game.White.Name} was afwezig");
                    continue;
                }
                if (game.Black is null)
                {
                    text.AppendLine($"- {game.White.Name} had een bye (vrij)");
                    continue;
                }

                text.AppendLine($"- {NameWithRating(game.White, data)} (wit) tegen {NameWithRating(game.Black, data)} (zwart): {Result(game.Result)}");
                if (Upset(game, round.Number, data, options.UpsetRatingDifference) is { } upset) upsets.Add(upset);
            }
        }

        if (upsets.Count > 0)
        {
            text.AppendLine().AppendLine($"OPVALLENDE UITSLAGEN (winst tegen een minstens {options.UpsetRatingDifference} punten hoger gerateerde tegenstander)");
            upsets.ForEach(u => text.AppendLine($"- {u}"));
        }

        if (data.Events is { Count: > 0 } events)
        {
            text.AppendLine().AppendLine("BIJZONDERE GEBEURTENISSEN (geen schaakuitslagen, maar wel het vermelden waard; door de toernooileider opgegeven)");
            foreach (var e in events) text.AppendLine($"- {e.ReplaceLineEndings(" ")}");
        }

        return new AiPrompt(SystemPrompt(data.WritingStyle, options), text.ToString());
    }

    private static string DescribePlayer(StandingDto standing, TournamentReportData data)
    {
        var details = new List<string>();
        if (data.Players.TryGetValue(standing.PlayerId, out var player))
        {
            if (player.Rating is { } rating) details.Add($"rating {rating}");
            if (Age(player.BirthDate, data.FirstRoundStartedAt) is { } age) details.Add($"{age} jaar");
            if (player.IsGuest) details.Add(player.Club is { } club ? $"gastspeler van {club.Name}" : "gastspeler");
            if (!string.IsNullOrWhiteSpace(player.City)) details.Add($"uit {player.City}");
        }
        if (!standing.IsActive) details.Add("tijdens het toernooi gestopt");

        return details.Count == 0 ? standing.Name : $"{standing.Name} ({string.Join(", ", details)})";
    }

    private static string NameWithRating(GamePlayerDto player, TournamentReportData data) =>
        Rating(player, data) is { } rating ? $"{player.Name} ({rating})" : player.Name;

    private static int? Rating(GamePlayerDto player, TournamentReportData data) =>
        data.Players.TryGetValue(player.PlayerId, out var p) ? p.Rating : null;

    private static string? Upset(GameDto game, int round, TournamentReportData data, int threshold)
    {
        if (threshold <= 0) return null;
        if (game.Black is null || Rating(game.White, data) is not { } white || Rating(game.Black, data) is not { } black)
            return null;

        return game.Result switch
        {
            GameResult.WhiteWins when black - white >= threshold =>
                $"Ronde {round}: {game.White.Name} ({white}) won van {game.Black.Name} ({black})",
            GameResult.BlackWins when white - black >= threshold =>
                $"Ronde {round}: {game.Black.Name} ({black}) won van {game.White.Name} ({white})",
            GameResult.Draw when Math.Abs(white - black) >= threshold * 2 =>
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
