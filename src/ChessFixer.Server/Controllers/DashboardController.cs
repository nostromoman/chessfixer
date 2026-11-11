using ChessFixer.Server.Data;
using ChessFixer.Server.Services;
using ChessFixer.Server.Services.Ratings;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Controllers;

/// <summary>The overview of the whole system on the home page.</summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController(
    ChessFixerDbContext db, RatingLogService ratings, TournamentService tournaments, TimeProvider time) : ControllerBase
{
    public const int TopPlayerCount = 5;
    public const int RiserCount = 3;
    public const int RecentlyClosedCount = 3;
    public const int Months = 12;

    [HttpGet]
    public async Task<DashboardDto> Get()
    {
        var tournamentCount = await db.Tournaments.CountAsync();
        var closedCount = await db.Tournaments.CountAsync(t => t.ClosedAt != null);
        var guestCount = await db.Players.CountAsync(p => !p.IsDeleted && p.IsGuest);
        var clubCount = await db.Clubs.CountAsync();
        var ratedGames = await db.Games.CountAsync(g => g.RatedAt != null);

        var byResult = await db.Games
            .GroupBy(g => g.Result)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        var results = new DashboardResultsDto(
            byResult.GetValueOrDefault(GameResult.WhiteWins),
            byResult.GetValueOrDefault(GameResult.Draw),
            byResult.GetValueOrDefault(GameResult.BlackWins),
            byResult.GetValueOrDefault(GameResult.WhiteWinsByForfeit) + byResult.GetValueOrDefault(GameResult.BlackWinsByForfeit));

        // The own players, highest rating first (with trend and 30-day change).
        var ratingList = await ratings.GetRatingListAsync();
        int? averageRating = ratingList.Count == 0 ? null : (int)Math.Round(ratingList.Average(p => p.Rating));
        var risers = ratingList.Where(p => p.Change30Days > 0)
            .OrderByDescending(p => p.Change30Days).ThenBy(p => p.Name)
            .Take(RiserCount).ToList();

        return new DashboardDto(
            tournamentCount, closedCount, ratingList.Count, guestCount, clubCount, results.Total, ratedGames, averageRating,
            results, await GamesPerMonthAsync(), ratingList.Take(TopPlayerCount).ToList(), risers,
            await OpenTournamentsAsync(), await RecentlyClosedAsync());
    }

    private async Task<List<DashboardMonthDto>> GamesPerMonthAsync()
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(Months - 1));
        var from = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var started = await db.Games
            .Where(g => g.Result != GameResult.NotPlayed && g.Result != GameResult.Bye
                        && g.Round.StartedAt != null && g.Round.StartedAt >= from)
            .Select(g => g.Round.StartedAt!.Value)
            .ToListAsync();
        var perMonth = started.CountBy(at => new DateOnly(at.Year, at.Month, 1)).ToDictionary();

        return Enumerable.Range(0, Months)
            .Select(i => first.AddMonths(i))
            .Select(month => new DashboardMonthDto(month, perMonth.GetValueOrDefault(month)))
            .ToList();
    }

    private async Task<List<DashboardTournamentDto>> OpenTournamentsAsync()
    {
        var open = await db.Tournaments
            .Where(t => t.ClosedAt == null)
            .OrderByDescending(t => t.Id)
            .Select(t => new
            {
                t.Id, t.Title,
                Players = t.Players.Count(p => p.IsActive),
                Round = t.Rounds.Where(r => r.StartedAt != null).OrderByDescending(r => r.Number)
                    .Select(r => new
                    {
                        r.Number,
                        Games = r.Games.Count(g => g.Result != GameResult.Bye),
                        Finished = r.Games.Count(g => g.Result != GameResult.Bye && g.Result != GameResult.NotPlayed)
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Same rules as TournamentService.StateOf (a closed tournament is not in this list).
        return open.Select(t => new DashboardTournamentDto(
                t.Id, t.Title,
                t.Round is null ? TournamentState.New
                : t.Round.Finished < t.Round.Games ? TournamentState.RoundActive
                : TournamentState.WaitingForNextRound,
                t.Players, t.Round?.Number ?? 0, t.Round?.Finished ?? 0, t.Round?.Games ?? 0))
            .ToList();
    }

    private async Task<List<DashboardClosedTournamentDto>> RecentlyClosedAsync()
    {
        var closed = await db.Tournaments
            .Where(t => t.ClosedAt != null)
            .OrderByDescending(t => t.ClosedAt).ThenByDescending(t => t.Id)
            .Take(RecentlyClosedCount)
            .Select(t => new
            {
                t.Id, t.Title, ClosedAt = t.ClosedAt!.Value,
                Players = t.Players.Count,
                Rounds = t.Rounds.Count(r => r.StartedAt != null)
            })
            .ToListAsync();

        var list = new List<DashboardClosedTournamentDto>();
        foreach (var t in closed)
        {
            var winner = (await tournaments.GetStandingsAsync(t.Id)).FirstOrDefault(s => s.Rank == 1 && s.Played > 0);
            list.Add(new DashboardClosedTournamentDto(t.Id, t.Title, t.ClosedAt, t.Players, t.Rounds, winner?.Name));
        }
        return list;
    }
}
