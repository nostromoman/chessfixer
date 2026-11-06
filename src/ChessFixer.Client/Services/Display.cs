using System.Globalization;
using ChessFixer.Shared.Enums;
using MudBlazor;

namespace ChessFixer.Client.Services;

/// <summary>Formatting helpers shared by the pages.</summary>
public static class Display
{
    /// <summary>1.5 → "1,5", 2 → "2" (current culture).</summary>
    public static string Points(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    public static Color StateColor(TournamentState state) => state switch
    {
        TournamentState.New => Color.Info,
        TournamentState.RoundActive => Color.Success,
        TournamentState.WaitingForNextRound => Color.Warning,
        _ => Color.Default
    };
}
