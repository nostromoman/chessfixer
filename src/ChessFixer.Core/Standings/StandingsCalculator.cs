using ChessFixer.Core.Pairing;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Standings;

/// <summary>One line of the standings. Players with equal points and tie-breaks share a rank.</summary>
public record StandingRow(
    int Rank,
    int PlayerId,
    double Points,
    double Buchholz,
    double SonnebornBerger,
    int Played,
    int Wins,
    int Draws,
    int Losses);

/// <summary>
/// Calculates the standings: points, then Buchholz, then Sonneborn-Berger (Swiss); or points, then Sonneborn-Berger,
/// then the number of wins (competition: when everyone plays everyone, Buchholz says little).
/// <list type="bullet">
/// <item>Games without a result yet are ignored, so the standings can be shown during a round.</item>
/// <item>An absence gives the points for absence but does not count as a game (played, won, drawn or lost). A round
/// without opponent (competition) is the same, without points.</item>
/// <item><b>Buchholz</b>: sum of the opponents' points. A bye or forfeit counts as a game against a
/// virtual opponent with the player's own points (FIDE approach), so a bye does not hurt the tie-break.</item>
/// <item><b>Sonneborn-Berger</b>: sum of the points of beaten opponents plus half the points of opponents
/// drawn against. Only games actually played on the board count.</item>
/// </list>
/// </summary>
public static class StandingsCalculator
{
    private const int Precision = 6;

    /// <param name="playerIds">Everyone who should appear, including removed players.</param>
    /// <param name="games">All games of the tournament.</param>
    /// <param name="scoring">Points per result.</param>
    /// <param name="system">Decides the tie-breaks.</param>
    public static IReadOnlyList<StandingRow> Calculate(
        IEnumerable<int> playerIds, IEnumerable<PlayedGame> games, ScoringRules scoring,
        PairingSystem system = PairingSystem.Swiss)
    {
        var stats = playerIds.Distinct().ToDictionary(id => id, _ => new PlayerStats());
        var finished = games.Where(g => g.Result != GameResult.NotPlayed).ToList();

        foreach (var game in finished)
        {
            var (whitePoints, blackPoints) = scoring.PointsFor(game.Result);
            Add(stats, game.WhiteId, whitePoints, game.BlackId, Fraction(game.Result, white: true), game.Result);
            if (game.BlackId is { } blackId)
                Add(stats, blackId, blackPoints, game.WhiteId, Fraction(game.Result, white: false), game.Result);
        }

        var points = stats.ToDictionary(s => s.Key, s => s.Value.Points);
        double PointsOf(int id) => points.GetValueOrDefault(id);

        var rows = stats
            .Select(s =>
            {
                var player = s.Value;
                var buchholz = player.Opponents.Sum(o => PointsOf(o.OpponentId))
                               + player.UnplayedGames * player.Points;
                var sonnebornBerger = player.Opponents.Sum(o => PointsOf(o.OpponentId) * o.Fraction);
                return new StandingRow(
                    Rank: 0,
                    PlayerId: s.Key,
                    Points: Math.Round(player.Points, Precision),
                    Buchholz: Math.Round(buchholz, Precision),
                    SonnebornBerger: Math.Round(sonnebornBerger, Precision),
                    Played: player.Played,
                    Wins: player.Wins,
                    Draws: player.Draws,
                    Losses: player.Losses);
            })
            .ToList();

        // The values that decide the order, most important first.
        Func<StandingRow, double>[] tieBreaks = system == PairingSystem.Competition
            ? [r => r.Points, r => r.SonnebornBerger, r => r.Wins]
            : [r => r.Points, r => r.Buchholz, r => r.SonnebornBerger];
        var ordered = rows.OrderByDescending(tieBreaks[0]);
        foreach (var tieBreak in tieBreaks.Skip(1)) ordered = ordered.ThenByDescending(tieBreak);
        rows = ordered.ThenBy(r => r.PlayerId).ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            var tiedWithPrevious = i > 0 && tieBreaks.All(value => value(rows[i]) == value(rows[i - 1]));
            rows[i] = rows[i] with { Rank = tiedWithPrevious ? rows[i - 1].Rank : i + 1 };
        }

        return rows;
    }

    private static void Add(
        Dictionary<int, PlayerStats> stats, int playerId, double points, int? opponentId, double fraction, GameResult result)
    {
        if (!stats.TryGetValue(playerId, out var player)) return;

        player.Points += points;
        // Like a bye, an absence counts as an unplayed game for Buchholz (virtual opponent with the own points).
        if (result is GameResult.Absent or GameResult.NoOpponent)
        {
            player.UnplayedGames++;
            return;
        }

        player.Played++;
        if (fraction == 1) player.Wins++;
        else if (fraction == 0.5) player.Draws++;
        else player.Losses++;

        var unplayed = opponentId is null
            || result is GameResult.WhiteWinsByForfeit or GameResult.BlackWinsByForfeit;
        if (unplayed)
            player.UnplayedGames++;
        else
            player.Opponents.Add((opponentId!.Value, fraction));
    }

    /// <summary>1 for a win, ½ for a draw, 0 for a loss, from the given side's point of view. A bye counts as a win.</summary>
    private static double Fraction(GameResult result, bool white) => result switch
    {
        GameResult.WhiteWins or GameResult.WhiteWinsByForfeit => white ? 1 : 0,
        GameResult.BlackWins or GameResult.BlackWinsByForfeit => white ? 0 : 1,
        GameResult.Draw => 0.5,
        GameResult.Bye => 1,
        _ => 0
    };

    private sealed class PlayerStats
    {
        public double Points { get; set; }
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int UnplayedGames { get; set; }
        public List<(int OpponentId, double Fraction)> Opponents { get; } = [];
    }
}
