using ChessFixer.Core.Rating;
using ChessFixer.Server.Data;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Services.Ratings;

/// <summary>The calculation of one game of a tournament, for White and Black.</summary>
public record GameRating(Game Game, RatingCalculation White, RatingCalculation Black);

/// <summary>The rating change of one player over a tournament: the exact changes added up, rounded once.</summary>
public record PlayerTournamentRating(int PlayerId, int Games, decimal ExactChange, int Change);

/// <summary>
/// The rating calculation of a tournament, FIDE style: every game is calculated with the ratings the players had
/// before the tournament; per player the exact changes are added up and rounded once. Used when a tournament is
/// closed, and to show the expected changes of a tournament that is still running.
/// </summary>
public static class TournamentRatings
{
    /// <summary>The games that count: played on the board (no forfeit, no bye), without a guest player, and not
    /// rated already (games of older versions were rated right after their result).</summary>
    /// <param name="players">By player id: the players as they were before the tournament.</param>
    public static List<GameRating> Calculate(Tournament tournament, IReadOnlyDictionary<int, RatedPlayer> players)
    {
        if (!tournament.IsRated) return [];
        return tournament.Rounds
            .Where(r => r.StartedAt is not null)
            .OrderBy(r => r.Number)
            .SelectMany(r => r.Games.OrderBy(g => g.Number))
            .Where(g => g.Black is not null && !g.IsRated && Counts(g))
            .Select(g =>
            {
                var white = players[g.White.PlayerId];
                var black = players[g.Black!.PlayerId];
                var whiteScore = ScoreOf(g.Result, isWhite: true);
                return new GameRating(g,
                    EloCalculator.Explain(white, black, whiteScore),
                    EloCalculator.Explain(black, white, 1 - whiteScore));
            })
            .ToList();
    }

    /// <summary>Whether a game counts for the rating: played on the board, without a guest player.</summary>
    public static bool Counts(Game game) =>
        game.Black is not null && EloCalculator.IsRated(game.Result)
        && !game.White.Player.IsGuest && !game.Black.Player.IsGuest;

    public static List<PlayerTournamentRating> Totals(IEnumerable<GameRating> games) =>
        games
            .SelectMany(g => new[] { (PlayerId: g.Game.White.PlayerId, g.White.ExactChange), (PlayerId: g.Game.Black!.PlayerId, g.Black.ExactChange) })
            .GroupBy(x => x.PlayerId)
            .Select(g =>
            {
                var exact = g.Sum(x => x.ExactChange);
                return new PlayerTournamentRating(g.Key, g.Count(), exact, EloCalculator.Round(exact));
            })
            .ToList();

    /// <summary>Score of a player for a rated result: 1, ½ or 0.</summary>
    public static decimal ScoreOf(GameResult result, bool isWhite)
    {
        var white = result switch
        {
            GameResult.WhiteWins => 1m,
            GameResult.Draw => 0.5m,
            _ => 0m
        };
        return isWhite ? white : 1 - white;
    }
}
