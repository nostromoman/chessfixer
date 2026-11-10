namespace ChessFixer.Core.Pairing;

/// <summary>
/// Round robin: everyone plays everyone once. The schedule (who meets whom in which round) follows
/// the circle method, seeded by registration order. Colors are decided per round from each player's
/// color history, so they alternate as much as possible.
/// After the last round of the cycle the schedule simply starts again (everyone meets everyone a second time, etc.).
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

        var history = PlayerHistory.Build(input);
        var slots = Slots(input.Players);

        int TimesMet((int? A, int? B) pair) =>
            pair is { A: { } a, B: { } b } ? history[a].TimesMet.GetValueOrDefault(b) : 0;
        int ByesOf((int? A, int? B) pair) =>
            pair.A is null ? history[pair.B!.Value].ByeCount : pair.B is null ? history[pair.A.Value].ByeCount : 0;
        bool IsNew((int? A, int? B) pair) => pair.A is not null && pair.B is not null && TimesMet(pair) == 0;

        var best = Enumerable.Range(0, slots.Count - 1)
            .Select(round => CircleRound(slots, round))
            .OrderByDescending(pairs => pairs.Count(IsNew))
            .ThenBy(pairs => pairs.Sum(TimesMet))
            .ThenBy(pairs => pairs.Sum(ByesOf))
            .First();

        return ToBoards(best, slots, history);
    }

    /// <summary>The players seeded by registration order; a null "bye" slot is added when the count is odd.</summary>
    private static List<int?> Slots(IEnumerable<PairingPlayer> players)
    {
        var slots = players.Select(p => p.Id).Order().Cast<int?>().ToList();
        if (slots.Count % 2 == 1) slots.Add(null);
        return slots;
    }

    /// <summary>
    /// Round <paramref name="round"/> (0 .. slots-2) of the circle method: the pairs of that round.
    /// One of the two can be null: the other player has a bye.
    /// </summary>
    private static List<(int? A, int? B)> CircleRound(List<int?> slots, int round)
    {
        var n = slots.Count;
        var pairs = new List<(int? A, int? B)>();
        for (var i = 0; i < n / 2; i++)
        {
            var a = slots[(round + i) % (n - 1)];
            var b = i == 0 ? slots[n - 1] : slots[(round + n - 1 - i) % (n - 1)];
            pairs.Add((a, b));
        }
        return pairs;
    }

    /// <summary>Turns the pairs of a round into boards, with colors from each player's history; the bye last.</summary>
    private static IReadOnlyList<BoardPairing> ToBoards(
        List<(int? A, int? B)> pairs, List<int?> slots, Dictionary<int, PlayerHistory> history)
    {
        var seed = slots.Select((id, index) => (id, index)).Where(x => x.id is not null)
            .ToDictionary(x => x.id!.Value, x => x.index);

        var games = new List<BoardPairing>();
        int? byePlayer = null;

        foreach (var (a, b) in pairs)
        {
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
                higherWhiteByDefault: board % 2 == 1,
                history[higher].RematchColorAgainst(lower));
            games.Add(new BoardPairing(board, white, black));
        }

        if (byePlayer is { } bye)
            games.Add(new BoardPairing(games.Count + 1, bye, null));

        return games;
    }
}
