using ChessFixer.Core.Rating;
using ChessFixer.Server.Data;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Ratings;

/// <summary>The tournament (and for one game: the game, as seen by the player) a rating change belongs to.</summary>
/// <param name="Games">For the change of a whole tournament: the number of games.</param>
public record RatedGameInfo(
    int TournamentId, string TournamentTitle, int? Round, Player? Opponent, int? OpponentRating, GameResult? Result,
    bool? IsWhite, int? Games = null);

/// <summary>
/// The rating log: every rating change is written here when it happens (in the same save as the change itself),
/// and the rating page with the trend of the own players.
/// </summary>
public class RatingLogService(ChessFixerDbContext db, TimeProvider time, IHttpContextAccessor http)
{
    public const int TrendLength = 10;
    public const int MaxPageSize = 200;
    private string? _changedBy;
    private bool _changedByLoaded;

    /// <summary>Logs a change of <paramref name="player"/>'s rating from <paramref name="before"/> to its current rating.
    /// Nothing is logged when the rating did not change (except for a new player). Saved with the next SaveChanges.</summary>
    public async Task AddAsync(Player player, int before, RatingChangeReason reason, RatedGameInfo? game = null)
    {
        if (player.Rating == before && reason != RatingChangeReason.NewPlayer) return;
        db.RatingLog.Add(new RatingLogEntry
        {
            At = time.GetUtcNow().UtcDateTime,
            Player = player,
            RatingBefore = before,
            RatingAfter = player.Rating,
            Reason = reason,
            TournamentId = game?.TournamentId,
            TournamentTitle = game?.TournamentTitle,
            Round = game?.Round,
            Opponent = game?.Opponent,
            OpponentRating = game?.OpponentRating,
            Result = game?.Result,
            IsWhite = game?.IsWhite,
            Games = game?.Games,
            ChangedBy = await ChangedByAsync()
        });
    }

    /// <summary>What the K-factor of each player depends on, besides the rating: whether the rating ever was 2400
    /// or more (from the log, so also by hand) and whether they are a junior in the given year.</summary>
    public async Task<Dictionary<int, RatedPlayer>> RatedPlayersAsync(IEnumerable<Player> players, int year)
    {
        var list = players.DistinctBy(p => p.Id).ToList();
        var ids = list.Select(p => p.Id).ToList();
        var reached = (await db.RatingLog
                .Where(e => ids.Contains(e.PlayerId)
                            && (e.RatingBefore >= EloCalculator.MasterRating || e.RatingAfter >= EloCalculator.MasterRating))
                .Select(e => e.PlayerId)
                .Distinct()
                .ToListAsync())
            .ToHashSet();
        return list.ToDictionary(p => p.Id, p => new RatedPlayer(
            p.Rating,
            HasReached2400: p.Rating >= EloCalculator.MasterRating || reached.Contains(p.Id),
            IsJunior: EloCalculator.IsJunior(p.BirthDate, year)));
    }

    /// <summary>The own players (not guests, not deleted) with their rating and its trend, highest rating first.</summary>
    public async Task<List<RatingListEntryDto>> GetRatingListAsync()
    {
        var players = await db.Players
            .Where(p => !p.IsGuest && !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.FirstName, p.LastName, p.Rating, HasPhoto = p.Photo != null, p.PhotoVersion })
            .ToListAsync();
        var ids = players.Select(p => p.Id).ToList();

        var entries = (await db.RatingLog
                .Where(e => ids.Contains(e.PlayerId))
                .Select(e => new { e.Id, e.PlayerId, e.At, e.RatingBefore, e.RatingAfter })
                .ToListAsync())
            .GroupBy(e => e.PlayerId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.At).ThenBy(e => e.Id).ToList());

        var ratedGames = (await db.Games
                .Where(g => g.RatedAt != null)
                .Select(g => new { White = g.White.PlayerId, Black = g.Black!.PlayerId })
                .ToListAsync())
            .SelectMany(g => new[] { g.White, g.Black })
            .CountBy(id => id)
            .ToDictionary();

        var since = time.GetUtcNow().UtcDateTime.AddDays(-30);
        return players
            .Select(p =>
            {
                var log = entries.GetValueOrDefault(p.Id) ?? [];
                var recent = log.TakeLast(TrendLength).ToList();
                var trend = recent.Count == 0 ? [] : recent.Take(1).Select(e => e.RatingBefore)
                    .Concat(recent.Select(e => e.RatingAfter)).ToList();
                if (trend.Count == 0 || trend[^1] != p.Rating) trend.Add(p.Rating);
                var change = log.Where(e => e.At >= since).Sum(e => e.RatingAfter - e.RatingBefore);
                return new RatingListEntryDto(p.Id, p.Name, p.FirstName, p.LastName, p.HasPhoto ? p.PhotoVersion : null,
                    p.Rating, ratedGames.GetValueOrDefault(p.Id), trend, change);
            })
            .OrderByDescending(p => p.Rating).ThenBy(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// The rating history of a player: the chart from the log, and every rated tournament with each game. Games of a
    /// closed tournament show what was stored when the ratings were processed; games of a running tournament show
    /// what they would give if the tournament were closed now.
    /// </summary>
    public async Task<PlayerRatingDto> GetPlayerHistoryAsync(Player player)
    {
        var timeline = await db.RatingLog
            .Where(e => e.PlayerId == player.Id)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new RatingPointDto(e.At, e.RatingBefore, e.RatingAfter, e.Reason, e.TournamentTitle))
            .ToListAsync();

        var tournamentIds = await db.TournamentPlayers
            .Where(tp => tp.PlayerId == player.Id && tp.Tournament.IsRated)
            .Select(tp => tp.TournamentId)
            .ToListAsync();
        var tournaments = await db.Tournaments
            .Where(t => tournamentIds.Contains(t.Id))
            .Include(t => t.Players).ThenInclude(p => p.Player)
            .Include(t => t.Rounds).ThenInclude(r => r.Games)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync();
        var tournamentChanges = await db.RatingLog
            .Where(e => e.PlayerId == player.Id && e.Reason == RatingChangeReason.Tournament && e.TournamentId != null)
            .ToDictionaryAsync(e => e.TournamentId!.Value, e => e.RatingAfter - e.RatingBefore);

        var year = time.GetUtcNow().Year;
        var result = new List<RatingTournamentDto>();
        foreach (var tournament in tournaments)
        {
            // Expected changes of a running tournament: with everyone's current rating.
            var preview = tournament.ClosedAt is null
                ? TournamentRatings.Calculate(tournament, await RatedPlayersAsync(tournament.Players.Select(p => p.Player), year))
                    .ToDictionary(g => g.Game.Id)
                : [];

            var games = new List<RatingGameDto>();
            foreach (var game in tournament.Rounds.Where(r => r.StartedAt != null).OrderBy(r => r.Number)
                         .SelectMany(r => r.Games.OrderBy(g => g.Number)))
            {
                if (game.Black is null || !EloCalculator.IsRated(game.Result)) continue;
                var isWhite = game.White.PlayerId == player.Id;
                if (!isWhite && game.Black.PlayerId != player.Id) continue;

                var me = isWhite ? game.White.Player : game.Black.Player;
                var opponent = isWhite ? game.Black.Player : game.White.Player;
                var score = TournamentRatings.ScoreOf(game.Result, isWhite);
                var round = tournament.Rounds.Single(r => r.Id == game.RoundId).Number;
                var opponentDto = new GamePlayerDto(opponent.Id, opponent.Name, opponent.IsDeleted);

                if (game.IsRated)
                {
                    var rating = (isWhite ? game.WhiteRatingBefore : game.BlackRatingBefore)!.Value;
                    var opponentRating = (isWhite ? game.BlackRatingBefore : game.WhiteRatingBefore)!.Value;
                    games.Add(new RatingGameDto(round, isWhite, opponentDto, game.Result, true, rating, opponentRating, score,
                        EloCalculator.ExpectedScore(rating, opponentRating),
                        isWhite ? game.WhiteK : game.BlackK,
                        isWhite ? game.WhiteRatingChange : game.BlackRatingChange));
                }
                else if (preview.TryGetValue(game.Id, out var calculated))
                {
                    var mine = isWhite ? calculated.White : calculated.Black;
                    games.Add(new RatingGameDto(round, isWhite, opponentDto, game.Result, true, mine.Rating, mine.OpponentRating,
                        score, mine.ExpectedScore, mine.K, mine.ExactChange));
                }
                else
                {
                    // Does not count: a game against (or as) a guest player.
                    games.Add(new RatingGameDto(round, isWhite, opponentDto, game.Result, false, me.Rating, opponent.Rating,
                        score, null, null, null));
                }
            }
            if (games.Count == 0) continue;

            var exact = games.Sum(g => g.Change ?? 0);
            var processed = tournament.ClosedAt is not null;
            var change = tournamentChanges.TryGetValue(tournament.Id, out var logged) ? logged
                : processed ? (int)games.Where(g => g.Change is not null).Sum(g => Math.Round(g.Change!.Value)) // older versions: whole changes per game
                : EloCalculator.Round(exact);
            var counted = games.FirstOrDefault(g => g.CountsForRating);
            var before = counted?.Rating ?? player.Rating;
            var date = tournament.ClosedAt
                       ?? tournament.Rounds.Where(r => r.StartedAt != null).Select(r => r.StartedAt).Min();
            result.Add(new RatingTournamentDto(tournament.Id, tournament.Title, date, processed, before, exact, change, games));
        }

        var ratedGames = result.Where(t => t.IsProcessed).Sum(t => t.Games.Count(g => g.CountsForRating));
        return new PlayerRatingDto(player.Rating, ratedGames, timeline,
            result.OrderBy(t => t.IsProcessed).ThenByDescending(t => t.Date).ToList());
    }

    /// <summary>A page of the log, newest first; optionally of one player only.</summary>
    public async Task<RatingLogPageDto> GetPageAsync(int skip, int take, int? playerId)
    {
        var query = db.RatingLog.AsQueryable();
        if (playerId is { } id) query = query.Where(e => e.PlayerId == id);

        var total = await query.CountAsync();
        var entries = await query
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxPageSize))
            .Select(e => new RatingLogEntryDto(
                e.Id, e.At,
                new GamePlayerDto(e.PlayerId, e.Player.Name, e.Player.IsDeleted),
                e.RatingBefore, e.RatingAfter, e.Reason,
                e.TournamentId, e.TournamentTitle, e.Round,
                e.Opponent == null ? null : new GamePlayerDto(e.Opponent.Id, e.Opponent.Name, e.Opponent.IsDeleted),
                e.OpponentRating, e.Result, e.IsWhite, e.ChangedBy, e.Games))
            .ToListAsync();
        return new RatingLogPageDto(total, entries);
    }

    /// <summary>Display name of the logged-in user; null outside a request (e.g. background work).</summary>
    private async Task<string?> ChangedByAsync()
    {
        if (_changedByLoaded) return _changedBy;
        _changedByLoaded = true;
        if (http.HttpContext?.User is { } user && UserSession.GetUserId(user) is { } userId)
            _changedBy = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).SingleOrDefaultAsync();
        return _changedBy;
    }
}
