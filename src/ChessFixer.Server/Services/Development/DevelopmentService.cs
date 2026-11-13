using ChessFixer.Server.Data;
using ChessFixer.Server.Hubs;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Development;

/// <summary>
/// Beheer, Development: resets the database to a known starting point for trying things out.
/// </summary>
public class DevelopmentService(ChessFixerDbContext db, IHubContext<TournamentHub> hub, ILogger<DevelopmentService> logger)
{
    /// <summary>
    /// Makes a backup of the database file, then deletes all tournaments, players, clubs and the rating log, and
    /// seeds the clubs and players of <see cref="DevelopmentSeed"/>. Kept: the accounts (system administrator and
    /// tournament leaders), the application settings (AI, tournament defaults, writing styles) and the activity log.
    /// </summary>
    public async Task<DevelopmentResetDto> ResetAsync()
    {
        var backup = await DatabaseInitializer.BackUpAsync(db, "reset", logger);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            // Children before parents (several foreign keys restrict deletes). ExecuteDelete skips the change tracker,
            // so the activity log does not list every deleted row.
            await db.RatingLog.ExecuteDeleteAsync();
            await db.Games.ExecuteDeleteAsync();
            await db.RoundReports.ExecuteDeleteAsync();
            await db.Rounds.ExecuteDeleteAsync();
            await db.TournamentReports.ExecuteDeleteAsync();
            await db.TournamentPlayers.ExecuteDeleteAsync();
            await db.Tournaments.ExecuteDeleteAsync();
            await db.PlayerPhotos.ExecuteDeleteAsync();
            await db.Players.ExecuteDeleteAsync();
            await db.Clubs.ExecuteDeleteAsync();

            var otherClubs = DevelopmentSeed.OtherClubs();
            db.Clubs.Add(DevelopmentSeed.OwnClub());
            db.Clubs.AddRange(otherClubs);
            var players = DevelopmentSeed.Players(otherClubs);
            db.Players.AddRange(players);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            logger.LogWarning("Database reset: {Players} players and {Clubs} clubs seeded.", players.Count, otherClubs.Count + 1);
        }

        await hub.Clients.All.SendAsync(TournamentHubContract.TournamentListChanged);
        return new DevelopmentResetDto(
            await db.Players.CountAsync(), await db.Clubs.CountAsync(), backup is null ? null : Path.GetFileName(backup));
    }
}
