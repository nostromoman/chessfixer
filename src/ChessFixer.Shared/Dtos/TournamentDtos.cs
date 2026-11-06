using ChessFixer.Shared.Enums;

namespace ChessFixer.Shared.Dtos;

public record TournamentSummaryDto(int Id, string Title, TournamentState State, int PlayerCount, int RoundCount);

public record TournamentDto(
    int Id,
    string Title,
    TournamentState State,
    TournamentSettingsDto Settings,
    int CurrentRound);

public record TournamentSettingsDto(
    PairingSystem PairingSystem,
    int GameTimeMinutes,
    int IncrementSeconds,
    double PointsForWin,
    double PointsForDraw,
    double PointsForLoss,
    double PointsForBye);

public record CreateTournamentRequest(string Title);

public record UpdateTournamentRequest(string Title, TournamentSettingsDto Settings);
