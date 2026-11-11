using ChessFixer.Server.Data;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Ratings;

/// <summary>The game a rating change belongs to, as seen by the player whose rating changed.</summary>
public record RatedGameInfo(
    int TournamentId, string TournamentTitle, int Round, Player Opponent, int OpponentRating, GameResult Result, bool IsWhite);

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
            ChangedBy = await ChangedByAsync()
        });
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
                e.OpponentRating, e.Result, e.IsWhite, e.ChangedBy))
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
