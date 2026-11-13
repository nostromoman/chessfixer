namespace ChessFixer.Core.Pairing;

/// <summary>
/// Competition: everyone plays everyone <c>timesAgainstEachOther</c> times. Each round only the players in
/// <see cref="PairingInput.Players"/> (those who take part) are paired, and only pairs that still have a game to play
/// (see <see cref="CompetitionSchedule"/>). Players who cannot be paired are not in the result: they have no opponent
/// this round (no bye, no points).
/// <para>The pairing is a maximum weight matching with as many games as possible. Between those it prefers, in this
/// order: leaving out the players with the fewest games still to play (and who were left out least often), so nobody
/// ends the season with a pile of games against few opponents; pairs who did not play each other yet, or longest ago
/// (the return game comes late); then colors.</para>
/// <para>When the fixed round robin schedule (circle method, seeded by registration order) has a round that is just as
/// good (as many games, the same players left out), that round is used. So when everyone is there every round, the
/// competition takes the minimal number of rounds.</para>
/// </summary>
/// <param name="timesAgainstEachOther">How many games every pair plays (1 or 2).</param>
/// <param name="participants">Every player in the competition in registration order, also those who withdrew (they
/// keep their place in the fixed schedule).</param>
/// <param name="activeParticipants">The players still in the competition (present or not). Games against players who
/// withdrew are not counted as games still to play.</param>
public sealed class CompetitionPairing(
    int timesAgainstEachOther, IReadOnlyList<int> participants, IReadOnlyCollection<int> activeParticipants)
    : IPairingStrategy
{
    public IReadOnlyList<BoardPairing> PairNextRound(PairingInput input)
    {
        if (input.Players.Count < 2) throw new PairingException(PairingError.NotEnoughPlayers);

        var schedule = new CompetitionSchedule(timesAgainstEachOther, input.History);
        var history = PlayerHistory.Build(input);
        var present = input.Players.Select(p => p.Id).Order().ToList();
        var round = input.History.Select(g => g.Round).DefaultIfEmpty(0).Max() + 1;

        var candidates = new List<(int A, int B)>();
        for (var i = 0; i < present.Count; i++)
        for (var j = i + 1; j < present.Count; j++)
        {
            if (schedule.RemainingBetween(present[i], present[j]) > 0) candidates.Add((present[i], present[j]));
        }

        if (candidates.Count == 0)
        {
            var active = activeParticipants.ToList();
            throw new PairingException(schedule.OpenGames(active).Any()
                ? PairingError.NoGamesBetweenPresentPlayers
                : PairingError.CompetitionComplete);
        }

        var weights = new Weights(schedule, history, present, activeParticipants, participants.Count, round);
        var index = present.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var mate = MaxWeightMatching.Find(
            present.Count,
            candidates.Select(c => new MaxWeightMatching.Edge(index[c.A], index[c.B], weights.Of(c.A, c.B))).ToList(),
            maxCardinality: true);
        var best = Enumerable.Range(0, present.Count)
            .Where(i => mate[i] > i)
            .Select(i => (present[i], present[mate[i]]))
            .ToList();

        // The first round of the fixed schedule that is just as good, if any. It only uses pairs of the current cycle
        // (who met least often), so the return games follow after everyone has played everyone once.
        var cycle = candidates.Min(c => schedule.PlayedBetween(c.A, c.B));
        var fixedRound = FixedScheduleRounds(present.ToHashSet(), schedule, cycle)
            .FirstOrDefault(r => r.Count == best.Count && weights.Major(r) == weights.Major(best));
        var pairs = fixedRound ?? best;

        return ToBoards(pairs, schedule, history, input.Players.ToDictionary(p => p.Id, p => p.Rating));
    }

    /// <summary>
    /// The rounds of the circle method over all participants, each restricted to the pairs that are both present
    /// and have played each other <paramref name="cycle"/> times (so they still have a game to play).
    /// </summary>
    private IEnumerable<List<(int, int)>> FixedScheduleRounds(HashSet<int> present, CompetitionSchedule schedule, int cycle)
    {
        var slots = participants.Cast<int?>().ToList();
        if (slots.Count % 2 == 1) slots.Add(null);
        var n = slots.Count;

        for (var round = 0; round < n - 1; round++)
        {
            var pairs = new List<(int, int)>();
            for (var i = 0; i < n / 2; i++)
            {
                var a = slots[(round + i) % (n - 1)];
                var b = i == 0 ? slots[n - 1] : slots[(round + n - 1 - i) % (n - 1)];
                if (a is { } x && b is { } y && present.Contains(x) && present.Contains(y)
                    && schedule.PlayedBetween(x, y) == cycle)
                    pairs.Add((x, y));
            }
            yield return pairs;
        }
    }

    /// <summary>
    /// Edge weights, built so that a heavier matching is better in this order: the players left out (major), the
    /// time since the pair last met, then colors. The factors make each part outweigh all lower parts together.
    /// </summary>
    private sealed class Weights
    {
        private const int MaxColorScore = 3;

        private readonly CompetitionSchedule _schedule;
        private readonly Dictionary<int, PlayerHistory> _history;
        private readonly Dictionary<int, long> _priority;
        private readonly int _round;
        private readonly long _neverMet;
        private readonly long _spacingFactor;
        private readonly long _priorityFactor;

        public Weights(CompetitionSchedule schedule, Dictionary<int, PlayerHistory> history, IReadOnlyList<int> present,
            IReadOnlyCollection<int> active, int participantCount, int round)
        {
            _schedule = schedule;
            _history = history;
            _round = round;

            // Priority to play: games still to play first, then how often the player had no opponent.
            var timesLeftOut = present.ToDictionary(p => p, p => (long)history[p].NoOpponentCount);
            var leftOutRange = timesLeftOut.Values.Max() + 1;
            _priority = present.ToDictionary(p => p, p => schedule.RemainingFor(p, active) * leftOutRange + timesLeftOut[p]);

            long maxGames = present.Count / 2 + 1;
            _neverMet = participantCount + 1;
            _spacingFactor = (MaxColorScore + 1) * maxGames;
            _priorityFactor = maxGames * (_neverMet * _spacingFactor + MaxColorScore + 1);
        }

        public long Of(int a, int b) =>
            (_priority[a] + _priority[b]) * _priorityFactor + Spacing(a, b) * _spacingFactor + ColorScore(a, b);

        /// <summary>The part of a round's weight that decides who plays: the priority of the players who play.</summary>
        public long Major(IEnumerable<(int A, int B)> pairs) => pairs.Sum(p => _priority[p.A] + _priority[p.B]);

        /// <summary>Rounds since the pair last met (capped); highest when they never met.</summary>
        private long Spacing(int a, int b) =>
            _schedule.LastRoundBetween(a, b) is { } last ? Math.Min(_round - last, _neverMet - 1) : _neverMet;

        /// <summary>3 when both players can get the color they want, less the stronger the conflict.</summary>
        private int ColorScore(int a, int b)
        {
            var prefA = _history[a].ColorPreference;
            var prefB = _history[b].ColorPreference;
            int conflict;
            if (_schedule.NextWhite(a, b) is { } white)
            {
                var (whitePref, blackPref) = white == a ? (prefA, prefB) : (prefB, prefA);
                conflict = Math.Max(
                    whitePref.Color == PieceColor.Black ? whitePref.Strength : 0,
                    blackPref.Color == PieceColor.White ? blackPref.Strength : 0);
            }
            else
                conflict = ColorAllocator.ConflictPenalty(prefA, prefB);
            return MaxColorScore - Math.Min(MaxColorScore, conflict);
        }
    }

    /// <summary>Boards ordered by rating (strongest pair first), with colors: fixed by the schedule when the pair met
    /// before, otherwise by each player's color history.</summary>
    private static List<BoardPairing> ToBoards(List<(int A, int B)> pairs, CompetitionSchedule schedule,
        Dictionary<int, PlayerHistory> history, Dictionary<int, int?> ratings)
    {
        int RatingOf(int id) => ratings[id] ?? 0;

        return pairs
            .Select(p => RatingOf(p.A) > RatingOf(p.B) || (RatingOf(p.A) == RatingOf(p.B) && p.A < p.B) ? p : (A: p.B, B: p.A))
            .OrderByDescending(p => RatingOf(p.A))
            .ThenByDescending(p => RatingOf(p.B))
            .ThenBy(p => p.A)
            .Select((p, i) =>
            {
                var board = i + 1;
                var fixedWhite = schedule.NextWhite(p.A, p.B);
                var (white, black) = ColorAllocator.Assign(
                    p.A, history[p.A].ColorPreference,
                    p.B, history[p.B].ColorPreference,
                    higherWhiteByDefault: board % 2 == 1,
                    fixedWhite is null ? null : fixedWhite == p.A ? PieceColor.White : PieceColor.Black);
                return new BoardPairing(board, white, black);
            })
            .ToList();
    }
}
