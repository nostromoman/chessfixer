namespace ChessFixer.Core.Pairing;

/// <summary>A game a pair still has to play in a competition.</summary>
/// <param name="WhiteId">Who has White in it; null when either player may have White (e.g. when they play each other once).</param>
public record OpenGame(int PlayerA, int PlayerB, int? WhiteId);

/// <summary>
/// Which games every pair of a competition has played and still has to play. Every pair plays
/// <see cref="TimesAgainstEachOther"/> games; with 2 (or any even number) each player has White equally often against
/// the other. Every game between two players uses up one of those games, whatever its result: a forfeit counts as
/// played, and so does a game that is still being played. Byes, absences and rounds without opponent do not count.
/// </summary>
public sealed class CompetitionSchedule
{
    private readonly Dictionary<(int, int), List<(int Round, int WhiteId)>> _games = [];

    public int TimesAgainstEachOther { get; }

    public CompetitionSchedule(int timesAgainstEachOther, IEnumerable<PlayedGame> history)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timesAgainstEachOther, 1);
        TimesAgainstEachOther = timesAgainstEachOther;

        foreach (var game in history)
        {
            if (game.BlackId is not { } blackId) continue;
            var key = Key(game.WhiteId, blackId);
            if (!_games.TryGetValue(key, out var games)) _games[key] = games = [];
            games.Add((game.Round, game.WhiteId));
        }
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    private IReadOnlyList<(int Round, int WhiteId)> GamesBetween(int a, int b) =>
        _games.TryGetValue(Key(a, b), out var games) ? games : [];

    public int PlayedBetween(int a, int b) => GamesBetween(a, b).Count;

    public int RemainingBetween(int a, int b) => a == b ? 0 : Math.Max(0, TimesAgainstEachOther - PlayedBetween(a, b));

    /// <summary>The round of their most recent game, or null when they have not played each other.</summary>
    public int? LastRoundBetween(int a, int b) =>
        GamesBetween(a, b) is { Count: > 0 } games ? games.Max(g => g.Round) : null;

    /// <summary>The games the two players still have to play, with the color when it is fixed.</summary>
    public IEnumerable<OpenGame> OpenGamesBetween(int a, int b)
    {
        var remaining = RemainingBetween(a, b);
        if (remaining == 0) yield break;

        // Each player gets White in half of the games (rounded down); with an odd number one game is free.
        var games = GamesBetween(a, b);
        var whitesEach = TimesAgainstEachOther / 2;
        var aWhite = Math.Max(0, whitesEach - games.Count(g => g.WhiteId == a));
        var bWhite = Math.Max(0, whitesEach - games.Count(g => g.WhiteId == b));
        var (first, second) = a < b ? (a, b) : (b, a);
        for (var i = 0; i < remaining; i++)
            yield return new OpenGame(first, second,
                i < aWhite ? a : i < aWhite + bWhite ? b : null);
    }

    /// <summary>
    /// Who must have White in the next game between the two: the one who had White less often against the other
    /// (null when even). With two games, each of them thus has White once.
    /// </summary>
    public int? NextWhite(int a, int b)
    {
        var games = GamesBetween(a, b);
        var balance = games.Count(g => g.WhiteId == a) - games.Count(g => g.WhiteId == b);
        return balance switch { > 0 => b, < 0 => a, _ => null };
    }

    /// <summary>All games still to play between the given players.</summary>
    public IEnumerable<OpenGame> OpenGames(IReadOnlyList<int> players)
    {
        for (var i = 0; i < players.Count; i++)
        for (var j = i + 1; j < players.Count; j++)
        foreach (var game in OpenGamesBetween(players[i], players[j]))
            yield return game;
    }

    /// <summary>How many games the player still has to play against the given opponents.</summary>
    public int RemainingFor(int player, IEnumerable<int> opponents) =>
        opponents.Where(o => o != player).Sum(o => RemainingBetween(player, o));
}
