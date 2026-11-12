using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Pairing;

/// <summary>What happened to one player in earlier rounds, as far as pairing is concerned.</summary>
internal sealed class PlayerHistory
{
    public double Score { get; private set; }
    public HashSet<int> Opponents { get; } = [];
    /// <summary>How often this player met each opponent (forfeits excluded).</summary>
    public Dictionary<int, int> TimesMet { get; } = [];
    /// <summary>Per opponent: games with White minus games with Black against that opponent.</summary>
    public Dictionary<int, int> WhiteBalanceAgainst { get; } = [];

    /// <summary>
    /// The color this player should get against <paramref name="opponentId"/> so that they alternate colors
    /// against each other: Black after having had White more often against them, and vice versa. Null when even.
    /// </summary>
    public PieceColor? RematchColorAgainst(int opponentId) =>
        WhiteBalanceAgainst.GetValueOrDefault(opponentId) switch
        {
            > 0 => PieceColor.Black,
            < 0 => PieceColor.White,
            _ => null
        };
    public List<PieceColor> Colors { get; } = [];
    public int ByeCount { get; private set; }
    public ColorPreference ColorPreference => ColorAllocator.PreferenceOf(Colors);

    /// <summary>
    /// Builds the history of every active player. Following FIDE practice, forfeited games
    /// do not count as "played": the players may meet again and the colors do not count.
    /// </summary>
    public static Dictionary<int, PlayerHistory> Build(PairingInput input)
    {
        var histories = input.Players.ToDictionary(p => p.Id, _ => new PlayerHistory());

        foreach (var game in input.History.OrderBy(g => g.Round))
        {
            var white = histories.GetValueOrDefault(game.WhiteId);
            var black = game.BlackId is { } blackId ? histories.GetValueOrDefault(blackId) : null;
            var (whitePoints, blackPoints) = input.Scoring.PointsFor(game.Result);

            if (white is not null) white.Score += whitePoints;
            if (black is not null) black.Score += blackPoints;

            // An absence is not a bye: the player can still get a bye later.
            if (game.Result == GameResult.Absent) continue;

            if (game.BlackId is null)
            {
                if (white is not null) white.ByeCount++;
                continue;
            }

            if (game.Result is GameResult.WhiteWinsByForfeit or GameResult.BlackWinsByForfeit)
                continue;

            if (white is not null)
            {
                white.Opponents.Add(game.BlackId.Value);
                white.TimesMet[game.BlackId.Value] = white.TimesMet.GetValueOrDefault(game.BlackId.Value) + 1;
                white.WhiteBalanceAgainst[game.BlackId.Value] = white.WhiteBalanceAgainst.GetValueOrDefault(game.BlackId.Value) + 1;
                white.Colors.Add(PieceColor.White);
            }
            if (black is not null)
            {
                black.Opponents.Add(game.WhiteId);
                black.TimesMet[game.WhiteId] = black.TimesMet.GetValueOrDefault(game.WhiteId) + 1;
                black.WhiteBalanceAgainst[game.WhiteId] = black.WhiteBalanceAgainst.GetValueOrDefault(game.WhiteId) - 1;
                black.Colors.Add(PieceColor.Black);
            }
        }

        return histories;
    }
}
