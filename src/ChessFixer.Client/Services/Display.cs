using System.Globalization;
using ChessFixer.Shared.Enums;
using MudBlazor;

namespace ChessFixer.Client.Services;

/// <summary>Formatting helpers shared by the pages.</summary>
public static class Display
{
    /// <summary>1.5 → "1,5", 2 → "2" (current culture).</summary>
    public static string Points(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>Time control as "minutes + increment", e.g. 15 + 10.</summary>
    public static string TimeControl(int gameTimeMinutes, int incrementSeconds) => $"{gameTimeMinutes} + {incrementSeconds}";

    /// <summary>CSS class that colors a result from one player's point of view (win, draw, loss); null without a result.</summary>
    public static string? OutcomeClass(GameResult result, bool isWhite)
    {
        double? whiteScore = result switch
        {
            GameResult.WhiteWins or GameResult.WhiteWinsByForfeit or GameResult.Bye => 1,
            GameResult.BlackWins or GameResult.BlackWinsByForfeit => 0,
            GameResult.Draw => 0.5,
            _ => null
        };
        if (whiteScore is not { } score) return null;
        var own = isWhite ? score : 1 - score;
        return own switch { 1 => "outcome-win", 0 => "outcome-loss", _ => "outcome-draw" };
    }

    public static Color StateColor(TournamentState state) => state switch
    {
        TournamentState.New => Color.Info,
        TournamentState.RoundActive => Color.Success,
        TournamentState.WaitingForNextRound => Color.Warning,
        _ => Color.Default
    };
}
