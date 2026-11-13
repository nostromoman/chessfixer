using ChessFixer.Server.Data;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services;

/// <summary>Reads and changes the values a new tournament starts with (stored in the AppSettings row).</summary>
public class TournamentDefaultsService(ChessFixerDbContext db)
{
    public async Task<TournamentDefaultsDto> GetAsync() =>
        ToDto(await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == AppSettings.SingletonId) ?? new AppSettings());

    public async Task<TournamentDefaultsDto> UpdateAsync(TournamentDefaultsDto request)
    {
        // Same rules as the settings of a tournament.
        TournamentService.ValidateSettings(request?.ToSettings(PairingSystem.Swiss));

        var settings = await db.AppSettings.SingleOrDefaultAsync(s => s.Id == AppSettings.SingletonId);
        if (settings is null)
        {
            settings = new AppSettings();
            db.AppSettings.Add(settings);
        }

        settings.DefaultGameTimeMinutes = request!.GameTimeMinutes;
        settings.DefaultIncrementSeconds = request.IncrementSeconds;
        settings.DefaultBoards = request.Boards;
        settings.DefaultPointsForWin = request.PointsForWin;
        settings.DefaultPointsForDraw = request.PointsForDraw;
        settings.DefaultPointsForLoss = request.PointsForLoss;
        settings.DefaultPointsForBye = request.PointsForBye;
        settings.DefaultPointsForAbsence = request.PointsForAbsence;
        settings.DefaultIsRated = request.IsRated;
        await db.SaveChangesAsync();
        return ToDto(settings);
    }

    private static TournamentDefaultsDto ToDto(AppSettings s) => new(
        s.DefaultGameTimeMinutes, s.DefaultIncrementSeconds, s.DefaultBoards,
        s.DefaultPointsForWin, s.DefaultPointsForDraw, s.DefaultPointsForLoss,
        s.DefaultPointsForBye, s.DefaultPointsForAbsence, s.DefaultIsRated);
}
