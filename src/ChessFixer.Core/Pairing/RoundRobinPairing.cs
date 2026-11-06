namespace ChessFixer.Core.Pairing;

/// <summary>
/// Round robin: everyone plays everyone once. The schedule (who meets whom in which round) follows
/// the circle method, seeded by registration order. Colors are decided per round from each player's
/// color history, so they alternate as much as possible.
/// Players cannot change after round 1; the server enforces that.
/// </summary>
public sealed class RoundRobinPairing : IPairingStrategy
{
    /// <summary>Number of rounds needed for everyone to play everyone (with an odd count, each player gets one bye).</summary>
    public static int TotalRounds(int playerCount) =>
        playerCount < 2 ? 0 : playerCount % 2 == 0 ? playerCount - 1 : playerCount;

    public IReadOnlyList<BoardPairing> PairNextRound(PairingInput input)
    {
        if (input.Players.Count < 2) throw new PairingException(PairingError.NotEnoughPlayers);

        // Seed by registration order; null is the "bye" slot when the number of players is odd.
        var slots = input.Players.Select(p => p.Id).Order().Cast<int?>().ToList();
        if (slots.Count % 2 == 1) slots.Add(null);

        var n = slots.Count;
        var round = input.History.Select(g => g.Round).Distinct().Count();
        if (round >= n - 1) throw new PairingException(PairingError.AllRoundsPlayed);

        var history = PlayerHistory.Build(input);
        var seed = slots.Select((id, index) => (id, index)).Where(x => x.id is not null).ToDictionary(x => x.id!.Value, x => x.index);

        var games = new List<BoardPairing>();
        int? byePlayer = null;

        for (var i = 0; i < n / 2; i++)
        {
            var a = slots[(round + i) % (n - 1)];
            var b = i == 0 ? slots[n - 1] : slots[(round + n - 1 - i) % (n - 1)];

            if (a is null || b is null)
            {
                byePlayer = a ?? b;
                continue;
            }

            var (higher, lower) = seed[a.Value] < seed[b.Value] ? (a.Value, b.Value) : (b.Value, a.Value);
            var board = games.Count + 1;
            var (white, black) = ColorAllocator.Assign(
                higher, history[higher].ColorPreference,
                lower, history[lower].ColorPreference,
                higherWhiteByDefault: board % 2 == 1);
            games.Add(new BoardPairing(board, white, black));
        }

        if (byePlayer is { } bye)
            games.Add(new BoardPairing(games.Count + 1, bye, null));

        return games;
    }
}
