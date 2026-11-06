using ChessFixer.Core.Pairing;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Core.Tests.Pairing;

/// <summary>Plays rounds with a pairing strategy so tests can check properties over a whole tournament.</summary>
internal sealed class TournamentSimulator(IPairingStrategy strategy, IEnumerable<PairingPlayer> players)
{
    public List<PairingPlayer> Players { get; } = players.ToList();
    public List<PlayedGame> History { get; } = [];
    public int RoundsPlayed { get; private set; }

    /// <summary>Players 1..count; a lower id means a higher rating.</summary>
    public static List<PairingPlayer> CreatePlayers(int count) =>
        Enumerable.Range(1, count).Select(i => new PairingPlayer(i, 2000 - i * 10)).ToList();

    public IReadOnlyList<BoardPairing> PairNext() =>
        strategy.PairNextRound(new PairingInput(Players, History, ScoringRules.Default));

    /// <summary>Pairs and plays one round. By default the higher-rated player (lower id) wins.</summary>
    public IReadOnlyList<BoardPairing> PlayRound(Func<BoardPairing, GameResult>? result = null)
    {
        var pairings = PairNext();
        RoundsPlayed++;
        foreach (var p in pairings)
        {
            var outcome = p.IsBye
                ? GameResult.Bye
                : result?.Invoke(p) ?? (p.WhiteId < p.BlackId ? GameResult.WhiteWins : GameResult.BlackWins);
            History.Add(new PlayedGame(RoundsPlayed, p.WhiteId, p.BlackId, outcome));
        }
        return pairings;
    }

    public IEnumerable<(int A, int B)> Meetings() =>
        History.Where(g => g.BlackId is not null)
            .Select(g => (Math.Min(g.WhiteId, g.BlackId!.Value), Math.Max(g.WhiteId, g.BlackId!.Value)));

    /// <summary>Colors of a player in round order: 'W', 'B', or '-' for a bye or a round not played.</summary>
    public string ColorsOf(int playerId) =>
        string.Concat(Enumerable.Range(1, RoundsPlayed).Select(round =>
        {
            var game = History.FirstOrDefault(g => g.Round == round && (g.WhiteId == playerId || g.BlackId == playerId));
            return game is null || game.BlackId is null ? '-' : game.WhiteId == playerId ? 'W' : 'B';
        }));
}
