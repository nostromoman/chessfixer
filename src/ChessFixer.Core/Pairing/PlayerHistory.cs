using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Pairing;

/// <summary>What happened to one player in earlier rounds, as far as pairing is concerned.</summary>
internal sealed class PlayerHistory
{
    public double Score { get; private set; }
    public HashSet<int> Opponents { get; } = [];
    public List<PieceColor> Colors { get; } = [];
    public bool HadBye { get; private set; }
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

            if (game.BlackId is null)
            {
                if (white is not null) white.HadBye = true;
                continue;
            }

            if (game.Result is GameResult.WhiteWinsByForfeit or GameResult.BlackWinsByForfeit)
                continue;

            if (white is not null)
            {
                white.Opponents.Add(game.BlackId.Value);
                white.Colors.Add(PieceColor.White);
            }
            if (black is not null)
            {
                black.Opponents.Add(game.WhiteId);
                black.Colors.Add(PieceColor.Black);
            }
        }

        return histories;
    }
}
