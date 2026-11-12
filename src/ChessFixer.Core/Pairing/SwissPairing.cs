using System.Numerics;

namespace ChessFixer.Core.Pairing;

/// <summary>
/// Swiss pairing, simplified Dutch system:
/// <list type="bullet">
/// <item>Players are ranked by score, then rating, then registration order.</item>
/// <item>Within a score group the top half plays the bottom half; a player left over floats to the next group.</item>
/// <item>Players do not meet twice (backtracking makes sure the whole round still works out). Only when that is
/// impossible (e.g. everyone has already played everyone) players meet again: first at most twice, then at most
/// three times, and so on. At such a level as few pairs as possible meet that often (e.g. as few third meetings
/// as possible).</item>
/// <item>Color preferences are taken into account when choosing between equally good opponents.</item>
/// <item>With an odd number of players, the lowest-ranked player without an earlier bye gets the bye.</item>
/// </list>
/// </summary>
public sealed class SwissPairing : IPairingStrategy
{
    private const int MaxPlayers = 64;

    public IReadOnlyList<BoardPairing> PairNextRound(PairingInput input)
    {
        if (input.Players.Count < 2) throw new PairingException(PairingError.NotEnoughPlayers);
        if (input.Players.Count > MaxPlayers) throw new PairingException(PairingError.TooManyPlayers);

        var history = PlayerHistory.Build(input);
        var ranked = input.Players
            .OrderByDescending(p => history[p.Id].Score)
            .ThenByDescending(p => p.Rating ?? int.MinValue)
            .ThenBy(p => p.Id)
            .Select(p => p.Id)
            .ToArray();

        var everyone = ranked.Length == 64 ? ulong.MaxValue : (1UL << ranked.Length) - 1;

        // Allow pairs that met at most maxTimesMet times; raise the limit until the round works out.
        // Above 0, also limit how many pairs meet exactly maxTimesMet times, so as few as possible do.
        // With a limit above every meeting count all pairs are allowed, so that always succeeds.
        var mostMeetings = history.Values.SelectMany(h => h.TimesMet.Values).DefaultIfEmpty(0).Max();
        List<(int Higher, int Lower)>? pairs = null;
        int? byeIndex = null;
        for (var maxTimesMet = 0; pairs is null && maxTimesMet <= mostMeetings; maxTimesMet++)
        {
            int[] budgets = maxTimesMet == 0 ? [int.MaxValue] : [.. Enumerable.Range(1, ranked.Length / 2)];
            foreach (var budget in budgets)
            {
                (pairs, byeIndex) = TrySolve(new Solver(ranked, history, maxTimesMet, budget), ranked, history, everyone);
                if (pairs is not null) break;
            }
        }

        if (pairs is null) throw new PairingException(PairingError.NoValidPairing);

        var score = (int index) => history[ranked[index]].Score;
        var boards = pairs
            .OrderByDescending(p => Math.Max(score(p.Higher), score(p.Lower)))
            .ThenByDescending(p => score(p.Higher) + score(p.Lower))
            .ThenBy(p => p.Higher)
            .Select((pair, i) =>
            {
                var board = i + 1;
                var (white, black) = ColorAllocator.Assign(
                    ranked[pair.Higher], history[ranked[pair.Higher]].ColorPreference,
                    ranked[pair.Lower], history[ranked[pair.Lower]].ColorPreference,
                    higherWhiteByDefault: board % 2 == 1,
                    history[ranked[pair.Higher]].RematchColorAgainst(ranked[pair.Lower]));
                return new BoardPairing(board, white, black);
            })
            .ToList();

        if (byeIndex is { } bye)
            boards.Add(new BoardPairing(boards.Count + 1, ranked[bye], null));

        return boards;
    }

    private static (List<(int Higher, int Lower)>? Pairs, int? ByeIndex) TrySolve(
        Solver solver, int[] ranked, Dictionary<int, PlayerHistory> history, ulong everyone)
    {
        if (ranked.Length % 2 == 0) return (solver.Solve(everyone), null);

        foreach (var candidate in ByeCandidates(ranked, history))
        {
            var pairs = solver.Solve(everyone & ~(1UL << candidate));
            if (pairs is not null) return (pairs, candidate);
        }
        return (null, null);
    }

    /// <summary>Players with the fewest byes first; among them the lowest-ranked first.</summary>
    private static IEnumerable<int> ByeCandidates(int[] ranked, Dictionary<int, PlayerHistory> history) =>
        Enumerable.Range(0, ranked.Length)
            .Reverse()
            .OrderBy(i => history[ranked[i]].ByeCount);

    /// <summary>
    /// Depth-first search for a perfect matching. Players are referred to by their index in the ranking;
    /// the set of unpaired players is a bit mask. Masks that cannot be paired are remembered.
    /// </summary>
    /// <param name="maxTimesMet">Only players who met at most this often may be paired (0: never met).</param>
    /// <param name="budget">How many pairs may have met exactly <paramref name="maxTimesMet"/> times (when that is above 0).</param>
    private sealed class Solver(int[] ranked, Dictionary<int, PlayerHistory> history, int maxTimesMet, int budget)
    {
        private const double Tolerance = 1e-9;
        private readonly HashSet<(ulong Unpaired, int Budget)> _unsolvable = [];
        private readonly Stack<(int Higher, int Lower)> _pairs = new();

        public List<(int Higher, int Lower)>? Solve(ulong unpaired)
        {
            _pairs.Clear();
            return TryPair(unpaired, budget) ? _pairs.Reverse().ToList() : null;
        }

        private bool TryPair(ulong unpaired, int budgetLeft)
        {
            if (unpaired == 0) return true;
            if (_unsolvable.Contains((unpaired, budgetLeft))) return false;

            var player = BitOperations.TrailingZeroCount(unpaired);
            var others = unpaired & ~(1UL << player);
            var me = history[ranked[player]];

            foreach (var opponent in Candidates(player, others))
            {
                var atLimit = maxTimesMet > 0 && me.TimesMet.GetValueOrDefault(ranked[opponent]) == maxTimesMet;
                if (atLimit && budgetLeft == 0) continue;

                _pairs.Push((player, opponent));
                if (TryPair(others & ~(1UL << opponent), atLimit ? budgetLeft - 1 : budgetLeft)) return true;
                _pairs.Pop();
            }

            _unsolvable.Add((unpaired, budgetLeft));
            return false;
        }

        /// <summary>Opponents for <paramref name="player"/> (the highest-ranked unpaired player), best first.</summary>
        private IEnumerable<int> Candidates(int player, ulong others)
        {
            var me = history[ranked[player]];
            var available = Indices(others).ToList();

            // Score group of the player (without the player itself). In a group of m players, the
            // player at the top should meet the player at position m/2: top half plays bottom half.
            var group = available.Where(i => Math.Abs(Score(i) - me.Score) < Tolerance).ToList();
            var idealPosition = (group.Count + 1) / 2 - 1;

            return available
                .Where(i => me.TimesMet.GetValueOrDefault(ranked[i]) <= maxTimesMet)
                .OrderBy(i => me.TimesMet.GetValueOrDefault(ranked[i]))
                .ThenBy(i => Math.Round(Math.Abs(Score(i) - me.Score), 6))
                .ThenBy(i => ColorAllocator.ConflictPenalty(me.ColorPreference, history[ranked[i]].ColorPreference))
                .ThenBy(i => group.IndexOf(i) is var position and >= 0 ? Math.Abs(position - idealPosition) : 0)
                .ThenBy(i => i);
        }

        private double Score(int index) => history[ranked[index]].Score;

        private static IEnumerable<int> Indices(ulong mask)
        {
            while (mask != 0)
            {
                var index = BitOperations.TrailingZeroCount(mask);
                yield return index;
                mask &= mask - 1;
            }
        }
    }
}
